using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrafficSimulation.Core.Events;
using TrafficSimulation.Core.Models;
using TrafficSimulation.Core.Repositories;
using TrafficSimulation.Core.Services;
using TrafficSimulation.Infrastructure.Services;

namespace TrafficSimulation.Application.Services
{
    public class TrafficSimulationService : ITrafficSimulationService
    {
        private readonly IRoadNetworkRepository _networkRepository;
        private readonly ISimulationRepository _simulationRepository;
        private readonly IStatisticsCalculator _statisticsCalculator;

        private SimulationSession _currentSession;
        private SimulationState _state = SimulationState.Stopped;
        private System.Threading.Timer _simulationTimer;
        private DateTime _lastUpdateTime;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isInitialized = false;

        public event EventHandler<SimulationStepEventArgs> SimulationStepCompleted;
        public event EventHandler<SimulationIncidentEventArgs> IncidentOccurred;
        public event EventHandler<SimulationStateChangedEventArgs> StateChanged;

        public TrafficSimulationService(
            IRoadNetworkRepository networkRepository,
            ISimulationRepository simulationRepository,
            IStatisticsCalculator statisticsCalculator)
        {
            _networkRepository = networkRepository ?? throw new ArgumentNullException(nameof(networkRepository));
            _simulationRepository = simulationRepository ?? throw new ArgumentNullException(nameof(simulationRepository));
            _statisticsCalculator = statisticsCalculator ?? new StatisticsCalculator();

            // Инициализация таймера
            _simulationTimer = new System.Threading.Timer(SimulationTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
        }

        public async Task<SimulationSession> CreateSessionAsync(
            string name,
            Guid networkId,
            SimulationParameters parameters)
        {
            try
            {
                if (string.IsNullOrEmpty(name))
                    throw new ArgumentException("Имя сессии не может быть пустым", nameof(name));

                if (parameters == null)
                    throw new ArgumentNullException(nameof(parameters));

                // Получаем дорожную сеть
                var network = await _networkRepository.GetByIdAsync(networkId);
                if (network == null)
                    throw new ArgumentException($"Дорожная сеть с ID {networkId} не найдена");

                // Создаем новую сессию
                _currentSession = new SimulationSession(name, networkId, parameters)
                {
                    Network = network,
                    StartTime = DateTime.Now,
                    State = SimulationState.Stopped,
                    CurrentTime = 0,
                    StepCount = 0
                };

                // Инициализация начального трафика
                InitializeTraffic(_currentSession, parameters);

                // Сохраняем сессию
                await _simulationRepository.SaveSessionAsync(_currentSession);

                _isInitialized = true;

                return _currentSession;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Ошибка создания сессии: {ex.Message}", ex);
            }
        }

        public async Task<bool> StartSimulationAsync(Guid sessionId)
        {
            try
            {
                if (_state != SimulationState.Stopped && _currentSession != null)
                    return false;

                // Если сессия не загружена или другая сессия
                if (_currentSession == null || _currentSession.Id != sessionId)
                {
                    _currentSession = await _simulationRepository.GetSessionAsync(sessionId);
                    if (_currentSession == null)
                        return false;
                }

                // Проверяем, что сессия инициализирована
                if (!_isInitialized)
                {
                    // Загружаем сеть, если ее нет
                    if (_currentSession.Network == null && _currentSession.NetworkId != Guid.Empty)
                    {
                        _currentSession.Network = await _networkRepository.GetByIdAsync(_currentSession.NetworkId);
                    }

                    if (_currentSession.Network == null)
                        throw new InvalidOperationException("Дорожная сеть не загружена");
                }

                // Начинаем симуляцию
                _currentSession.Start();
                _currentSession.State = SimulationState.Running;
                _state = SimulationState.Running;

                _cancellationTokenSource = new CancellationTokenSource();
                _lastUpdateTime = DateTime.Now;

                // Запускаем таймер симуляции (асинхронный цикл вместо Timer)
                _cancellationTokenSource = new CancellationTokenSource();
                _ = RunSimulationAsync(_cancellationTokenSource.Token);

                await _simulationRepository.SaveSessionAsync(_currentSession);
                OnStateChanged(SimulationState.Stopped, SimulationState.Running);

                return true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Ошибка запуска симуляции: {ex.Message}", ex);
            }
        }

        private async Task RunSimulationAsync(CancellationToken cancellationToken)
        {
            while (_state == SimulationState.Running && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var timeSinceLastUpdate = (DateTime.Now - _lastUpdateTime).TotalSeconds;
                    var timeStep = _currentSession.Parameters.TimeStepSeconds;

                    if (timeSinceLastUpdate >= timeStep)
                    {
                        await PerformSimulationStepAsync(_currentSession);
                        _lastUpdateTime = DateTime.Now;
                    }

                    await Task.Delay(100, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Simulation error: {ex.Message}");
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }

        private async Task PerformSimulationStepAsync(SimulationSession session)
        {
            if (session == null || session.State != SimulationState.Running)
                return;

            try
            {
                // 1. Генерация нового трафика
                await GenerateNewTrafficAsync(session);

                // 2. Обновление позиций транспортных средств
                await UpdateVehiclePositionsAsync(session);

                // 3. Обновление позиций пешеходов
                await UpdatePedestrianPositionsAsync(session);

                // 4. Расчет плотности дорог
                CalculateEdgeDensities(session);

                // 5. Обновление светофоров
                UpdateTrafficLights(session);

                // 6. Обнаружение конфликтов и ДТП
                var incidents = DetectConflicts(session);

                // 7. Добавление инцидентов в сессию
                foreach (var incident in incidents)
                {
                    session.AddIncident(incident);
                    OnIncidentOccurred(incident, session.CurrentTime);
                }

                // 8. Расчет статистики
                var metric = _statisticsCalculator.CalculateMetrics(session);
                session.AddMetric(metric);

                // 9. Обновление времени
                session.StepCount++;
                session.CurrentTime += session.Parameters.TimeStepSeconds;

                // 10. Сохранение сессии
                await _simulationRepository.SaveSessionAsync(session);

                // 11. Вызов события завершения шага
                OnSimulationStepCompleted(session.StepCount, session.CurrentTime, metric);

                // 12. Проверка завершения симуляции
                if (session.CurrentTime >= session.Parameters.SimulationDurationSeconds)
                {
                    await StopSimulationAsync(session.Id);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Ошибка выполнения шага симуляции: {ex.Message}", ex);
            }
        }

        public async Task<bool> PauseSimulationAsync(Guid sessionId)
        {
            if (_state != SimulationState.Running || _currentSession?.Id != sessionId)
                return false;

            _state = SimulationState.Paused;
            _currentSession.State = SimulationState.Paused;

            await _simulationRepository.SaveSessionAsync(_currentSession);
            OnStateChanged(SimulationState.Running, SimulationState.Paused);

            return true;
        }

        public async Task<bool> ResumeSimulationAsync(Guid sessionId)
        {
            if (_state != SimulationState.Paused || _currentSession?.Id != sessionId)
                return false;

            _state = SimulationState.Running;
            _currentSession.State = SimulationState.Running;

            _lastUpdateTime = DateTime.Now;

            // Возобновляем симуляцию
            _cancellationTokenSource = new CancellationTokenSource();
            _ = RunSimulationAsync(_cancellationTokenSource.Token);

            await _simulationRepository.SaveSessionAsync(_currentSession);
            OnStateChanged(SimulationState.Paused, SimulationState.Running);

            return true;
        }

        public async Task<bool> StopSimulationAsync(Guid sessionId)
        {
            if (_currentSession?.Id != sessionId)
                return false;

            _state = SimulationState.Stopped;
            _currentSession.State = SimulationState.Stopped;
            _currentSession.EndTime = DateTime.Now;

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            await _simulationRepository.SaveSessionAsync(_currentSession);
            OnStateChanged(SimulationState.Running, SimulationState.Stopped);

            return true;
        }

        public async Task<SimulationStepResult> SimulateStepAsync(Guid sessionId)
        {
            if (_currentSession?.Id != sessionId)
                throw new InvalidOperationException("Session not found or not active");

            var stepResult = new SimulationStepResult();

            try
            {
                // 1. Генерация нового трафика
                await GenerateNewTrafficAsync(_currentSession);

                // 2. Обновление позиций транспортных средств
                await UpdateVehiclePositionsAsync(_currentSession);

                // 3. Обновление позиций пешеходов
                await UpdatePedestrianPositionsAsync(_currentSession);

                // 4. Расчет плотности дорог
                CalculateEdgeDensities(_currentSession);

                // 5. Обновление светофоров
                UpdateTrafficLights(_currentSession);

                // 6. Обнаружение конфликтов и ДТП
                stepResult.Incidents = DetectConflicts(_currentSession);

                // 7. Расчет статистики
                stepResult.AverageVehicleSpeed = _currentSession.CalculateAverageVehicleSpeed();
                stepResult.CongestionLevel = _currentSession.Network.Edges.Any()
                    ? _currentSession.Network.Edges.Average(e => e.CongestionLevel)
                    : 0;

                // 8. Сохраняем метрики
                var metric = _statisticsCalculator.CalculateMetrics(_currentSession);
                _currentSession.AddMetric(metric);

                // 9. Обновление времени
                _currentSession.StepCount++;
                _currentSession.CurrentTime += _currentSession.Parameters.TimeStepSeconds;

                await _simulationRepository.SaveSessionAsync(_currentSession);

                // 10. Вызываем события
                OnSimulationStepCompleted(_currentSession.StepCount, _currentSession.CurrentTime, metric);

                foreach (var incident in stepResult.Incidents)
                {
                    OnIncidentOccurred(incident, _currentSession.CurrentTime);
                }

                // 11. Проверяем завершение симуляции
                if (_currentSession.CurrentTime >= _currentSession.Parameters.SimulationDurationSeconds)
                {
                    await StopSimulationAsync(sessionId);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Ошибка выполнения шага симуляции: {ex.Message}", ex);
            }

            return stepResult;
        }

        private void InitializeTraffic(SimulationSession session, SimulationParameters parameters)
        {
            if (session?.Network == null) return;

            var random = new Random();

            // Инициализация транспортных средств
            for (int i = 0; i < parameters.InitialVehicles; i++)
            {
                try
                {
                    var vehicleType = GetRandomVehicleType(parameters, random);
                    var driverStyle = GetRandomDriverStyle(parameters, random);

                    var vehicle = new Vehicle(vehicleType, driverStyle);

                    // Выбираем случайное ребро
                    var edges = session.Network.Edges.Where(e => !e.IsBlocked).ToList();
                    if (edges.Any())
                    {
                        var edge = edges[random.Next(edges.Count)];
                        vehicle.CurrentEdgeId = edge.Id;
                        vehicle.Position = new Position
                        {
                            EdgeId = edge.Id,
                            Offset = random.NextDouble() * edge.Length * 0.8
                        };

                        edge.Vehicles.Add(vehicle);
                        session.Vehicles.Add(vehicle);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка инициализации ТС: {ex.Message}");
                }
            }

            // Инициализация пешеходов
            for (int i = 0; i < parameters.InitialPedestrians; i++)
            {
                try
                {
                    var pedestrianType = GetRandomPedestrianType(random);
                    var pedestrian = new Pedestrian(pedestrianType);

                    // Выбираем случайную вершину
                    if (session.Network.Vertices.Any())
                    {
                        var vertex = session.Network.Vertices[random.Next(session.Network.Vertices.Count)];
                        pedestrian.Position = new Position { EdgeId = vertex.Id, Offset = 0 };
                        session.Pedestrians.Add(pedestrian);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка инициализации пешехода: {ex.Message}");
                }
            }
        }

        private VehicleType GetRandomVehicleType(SimulationParameters parameters, Random random)
        {
            return WeightedRandomSelection(parameters.VehicleTypeDistribution, random);
        }

        private DriverStyle GetRandomDriverStyle(SimulationParameters parameters, Random random)
        {
            return WeightedRandomSelection(parameters.DriverStyleDistribution, random);
        }

        private PedestrianType GetRandomPedestrianType(Random random)
        {
            return random.NextDouble() switch
            {
                < 0.6 => PedestrianType.Adult,
                < 0.75 => PedestrianType.Child,
                < 0.95 => PedestrianType.Elderly,
                _ => PedestrianType.Disabled
            };
        }

        private T WeightedRandomSelection<T>(Dictionary<T, double> distribution, Random random)
        {
            var totalWeight = distribution.Values.Sum();
            var randomValue = random.NextDouble() * totalWeight;

            foreach (var (key, weight) in distribution)
            {
                randomValue -= weight;
                if (randomValue <= 0)
                    return key;
            }

            return distribution.Keys.First();
        }

        private async Task GenerateNewTrafficAsync(SimulationSession session)
        {
            var parameters = session.Parameters;
            var intensityFactor = parameters.GetCurrentIntensityFactor();
            var random = new Random();

            // Генерация новых транспортных средств
            double vehicleProb = parameters.VehicleIntensity * intensityFactor * (parameters.TimeStepSeconds / 60.0);
            if (random.NextDouble() < vehicleProb && session.Vehicles.Count < 1000)
            {
                var vehicleType = WeightedRandomSelection(parameters.VehicleTypeDistribution, random);
                var driverStyle = WeightedRandomSelection(parameters.DriverStyleDistribution, random);

                var vehicle = new Vehicle(vehicleType, driverStyle);

                var availableEdges = session.Network.Edges
                    .Where(e => !e.IsBlocked && e.Vehicles.Count < 50)
                    .ToList();

                if (availableEdges.Any())
                {
                    var edge = availableEdges[random.Next(availableEdges.Count)];
                    vehicle.CurrentEdgeId = edge.Id;
                    vehicle.Position = new Position { EdgeId = edge.Id, Offset = 0 };
                    edge.Vehicles.Add(vehicle);
                    session.Vehicles.Add(vehicle);
                }
            }

            // Генерация новых пешеходов
            double pedestrianProb = parameters.PedestrianIntensity * intensityFactor * (parameters.TimeStepSeconds / 60.0);
            if (random.NextDouble() < pedestrianProb && session.Pedestrians.Count < 500)
            {
                var pedestrianType = random.NextDouble() switch
                {
                    < 0.6 => PedestrianType.Adult,
                    < 0.75 => PedestrianType.Child,
                    < 0.95 => PedestrianType.Elderly,
                    _ => PedestrianType.Disabled
                };

                var pedestrian = new Pedestrian(pedestrianType);

                if (session.Network.Vertices.Any())
                {
                    var vertex = session.Network.Vertices[random.Next(session.Network.Vertices.Count)];
                    pedestrian.Position = new Position { EdgeId = vertex.Id, Offset = 0 };
                }

                session.Pedestrians.Add(pedestrian);
            }
        }

        private async Task UpdateVehiclePositionsAsync(SimulationSession session)
        {
            var vehiclesToRemove = new List<Vehicle>();
            var random = new Random();

            foreach (var vehicle in session.Vehicles)
            {
                var edge = session.Network.Edges.FirstOrDefault(e => e.Id == vehicle.CurrentEdgeId);
                if (edge == null || edge.IsBlocked)
                {
                    vehicle.WaitingTime += session.Parameters.TimeStepSeconds;
                    vehicle.CurrentSpeed = 0;
                    continue;
                }

                // Расчет плотности на ребре
                double density = CalculateEdgeDensity(edge);

                // Обновление скорости
                vehicle.UpdateSpeed(density, edge.MaxSpeed, session.Parameters.TimeStepSeconds);

                // Расчет пройденного расстояния
                double distanceMoved = vehicle.CurrentSpeed * (session.Parameters.TimeStepSeconds / 3.6);

                // Обновление позиции
                vehicle.Position.Offset += distanceMoved;
                vehicle.DistanceTraveled += distanceMoved / 1000;
                vehicle.TotalTravelTime += session.Parameters.TimeStepSeconds;

                // Проверка достижения конца ребра
                if (vehicle.Position.Offset >= edge.Length)
                {
                    // Переход на следующее ребро
                    var nextVertexId = edge.EndVertexId;
                    var nextVertex = session.Network.Vertices.First(v => v.Id == nextVertexId);

                    // Поиск следующего ребра
                    var outgoingEdges = session.Network.Edges
                        .Where(e => e.StartVertexId == nextVertexId && !e.IsBlocked)
                        .ToList();

                    if (outgoingEdges.Any() && random.NextDouble() < 0.8)
                    {
                        var sameTypeEdges = outgoingEdges
                            .Where(e => e.Type == edge.Type)
                            .ToList();

                        RoadSegment nextEdge;
                        if (sameTypeEdges.Any())
                            nextEdge = sameTypeEdges[random.Next(sameTypeEdges.Count)];
                        else
                            nextEdge = outgoingEdges[random.Next(outgoingEdges.Count)];

                        // Переход на новое ребро
                        vehicle.CurrentEdgeId = nextEdge.Id;
                        vehicle.Position.EdgeId = nextEdge.Id;
                        vehicle.Position.Offset = vehicle.Position.Offset - edge.Length;

                        // Обновление списков транспортных средств
                        edge.Vehicles.Remove(vehicle);
                        nextEdge.Vehicles.Add(vehicle);
                    }
                    else
                    {
                        // Конец маршрута
                        vehicle.DestinationReached = true;
                        vehiclesToRemove.Add(vehicle);
                        edge.Vehicles.Remove(vehicle);
                    }
                }
            }

            // Удаление транспортных средств, достигших пункта назначения
            foreach (var vehicle in vehiclesToRemove)
            {
                session.Vehicles.Remove(vehicle);
                session.CompletedVehiclesCount++;
            }
        }

        private async Task UpdatePedestrianPositionsAsync(SimulationSession session)
        {
            var pedestriansToRemove = new List<Pedestrian>();
            var random = new Random();

            foreach (var pedestrian in session.Pedestrians)
            {
                var currentVertexId = pedestrian.Position.EdgeId;
                var currentVertex = session.Network.Vertices.FirstOrDefault(v => v.Id == currentVertexId);

                if (currentVertex == null)
                    continue;

                if (pedestrian.IsMoving)
                {
                    var moveProbability = pedestrian.Type switch
                    {
                        PedestrianType.Child => 0.6,
                        PedestrianType.Elderly => 0.25,
                        PedestrianType.Disabled => 0.15,
                        _ => 0.4
                    };

                    if (random.NextDouble() < moveProbability)
                    {
                        var outgoingEdges = session.Network.Edges
                            .Where(e => e.StartVertexId == currentVertexId)
                            .ToList();

                        if (outgoingEdges.Any())
                        {
                            var edge = outgoingEdges[random.Next(outgoingEdges.Count)];
                            var newVertexId = edge.EndVertexId;

                            if (edge.HasCrosswalk)
                            {
                                var density = CalculateEdgeDensity(edge);
                                var safetyThreshold = 0.3 + random.NextDouble() * 0.2;

                                if (density < safetyThreshold || random.NextDouble() < 0.1)
                                {
                                    pedestrian.Position.EdgeId = newVertexId;
                                    pedestrian.WaitingTime = 0;
                                    pedestrian.DistanceTraveled += 0.1;
                                }
                                else
                                {
                                    pedestrian.WaitingTime += session.Parameters.TimeStepSeconds;
                                    if (pedestrian.WaitingTime > pedestrian.Patience)
                                    {
                                        pedestrian.PanicLevel = Math.Min(1.0, pedestrian.PanicLevel + 0.05);
                                    }
                                }
                            }
                            else
                            {
                                pedestrian.Position.EdgeId = newVertexId;
                                pedestrian.DistanceTraveled += 0.1;
                            }
                        }
                    }
                }

                var localDensity = session.Pedestrians.Count(p =>
                    p.Position.EdgeId == pedestrian.Position.EdgeId) / 10.0;

                pedestrian.UpdateSpeed(localDensity, session.Parameters.TimeStepSeconds);

                if (random.NextDouble() < 0.02)
                {
                    pedestrian.DestinationReached = true;
                    pedestriansToRemove.Add(pedestrian);
                    session.CompletedPedestriansCount++;
                }
            }

            foreach (var pedestrian in pedestriansToRemove)
            {
                session.Pedestrians.Remove(pedestrian);
            }
        }

        private double CalculateEdgeDensity(RoadSegment edge)
        {
            if (!edge.Vehicles.Any())
            {
                edge.Density = 0;
                edge.CongestionLevel = 0;
                return 0;
            }

            var pceFactors = new Dictionary<VehicleType, double>
            {
                [VehicleType.Car] = 1.0,
                [VehicleType.NoviceCar] = 1.1,
                [VehicleType.Bus] = 2.5,
                [VehicleType.Truck] = 3.0,
                [VehicleType.Special] = 1.8,
                [VehicleType.Bicycle] = 0.5
            };

            double totalPce = edge.Vehicles.Sum(v => pceFactors.GetValueOrDefault(v.Type, 1.0));
            double lengthKm = edge.Length / 1000;

            if (lengthKm == 0 || edge.Lanes == 0)
            {
                edge.Density = 0;
                edge.CongestionLevel = 0;
                return 0;
            }

            double density = totalPce / lengthKm / edge.Lanes;
            edge.Density = density;

            if (density > 0.7)
            {
                edge.CongestionLevel = Math.Min(1.0, (density - 0.7) / (1.8 - 0.7));
            }
            else
            {
                edge.CongestionLevel = 0;
            }

            return density;
        }

        private void CalculateEdgeDensities(SimulationSession session)
        {
            foreach (var edge in session.Network.Edges)
            {
                CalculateEdgeDensity(edge);
            }
        }

        private void UpdateTrafficLights(SimulationSession session)
        {
            const int cycleDuration = 120;

            foreach (var vertex in session.Network.Vertices.Where(v => v.HasTrafficLights))
            {
                int phase = (session.CurrentTime / (cycleDuration / 4)) % 4;
                vertex.TrafficLightPhase = phase;

                var outgoingEdges = session.Network.Edges
                    .Where(e => e.StartVertexId == vertex.Id)
                    .ToList();

                if (outgoingEdges.Any())
                {
                    vertex.GreenDirectionEdgeId = outgoingEdges[phase % outgoingEdges.Count].Id;
                }
            }
        }

        private List<TrafficIncident> DetectConflicts(SimulationSession session)
        {
            var incidents = new List<TrafficIncident>();
            var random = new Random();
            var now = DateTime.Now;

            foreach (var edge in session.Network.Edges)
            {
                if (edge.Density > 1.0 && !edge.IsBlocked)
                {
                    double accidentProb = session.Parameters.AccidentProbabilityFactor * Math.Pow(edge.Density, 2);
                    double congestionFactor = 1 + edge.CongestionLevel * 2;
                    accidentProb *= congestionFactor;

                    if (random.NextDouble() < accidentProb && random.NextDouble() > session.Parameters.NoAccidentChance)
                    {
                        edge.IsBlocked = true;
                        edge.BlockedUntil = now.AddSeconds(session.Parameters.BlockDurationSeconds);
                        edge.AccidentCount++;

                        var incident = new TrafficIncident
                        {
                            Id = Guid.NewGuid(),
                            Type = IncidentType.Accident,
                            LocationEdgeId = edge.Id,
                            Time = now,
                            Severity = edge.Density > 1.5 ? IncidentSeverity.High : IncidentSeverity.Medium,
                            Description = $"ДТП! Дорога заблокирована. Плотность: {edge.Density:F2}",
                            IsActive = true
                        };

                        incidents.Add(incident);

                        foreach (var vehicle in edge.Vehicles)
                        {
                            vehicle.CurrentSpeed = 0;
                            vehicle.WaitingTime = 0;
                        }
                    }
                }

                if (edge.HasCrosswalk && edge.Density > 0.3)
                {
                    var startVertex = session.Network.Vertices.First(v => v.Id == edge.StartVertexId);
                    var endVertex = session.Network.Vertices.First(v => v.Id == edge.EndVertexId);

                    var pedestriansNearby = session.Pedestrians.Count(p =>
                        p.Position.EdgeId == startVertex.Id || p.Position.EdgeId == endVertex.Id);

                    if (pedestriansNearby > 0)
                    {
                        double conflictProb = Math.Min(0.8, 0.1 + edge.Density * 0.3 + pedestriansNearby * 0.1);
                        if (random.NextDouble() < conflictProb)
                        {
                            var incident = new TrafficIncident
                            {
                                Id = Guid.NewGuid(),
                                Type = IncidentType.VehiclePedestrianConflict,
                                LocationEdgeId = edge.Id,
                                Time = now,
                                Severity = edge.Density > 1.0 ? IncidentSeverity.High : IncidentSeverity.Medium,
                                Description = $"Конфликт транспорт-пешеход на переходе. Пешеходов: {pedestriansNearby}",
                                IsActive = true
                            };

                            incidents.Add(incident);
                        }
                    }
                }
            }

            foreach (var edge in session.Network.Edges.Where(e => e.IsBlocked))
            {
                if (now >= edge.BlockedUntil)
                {
                    edge.IsBlocked = false;

                    var incident = new TrafficIncident
                    {
                        Id = Guid.NewGuid(),
                        Type = IncidentType.RoadUnblocked,
                        LocationEdgeId = edge.Id,
                        Time = now,
                        Severity = IncidentSeverity.Low,
                        Description = "Дорога разблокирована после ДТП",
                        IsActive = false
                    };

                    incidents.Add(incident);
                }
            }

            return incidents;
        }

        private void OnSimulationStepCompleted(int stepNumber, int currentTime, SimulationMetric metric)
        {
            SimulationStepCompleted?.Invoke(this, new SimulationStepEventArgs
            {
                StepNumber = stepNumber,
                CurrentTime = currentTime,
                Metrics = metric
            });
        }

        private void OnIncidentOccurred(TrafficIncident incident, int simulationTime)
        {
            IncidentOccurred?.Invoke(this, new SimulationIncidentEventArgs
            {
                Incident = incident,
                SimulationTime = simulationTime
            });
        }

        private void OnStateChanged(SimulationState previousState, SimulationState newState)
        {
            StateChanged?.Invoke(this, new SimulationStateChangedEventArgs
            {
                PreviousState = previousState,
                NewState = newState,
                ChangeTime = DateTime.Now
            });
        }

        // Остальные публичные методы

        public async Task<SimulationMetric> GetCurrentMetricsAsync(Guid sessionId)
        {
            var session = await _simulationRepository.GetSessionAsync(sessionId);
            if (session == null)
                return null;

            return _statisticsCalculator.CalculateMetrics(session);
        }

        public async Task<IEnumerable<SimulationMetric>> GetMetricsHistoryAsync(Guid sessionId)
        {
            return await _simulationRepository.GetSessionMetricsAsync(sessionId);
        }

        public async Task<IEnumerable<TrafficIncident>> GetIncidentsAsync(Guid sessionId)
        {
            var session = await _simulationRepository.GetSessionAsync(sessionId);
            return session?.Incidents ?? new List<TrafficIncident>();
        }

        public async Task<bool> SaveSessionAsync(Guid sessionId)
        {
            if (_currentSession?.Id != sessionId)
            {
                _currentSession = await _simulationRepository.GetSessionAsync(sessionId);
                if (_currentSession == null)
                    return false;
            }

            await _simulationRepository.SaveSessionAsync(_currentSession);
            return true;
        }

        public async Task<bool> LoadSessionAsync(Guid sessionId)
        {
            if (_state != SimulationState.Stopped)
                return false;

            _currentSession = await _simulationRepository.GetSessionAsync(sessionId);
            return _currentSession != null;
        }

        public async Task<Vehicle> AddVehicleAsync(Guid sessionId, VehicleType type, DriverStyle style)
        {
            var session = await _simulationRepository.GetSessionAsync(sessionId);
            if (session == null || session.Network == null)
                return null;

            var vehicle = new Vehicle(type, style);

            var availableEdges = session.Network.Edges.Where(e => !e.IsBlocked).ToList();
            if (availableEdges.Any())
            {
                var random = new Random();
                var edge = availableEdges[random.Next(availableEdges.Count)];

                vehicle.CurrentEdgeId = edge.Id;
                vehicle.Position = new Position { EdgeId = edge.Id, Offset = 0 };

                edge.Vehicles.Add(vehicle);
                session.Vehicles.Add(vehicle);
            }

            await _simulationRepository.SaveSessionAsync(session);
            return vehicle;
        }

        public async Task<Pedestrian> AddPedestrianAsync(Guid sessionId, PedestrianType type)
        {
            var session = await _simulationRepository.GetSessionAsync(sessionId);
            if (session == null || session.Network == null)
                return null;

            var pedestrian = new Pedestrian(type);

            if (session.Network.Vertices.Any())
            {
                var random = new Random();
                var vertex = session.Network.Vertices[random.Next(session.Network.Vertices.Count)];

                pedestrian.Position = new Position { EdgeId = Guid.Empty, Offset = 0 };
                pedestrian.Position.EdgeId = vertex.Id;
            }

            session.Pedestrians.Add(pedestrian);
            await _simulationRepository.SaveSessionAsync(session);
            return pedestrian;
        }

        public async Task<double> CalculateEdgeDensityAsync(Guid sessionId, Guid edgeId)
        {
            var session = await _simulationRepository.GetSessionAsync(sessionId);
            if (session?.Network == null)
                return 0;

            var edge = session.Network.Edges.FirstOrDefault(e => e.Id == edgeId);
            if (edge == null)
                return 0;

            return CalculateEdgeDensity(edge);
        }

        public async Task<double> CalculateNetworkCongestionAsync(Guid sessionId)
        {
            var session = await _simulationRepository.GetSessionAsync(sessionId);
            if (session?.Network == null)
                return 0;

            double totalCongestion = 0;
            int edgeCount = 0;

            foreach (var edge in session.Network.Edges)
            {
                CalculateEdgeDensity(edge);
                totalCongestion += edge.CongestionLevel;
                edgeCount++;
            }

            return edgeCount > 0 ? totalCongestion / edgeCount : 0;
        }

        public async Task<IEnumerable<RoadSegment>> FindOptimalRouteAsync(
            Guid sessionId,
            Guid startVertexId,
            Guid endVertexId,
            VehicleType vehicleType)
        {
            var session = await _simulationRepository.GetSessionAsync(sessionId);
            if (session?.Network == null)
                return new List<RoadSegment>();

            var network = session.Network;
            var startVertex = network.Vertices.FirstOrDefault(v => v.Id == startVertexId);
            var endVertex = network.Vertices.FirstOrDefault(v => v.Id == endVertexId);

            if (startVertex == null || endVertex == null)
                return new List<RoadSegment>();

            var distances = new Dictionary<Guid, double>();
            var previous = new Dictionary<Guid, RoadSegment>();
            var unvisited = new HashSet<Guid>();

            foreach (var vertex in network.Vertices)
            {
                distances[vertex.Id] = double.MaxValue;
                unvisited.Add(vertex.Id);
            }

            distances[startVertexId] = 0;

            while (unvisited.Any())
            {
                var currentVertexId = unvisited.OrderBy(v => distances[v]).First();
                unvisited.Remove(currentVertexId);

                if (currentVertexId == endVertexId)
                    break;

                var outgoingEdges = network.Edges
                    .Where(e => e.StartVertexId == currentVertexId && !e.IsBlocked)
                    .ToList();

                foreach (var edge in outgoingEdges)
                {
                    var neighborId = edge.EndVertexId;
                    var travelTime = edge.Length / (edge.MaxSpeed / 3.6);
                    travelTime *= (1 + edge.CongestionLevel * 2);

                    if (vehicleType == VehicleType.Bicycle && edge.MaxSpeed > 50)
                        travelTime *= 1.5;

                    var alt = distances[currentVertexId] + travelTime;
                    if (alt < distances[neighborId])
                    {
                        distances[neighborId] = alt;
                        previous[neighborId] = edge;
                    }
                }
            }

            var path = new List<RoadSegment>();
            var current = endVertexId;

            while (previous.ContainsKey(current))
            {
                var edge = previous[current];
                path.Insert(0, edge);
                current = edge.StartVertexId;
            }

            return path;
        }

        private void SimulationTimerCallback(object state)
        {
            // Резервный метод для таймера
            if (_state == SimulationState.Running && _currentSession != null)
            {
                _ = PerformSimulationStepAsync(_currentSession);
            }
        }
    }
}