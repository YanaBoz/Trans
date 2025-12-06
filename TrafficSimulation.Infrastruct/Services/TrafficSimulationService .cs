using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrafficSimulation.Core.Events;
using TrafficSimulation.Core.Models;
using TrafficSimulation.Core.Repositories;
using TrafficSimulation.Core.Services;

namespace TrafficSimulation.Application.Services
{
    public class TrafficSimulationService : ITrafficSimulationService
    {
        private readonly IRoadNetworkRepository _networkRepository;
        private readonly ISimulationRepository _simulationRepository;
        private readonly IStatisticsCalculator _statisticsCalculator;

        private SimulationSession _currentSession;
        private SimulationState _state = SimulationState.Stopped;
        private Timer _simulationTimer;
        private DateTime _lastUpdateTime;
        private CancellationTokenSource _cancellationTokenSource;

        public event EventHandler<SimulationStepEventArgs> SimulationStepCompleted;
        public event EventHandler<SimulationIncidentEventArgs> IncidentOccurred;
        public event EventHandler<SimulationStateChangedEventArgs> StateChanged;

        public TrafficSimulationService(
            IRoadNetworkRepository networkRepository,
            ISimulationRepository simulationRepository,
            IStatisticsCalculator statisticsCalculator)
        {
            _networkRepository = networkRepository;
            _simulationRepository = simulationRepository;
            _statisticsCalculator = statisticsCalculator ?? new StatisticsCalculator();
        }

        public async Task<SimulationSession> CreateSessionAsync(
            string name,
            Guid networkId,
            SimulationParameters parameters)
        {
            if (_state != SimulationState.Stopped && _currentSession != null)
                throw new InvalidOperationException("Another simulation is already running");

            var network = await _networkRepository.GetByIdAsync(networkId);
            if (network == null)
                throw new ArgumentException($"Network with id {networkId} not found");

            _currentSession = new SimulationSession(name, networkId, parameters)
            {
                Network = network,
                StartTime = DateTime.Now,
                State = SimulationState.Stopped
            };

            // Инициализация начального трафика
            InitializeTraffic(_currentSession, parameters);

            await _simulationRepository.SaveSessionAsync(_currentSession);

            OnStateChanged(SimulationState.Stopped, SimulationState.Stopped);

            return _currentSession;
        }

        public async Task<bool> StartSimulationAsync(Guid sessionId)
        {
            if (_state != SimulationState.Stopped)
                return false;

            if (_currentSession == null || _currentSession.Id != sessionId)
            {
                _currentSession = await _simulationRepository.GetSessionAsync(sessionId);
                if (_currentSession == null)
                    return false;
            }

            _currentSession.Start();
            _currentSession.State = SimulationState.Running;
            _state = SimulationState.Running;

            _cancellationTokenSource = new CancellationTokenSource();
            _lastUpdateTime = DateTime.Now;

            // Запуск асинхронной симуляции
            _ = RunSimulationAsync(_cancellationTokenSource.Token);

            await _simulationRepository.SaveSessionAsync(_currentSession);
            OnStateChanged(SimulationState.Stopped, SimulationState.Running);

            return true;
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
            OnStateChanged(_state, SimulationState.Stopped);

            return true;
        }

        public async Task<SimulationStepResult> SimulateStepAsync(Guid sessionId)
        {
            if (_currentSession?.Id != sessionId)
                throw new InvalidOperationException("Session not found or not active");

            var stepResult = await PerformSimulationStepAsync(_currentSession);

            _currentSession.StepCount++;
            _currentSession.CurrentTime += _currentSession.Parameters.TimeStepSeconds;

            // Сохраняем метрики
            var metric = _statisticsCalculator.CalculateMetrics(_currentSession);
            _currentSession.AddMetric(metric);

            await _simulationRepository.SaveSessionAsync(_currentSession);

            // Вызываем события
            SimulationStepCompleted?.Invoke(this, new SimulationStepEventArgs
            {
                StepNumber = _currentSession.StepCount,
                CurrentTime = _currentSession.CurrentTime,
                Metrics = metric
            });

            foreach (var incident in stepResult.Incidents)
            {
                IncidentOccurred?.Invoke(this, new SimulationIncidentEventArgs
                {
                    Incident = incident,
                    SimulationTime = _currentSession.CurrentTime
                });
            }

            // Проверяем завершение симуляции
            if (_currentSession.CurrentTime >= _currentSession.Parameters.SimulationDurationSeconds)
            {
                await StopSimulationAsync(sessionId);
            }

            return stepResult;
        }

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

            // Выбираем случайное ребро для начала движения
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

            // Выбираем случайную вершину
            if (session.Network.Vertices.Any())
            {
                var random = new Random();
                var vertex = session.Network.Vertices[random.Next(session.Network.Vertices.Count)];

                pedestrian.Position = new Position { EdgeId = Guid.Empty, Offset = 0 };
                // Для пешехода храним ID вершины в EdgeId (это временное решение)
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
            // Упрощенная реализация поиска пути (алгоритм Дейкстры)
            var session = await _simulationRepository.GetSessionAsync(sessionId);
            if (session?.Network == null)
                return new List<RoadSegment>();

            var network = session.Network;
            var startVertex = network.Vertices.FirstOrDefault(v => v.Id == startVertexId);
            var endVertex = network.Vertices.FirstOrDefault(v => v.Id == endVertexId);

            if (startVertex == null || endVertex == null)
                return new List<RoadSegment>();

            // Создаем граф для поиска пути
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

                // Получаем исходящие ребра из текущей вершины
                var outgoingEdges = network.Edges
                    .Where(e => e.StartVertexId == currentVertexId && !e.IsBlocked)
                    .ToList();

                foreach (var edge in outgoingEdges)
                {
                    var neighborId = edge.EndVertexId;
                    var travelTime = edge.Length / (edge.MaxSpeed / 3.6); // время в секундах

                    // Учитываем загруженность
                    travelTime *= (1 + edge.CongestionLevel * 2);

                    // Учитываем тип транспортного средства
                    if (vehicleType == VehicleType.Bicycle && edge.MaxSpeed > 50)
                        travelTime *= 1.5; // Велосипедисты медленнее на скоростных дорогах

                    var alt = distances[currentVertexId] + travelTime;
                    if (alt < distances[neighborId])
                    {
                        distances[neighborId] = alt;
                        previous[neighborId] = edge;
                    }
                }
            }

            // Восстанавливаем путь
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

        #region Private Methods

        private void InitializeTraffic(SimulationSession session, SimulationParameters parameters)
        {
            var random = new Random();

            // Добавление начальных транспортных средств
            for (int i = 0; i < parameters.InitialVehicles; i++)
            {
                var vehicleType = WeightedRandomSelection(parameters.VehicleTypeDistribution, random);
                var driverStyle = WeightedRandomSelection(parameters.DriverStyleDistribution, random);

                var vehicle = new Vehicle(vehicleType, driverStyle);

                if (session.Network.Edges.Any())
                {
                    var edge = session.Network.Edges[random.Next(session.Network.Edges.Count)];
                    vehicle.CurrentEdgeId = edge.Id;
                    vehicle.Position = new Position { EdgeId = edge.Id, Offset = random.NextDouble() * edge.Length * 0.5 };
                    edge.Vehicles.Add(vehicle);
                }

                session.Vehicles.Add(vehicle);
            }

            // Добавление начальных пешеходов
            for (int i = 0; i < parameters.InitialPedestrians; i++)
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
                        await SimulateStepAsync(_currentSession.Id);
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

        private async Task<SimulationStepResult> PerformSimulationStepAsync(SimulationSession session)
        {
            var result = new SimulationStepResult();

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
            result.Incidents = incidents;

            // 7. Расчет статистики
            result.AverageVehicleSpeed = session.CalculateAverageVehicleSpeed();
            result.CongestionLevel = session.Network.Edges.Any()
                ? session.Network.Edges.Average(e => e.CongestionLevel)
                : 0;

            return result;
        }

        private async Task GenerateNewTrafficAsync(SimulationSession session)
        {
            var parameters = session.Parameters;
            var intensityFactor = parameters.GetCurrentIntensityFactor();
            var random = new Random();

            // Генерация новых транспортных средств
            double vehicleProb = parameters.VehicleIntensity * intensityFactor * (parameters.TimeStepSeconds / 60.0);
            if (random.NextDouble() < vehicleProb && session.Vehicles.Count < 1000) // Ограничение на максимальное количество ТС
            {
                var vehicleType = WeightedRandomSelection(parameters.VehicleTypeDistribution, random);
                var driverStyle = WeightedRandomSelection(parameters.DriverStyleDistribution, random);

                var vehicle = new Vehicle(vehicleType, driverStyle);

                var availableEdges = session.Network.Edges
                    .Where(e => !e.IsBlocked && e.Vehicles.Count < 50) // Ограничение на количество ТС на ребре
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
            if (random.NextDouble() < pedestrianProb && session.Pedestrians.Count < 500) // Ограничение на максимальное количество пешеходов
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
                double distanceMoved = vehicle.CurrentSpeed * (session.Parameters.TimeStepSeconds / 3.6); // км/ч -> м/с

                // Обновление позиции
                vehicle.Position.Offset += distanceMoved;
                vehicle.DistanceTraveled += distanceMoved / 1000; // метры -> километры
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

                    if (outgoingEdges.Any() && random.NextDouble() < 0.8) // 80% шанс продолжить движение
                    {
                        // Выбор ребра того же типа
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
                // Пешеходы перемещаются между вершинами
                var currentVertexId = pedestrian.Position.EdgeId; // Временно храним ID вершины здесь
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
                        // Получаем исходящие ребра из текущей вершины
                        var outgoingEdges = session.Network.Edges
                            .Where(e => e.StartVertexId == currentVertexId)
                            .ToList();

                        if (outgoingEdges.Any())
                        {
                            var edge = outgoingEdges[random.Next(outgoingEdges.Count)];
                            var newVertexId = edge.EndVertexId;

                            // Проверяем переход через дорогу
                            if (edge.HasCrosswalk)
                            {
                                var density = CalculateEdgeDensity(edge);
                                var safetyThreshold = 0.3 + random.NextDouble() * 0.2;

                                if (density < safetyThreshold || random.NextDouble() < 0.1) // 10% шанс рискнуть
                                {
                                    pedestrian.Position.EdgeId = newVertexId;
                                    pedestrian.WaitingTime = 0;
                                    pedestrian.DistanceTraveled += 0.1; // примерное расстояние
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

                // Расчет плотности пешеходов вокруг
                var localDensity = session.Pedestrians.Count(p =>
                    p.Position.EdgeId == pedestrian.Position.EdgeId) / 10.0;

                pedestrian.UpdateSpeed(localDensity, session.Parameters.TimeStepSeconds);

                // Случайное достижение пункта назначения
                if (random.NextDouble() < 0.02) // 2% шанс завершить маршрут на каждом шаге
                {
                    pedestrian.DestinationReached = true;
                    pedestriansToRemove.Add(pedestrian);
                    session.CompletedPedestriansCount++;
                }
            }

            // Удаление пешеходов, достигших пункта назначения
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

            // Коэффициенты PCE (Passenger Car Equivalent)
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

            // Расчет уровня загруженности
            if (density > 0.7) // Используем фиксированный порог
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
            const int cycleDuration = 120; // секунд

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
                // Проверка ДТП
                if (edge.Density > 1.0 && !edge.IsBlocked)
                {
                    double accidentProb = session.Parameters.AccidentProbabilityFactor * Math.Pow(edge.Density, 2);
                    double congestionFactor = 1 + edge.CongestionLevel * 2;
                    accidentProb *= congestionFactor;

                    if (random.NextDouble() < accidentProb && random.NextDouble() > session.Parameters.NoAccidentChance)
                    {
                        // Создание ДТП
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
                        session.AddIncident(incident);

                        // Остановка всех транспортных средств на ребре
                        foreach (var vehicle in edge.Vehicles)
                        {
                            vehicle.CurrentSpeed = 0;
                            vehicle.WaitingTime = 0;
                        }
                    }
                }

                // Проверка конфликтов с пешеходами
                if (edge.HasCrosswalk && edge.Density > 0.3)
                {
                    // Подсчет пешеходов рядом с переходом
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
                            session.AddIncident(incident);
                        }
                    }
                }
            }

            // Проверка разблокировки дорог
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
                    session.AddIncident(incident);
                }
            }

            return incidents;
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

        #endregion
    }

    public class StatisticsCalculator : IStatisticsCalculator
    {
        public SimulationMetric CalculateMetrics(SimulationSession session)
        {
            var metric = new SimulationMetric(session.Id, session.CurrentTime)
            {
                VehicleCount = session.Vehicles.Count,
                PedestrianCount = session.Pedestrians.Count,
                ActiveIncidents = session.Incidents?.Count(i => i.IsActive) ?? 0,
                BlockedRoadsCount = session.Network?.Edges.Count(e => e.IsBlocked) ?? 0
            };

            if (session.Vehicles.Any())
            {
                metric.AverageVehicleSpeed = session.Vehicles.Average(v => v.CurrentSpeed);
                metric.TotalDelay = session.Vehicles.Sum(v => v.Delay);
                metric.AverageTravelTime = session.Vehicles.Average(v => v.TotalTravelTime);
                metric.VehicleThroughput = session.CompletedVehiclesCount;
            }

            if (session.Pedestrians.Any())
            {
                metric.AveragePedestrianSpeed = session.Pedestrians.Average(p => p.CurrentSpeed);
                metric.PedestrianThroughput = session.CompletedPedestriansCount;
            }

            // Расчет загруженности
            if (session.Network?.Edges != null)
            {
                var congestionSum = session.Network.Edges.Sum(e => e.CongestionLevel);
                var edgeCount = session.Network.Edges.Count;
                metric.CongestionLevel = edgeCount > 0 ? congestionSum / edgeCount : 0;

                // Расчет использования сети
                var totalLength = session.Network.Edges.Sum(e => e.Length);
                var totalVehicles = session.Vehicles.Sum(v =>
                {
                    var edge = session.Network.Edges.FirstOrDefault(e => e.Id == v.CurrentEdgeId);
                    return edge != null ? v.DistanceTraveled / (edge.Length / 1000) : 0;
                });
                metric.NetworkUtilization = totalLength > 0 ? totalVehicles / totalLength * 100 : 0;
            }

            metric.AccidentCount = session.Incidents?.Count(i => i.Type == IncidentType.Accident) ?? 0;

            return metric;
        }

        public ComparisonCriteria GetDefaultCriteria()
        {
            return new ComparisonCriteria
            {
                IncludeAverageSpeed = true,
                IncludeThroughput = true,
                IncludeCongestionLevel = true,
                IncludeAccidents = true,
                IncludeTotalDelay = true,
                IncludeTrafficLightEfficiency = false,
                Weights = new Dictionary<string, double>
                {
                    ["AverageSpeed"] = 0.25,
                    ["Throughput"] = 0.30,
                    ["CongestionLevel"] = 0.20,
                    ["Accidents"] = 0.15,
                    ["TotalDelay"] = 0.10
                }
            };
        }

        public List<KeyValuePair<string, double>> CalculatePerformanceIndicators(SimulationSession session)
        {
            var indicators = new List<KeyValuePair<string, double>>();

            // Показатель эффективности транспортного потока
            double flowEfficiency = CalculateFlowEfficiency(session);
            indicators.Add(new KeyValuePair<string, double>("Эффективность потока", flowEfficiency));

            // Показатель безопасности
            double safetyIndex = CalculateSafetyIndex(session);
            indicators.Add(new KeyValuePair<string, double>("Индекс безопасности", safetyIndex));

            // Показатель загруженности сети
            double networkUtilization = CalculateNetworkUtilization(session);
            indicators.Add(new KeyValuePair<string, double>("Использование сети", networkUtilization));

            return indicators;
        }

        private double CalculateFlowEfficiency(SimulationSession session)
        {
            if (!session.Vehicles.Any())
                return 0;

            double totalDistance = session.Vehicles.Sum(v => v.DistanceTraveled);
            double totalTime = session.CurrentTime / 3600.0; // в часах

            if (totalTime == 0)
                return 0;

            return totalDistance / totalTime; // км/ч
        }

        private double CalculateSafetyIndex(SimulationSession session)
        {
            double totalVehicles = session.Vehicles.Count + session.CompletedVehiclesCount;
            if (totalVehicles == 0)
                return 100;

            double accidentRate = (session.Incidents?.Count(i => i.Type == IncidentType.Accident) ?? 0) / totalVehicles;
            return Math.Max(0, 100 - accidentRate * 10000);
        }

        private double CalculateNetworkUtilization(SimulationSession session)
        {
            if (session.Network?.Edges == null || !session.Network.Edges.Any())
                return 0;

            return session.Network.Edges.Average(e => e.Density) * 100;
        }
    }
}