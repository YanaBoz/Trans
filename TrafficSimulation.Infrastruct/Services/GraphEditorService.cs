using TrafficSimulation.Core.Models;
using TrafficSimulation.Core.Repositories;
using TrafficSimulation.Core.Services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace TrafficSimulation.Infrastructure.Services
{
    public class GraphEditorService : IGraphEditorService
    {
        private readonly IRoadNetworkRepository _repository;
        private readonly Random _random;

        public GraphEditorService(IRoadNetworkRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _random = new Random();
        }

        public async Task<RoadNetwork> CreateNetworkAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Имя сети не может быть пустым", nameof(name));

            var network = new RoadNetwork(name)
            {
                Description = $"Сеть создана {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                CreatedDate = DateTime.Now,
                ModifiedDate = DateTime.Now
            };

            await _repository.SaveAsync(network);
            return network;
        }

        public async Task<RoadNetwork> LoadNetworkAsync(Guid networkId)
        {
            var network = await _repository.GetByIdAsync(networkId);
            if (network == null)
                throw new KeyNotFoundException($"Сеть с ID {networkId} не найдена");

            return network;
        }

        public async Task<bool> SaveNetworkAsync(RoadNetwork network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));

            try
            {
                network.UpdateModifiedDate();
                await _repository.SaveAsync(network);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сохранения сети: {ex.Message}");
                return false;
            }
        }

        public async Task<RoadNetwork> CloneNetworkAsync(Guid networkId, string newName)
        {
            var network = await LoadNetworkAsync(networkId);
            return await _repository.CloneAsync(network, newName);
        }

        public async Task<Vertex> AddVertexAsync(
            Guid networkId,
            int x,
            int y,
            VertexType type = VertexType.Intersection,
            int cityId = 1,
            bool hasTrafficLights = true)
        {
            var network = await LoadNetworkAsync(networkId);

            var vertex = new Vertex
            {
                Id = Guid.NewGuid(),
                Name = $"V{network.Vertices.Count + 1}",
                X = x,
                Y = y,
                Type = type,
                HasTrafficLights = type == VertexType.Intersection ? hasTrafficLights : false,
                CityId = cityId,
                TrafficLightPhase = 0,
                IncomingEdges = new List<Guid>(),
                OutgoingEdges = new List<Guid>()
            };

            network.Vertices.Add(vertex);
            await SaveNetworkAsync(network);

            return vertex;
        }

        public async Task<RoadSegment> AddEdgeAsync(
            Guid networkId,
            Guid startVertexId,
            Guid endVertexId,
            RoadType type = RoadType.Urban,
            double length = 0,
            int lanes = 1,
            double maxSpeed = 50,
            bool hasCrosswalk = false,
            bool isBidirectional = true)
        {
            var network = await LoadNetworkAsync(networkId);

            var startVertex = network.Vertices.FirstOrDefault(v => v.Id == startVertexId);
            var endVertex = network.Vertices.FirstOrDefault(v => v.Id == endVertexId);

            if (startVertex == null)
                throw new ArgumentException($"Начальная вершина {startVertexId} не найдена", nameof(startVertexId));

            if (endVertex == null)
                throw new ArgumentException($"Конечная вершина {endVertexId} не найдена", nameof(endVertexId));

            if (startVertex.Id == endVertex.Id)
                throw new ArgumentException("Нельзя создать ребро из вершины в саму себя");

            // Проверяем существование ребра
            var existingEdge = network.Edges.FirstOrDefault(e =>
                e.StartVertexId == startVertexId && e.EndVertexId == endVertexId);

            if (existingEdge != null)
                throw new InvalidOperationException($"Ребро из {startVertexId} в {endVertexId} уже существует");

            // Вычисляем длину, если не указана
            if (length <= 0)
            {
                length = CalculateDistance(startVertex, endVertex);
            }

            // Создаем основное ребро
            var edge = new RoadSegment
            {
                Id = Guid.NewGuid(),
                Name = $"E{startVertex.Name}-{endVertex.Name}",
                StartVertexId = startVertexId,
                EndVertexId = endVertexId,
                Length = Math.Max(1, length),
                Lanes = Math.Max(1, lanes),
                MaxSpeed = Math.Max(10, Math.Min(120, maxSpeed)),
                Type = type,
                HasCrosswalk = type == RoadType.Urban && hasCrosswalk,
                CityId = startVertex.CityId == endVertex.CityId ? startVertex.CityId : 0,
                IsBlocked = false,
                BlockedUntil = DateTime.MinValue,
                AccidentCount = 0,
                CongestionLevel = 0,
                Density = 0,
                FlowRate = 0,
                IsBidirectional = isBidirectional,
                Vehicles = new List<Vehicle>()
            };

            network.Edges.Add(edge);

            // Обновляем списки ребер в вершинах
            startVertex.OutgoingEdges.Add(edge.Id);
            endVertex.IncomingEdges.Add(edge.Id);

            // Если ребро двустороннее, создаем обратное направление
            if (isBidirectional)
            {
                var reverseEdge = new RoadSegment
                {
                    Id = Guid.NewGuid(),
                    Name = $"E{endVertex.Name}-{startVertex.Name}",
                    StartVertexId = endVertexId,
                    EndVertexId = startVertexId,
                    Length = Math.Max(1, length),
                    Lanes = Math.Max(1, lanes),
                    MaxSpeed = Math.Max(10, Math.Min(120, maxSpeed)),
                    Type = type,
                    HasCrosswalk = type == RoadType.Urban && hasCrosswalk,
                    CityId = startVertex.CityId == endVertex.CityId ? startVertex.CityId : 0,
                    IsBlocked = false,
                    BlockedUntil = DateTime.MinValue,
                    AccidentCount = 0,
                    CongestionLevel = 0,
                    Density = 0,
                    FlowRate = 0,
                    IsBidirectional = true,
                    Vehicles = new List<Vehicle>()
                };

                network.Edges.Add(reverseEdge);
                endVertex.OutgoingEdges.Add(reverseEdge.Id);
                startVertex.IncomingEdges.Add(reverseEdge.Id);
            }

            await SaveNetworkAsync(network);
            return edge;
        }

        public async Task<bool> RemoveVertexAsync(Guid networkId, Guid vertexId)
        {
            var network = await LoadNetworkAsync(networkId);

            var vertex = network.Vertices.FirstOrDefault(v => v.Id == vertexId);
            if (vertex == null)
                return false;

            // Находим все ребра, связанные с этой вершиной
            var edgesToRemove = network.Edges
                .Where(e => e.StartVertexId == vertexId || e.EndVertexId == vertexId)
                .ToList();

            // Удаляем ребра
            foreach (var edge in edgesToRemove)
            {
                await RemoveEdgeAsync(networkId, edge.Id);
            }

            // Удаляем вершину
            network.Vertices.Remove(vertex);

            await SaveNetworkAsync(network);
            return true;
        }

        public async Task<bool> RemoveEdgeAsync(Guid networkId, Guid edgeId)
        {
            var network = await LoadNetworkAsync(networkId);

            var edge = network.Edges.FirstOrDefault(e => e.Id == edgeId);
            if (edge == null)
                return false;

            // Удаляем ссылки на ребро из вершин
            var startVertex = network.Vertices.FirstOrDefault(v => v.Id == edge.StartVertexId);
            var endVertex = network.Vertices.FirstOrDefault(v => v.Id == edge.EndVertexId);

            if (startVertex != null)
                startVertex.OutgoingEdges.Remove(edgeId);

            if (endVertex != null)
                endVertex.IncomingEdges.Remove(edgeId);

            // Удаляем ребро
            network.Edges.Remove(edge);

            await SaveNetworkAsync(network);
            return true;
        }

        public async Task<bool> UpdateVertexAsync(Guid networkId, Vertex vertex)
        {
            if (vertex == null)
                throw new ArgumentNullException(nameof(vertex));

            var network = await LoadNetworkAsync(networkId);

            var existingVertex = network.Vertices.FirstOrDefault(v => v.Id == vertex.Id);
            if (existingVertex == null)
                return false;

            // Обновляем свойства
            existingVertex.Name = vertex.Name;
            existingVertex.X = vertex.X;
            existingVertex.Y = vertex.Y;
            existingVertex.Type = vertex.Type;
            existingVertex.HasTrafficLights = vertex.HasTrafficLights;
            existingVertex.CityId = vertex.CityId;
            existingVertex.TrafficLightPhase = vertex.TrafficLightPhase;

            await SaveNetworkAsync(network);
            return true;
        }

        public async Task<bool> UpdateEdgeAsync(Guid networkId, RoadSegment edge)
        {
            if (edge == null)
                throw new ArgumentNullException(nameof(edge));

            var network = await LoadNetworkAsync(networkId);

            var existingEdge = network.Edges.FirstOrDefault(e => e.Id == edge.Id);
            if (existingEdge == null)
                return false;

            // Обновляем свойства (кроме ID и связей)
            existingEdge.Name = edge.Name;
            existingEdge.Length = Math.Max(1, edge.Length);
            existingEdge.Lanes = Math.Max(1, edge.Lanes);
            existingEdge.MaxSpeed = Math.Max(10, Math.Min(120, edge.MaxSpeed));
            existingEdge.Type = edge.Type;
            existingEdge.HasCrosswalk = edge.HasCrosswalk;
            existingEdge.CityId = edge.CityId;
            existingEdge.IsBidirectional = edge.IsBidirectional;

            await SaveNetworkAsync(network);
            return true;
        }

        public async Task<bool> SetTrafficLightsAsync(Guid networkId, Guid vertexId, bool enabled)
        {
            var network = await LoadNetworkAsync(networkId);

            var vertex = network.Vertices.FirstOrDefault(v => v.Id == vertexId);
            if (vertex == null)
                return false;

            if (vertex.Type != VertexType.Intersection)
                return false;

            vertex.HasTrafficLights = enabled;

            await SaveNetworkAsync(network);
            return true;
        }

        public async Task<bool> SetCrosswalkAsync(Guid networkId, Guid edgeId, bool enabled)
        {
            var network = await LoadNetworkAsync(networkId);

            var edge = network.Edges.FirstOrDefault(e => e.Id == edgeId);
            if (edge == null)
                return false;

            if (edge.Type != RoadType.Urban)
                return false;

            edge.HasCrosswalk = enabled;

            await SaveNetworkAsync(network);
            return true;
        }

        public async Task<Dictionary<Guid, Point>> CalculateLayoutAsync(
            Guid networkId,
            LayoutAlgorithm algorithm = LayoutAlgorithm.FruchtermanReingold)
        {
            var network = await LoadNetworkAsync(networkId);
            var layout = new Dictionary<Guid, Point>();

            switch (algorithm)
            {
                case LayoutAlgorithm.Circular:
                    layout = CalculateCircularLayout(network);
                    break;
                case LayoutAlgorithm.Grid:
                    layout = CalculateGridLayout(network);
                    break;
                case LayoutAlgorithm.Random:
                    layout = CalculateRandomLayout(network);
                    break;
                case LayoutAlgorithm.FruchtermanReingold:
                default:
                    layout = CalculateFruchtermanReingoldLayout(network);
                    break;
            }

            return layout;
        }

        public async Task<bool> ValidateNetworkAsync(Guid networkId)
        {
            var network = await LoadNetworkAsync(networkId);

            // Проверка наличия вершин
            if (!network.Vertices.Any())
                return false;

            // Проверка, что все ребра ссылаются на существующие вершины
            foreach (var edge in network.Edges)
            {
                var startExists = network.Vertices.Any(v => v.Id == edge.StartVertexId);
                var endExists = network.Vertices.Any(v => v.Id == edge.EndVertexId);

                if (!startExists || !endExists)
                    return false;
            }

            // Проверка, что вершины имеют корректные списки ребер
            foreach (var vertex in network.Vertices)
            {
                foreach (var edgeId in vertex.IncomingEdges)
                {
                    var edge = network.Edges.FirstOrDefault(e => e.Id == edgeId);
                    if (edge == null || edge.EndVertexId != vertex.Id)
                        return false;
                }

                foreach (var edgeId in vertex.OutgoingEdges)
                {
                    var edge = network.Edges.FirstOrDefault(e => e.Id == edgeId);
                    if (edge == null || edge.StartVertexId != vertex.Id)
                        return false;
                }
            }

            return true;
        }

        public async Task<List<string>> GetNetworkIssuesAsync(Guid networkId)
        {
            var issues = new List<string>();
            var network = await LoadNetworkAsync(networkId);

            // Проверка изолированных вершин
            foreach (var vertex in network.Vertices)
            {
                if (!vertex.IncomingEdges.Any() && !vertex.OutgoingEdges.Any())
                {
                    issues.Add($"Вершина {vertex.Name} ({vertex.Id}) изолирована (нет входящих или исходящих ребер)");
                }
            }

            // Проверка дублирующих ребер
            var edgeGroups = network.Edges
                .GroupBy(e => new { e.StartVertexId, e.EndVertexId })
                .Where(g => g.Count() > 1);

            foreach (var group in edgeGroups)
            {
                issues.Add($"Несколько ребер из {group.Key.StartVertexId} в {group.Key.EndVertexId}");
            }

            // Проверка очень коротких ребер
            foreach (var edge in network.Edges.Where(e => e.Length < 10))
            {
                issues.Add($"Ребро {edge.Name} слишком короткое ({edge.Length:F1} м)");
            }

            // Проверка некорректных скоростей
            foreach (var edge in network.Edges)
            {
                if (edge.MaxSpeed < 10 || edge.MaxSpeed > 120)
                {
                    issues.Add($"Ребро {edge.Name} имеет недопустимую скорость ({edge.MaxSpeed} км/ч)");
                }
            }

            // Проверка несогласованности двусторонних ребер
            foreach (var edge in network.Edges.Where(e => e.IsBidirectional))
            {
                var reverseEdge = network.Edges.FirstOrDefault(e =>
                    e.StartVertexId == edge.EndVertexId &&
                    e.EndVertexId == edge.StartVertexId);

                if (reverseEdge == null || !reverseEdge.IsBidirectional)
                {
                    issues.Add($"Двустороннее ребро {edge.Name} не имеет обратного направления");
                }
            }

            return issues;
        }

        public async Task<RoadNetwork> GenerateGridNetworkAsync(
            string name,
            int width,
            int height,
            int cellSize = 200,
            int cityId = 1)
        {
            if (width < 1 || height < 1)
                throw new ArgumentException("Ширина и высота должны быть не менее 1");

            var network = new RoadNetwork(name)
            {
                Description = $"Сеточная сеть {width}x{height}, размер ячейки: {cellSize}м"
            };

            // Создаем вершины в сетке
            var vertices = new Vertex[height, width];
            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    var vertex = new Vertex
                    {
                        Id = Guid.NewGuid(),
                        Name = $"V{row}_{col}",
                        X = col * cellSize,
                        Y = row * cellSize,
                        Type = VertexType.Intersection,
                        HasTrafficLights = (row > 0 && col > 0) && _random.NextDouble() < 0.7,
                        CityId = cityId,
                        TrafficLightPhase = 0,
                        IncomingEdges = new List<Guid>(),
                        OutgoingEdges = new List<Guid>()
                    };

                    vertices[row, col] = vertex;
                    network.Vertices.Add(vertex);
                }
            }

            // Создаем горизонтальные ребра
            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width - 1; col++)
                {
                    var from = vertices[row, col];
                    var to = vertices[row, col + 1];

                    await AddGridEdgeAsync(network, from, to, cellSize);
                }
            }

            // Создаем вертикальные ребра
            for (int row = 0; row < height - 1; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    var from = vertices[row, col];
                    var to = vertices[row + 1, col];

                    await AddGridEdgeAsync(network, from, to, cellSize);
                }
            }

            await _repository.SaveAsync(network);
            return network;
        }

        private async Task AddGridEdgeAsync(RoadNetwork network, Vertex from, Vertex to, int cellSize)
        {
            var length = CalculateDistance(from, to);
            var edge = new RoadSegment
            {
                Id = Guid.NewGuid(),
                Name = $"E{from.Name}-{to.Name}",
                StartVertexId = from.Id,
                EndVertexId = to.Id,
                Length = length,
                Lanes = _random.Next(1, 4),
                MaxSpeed = new[] { 40, 50, 60, 70 }[_random.Next(4)],
                Type = RoadType.Urban,
                HasCrosswalk = _random.NextDouble() < 0.3,
                CityId = from.CityId,
                IsBidirectional = true,
                IsBlocked = false,
                BlockedUntil = DateTime.MinValue,
                AccidentCount = 0,
                CongestionLevel = 0,
                Density = 0,
                FlowRate = 0,
                Vehicles = new List<Vehicle>()
            };

            network.Edges.Add(edge);
            from.OutgoingEdges.Add(edge.Id);
            to.IncomingEdges.Add(edge.Id);

            // Добавляем обратное направление для двусторонней дороги
            var reverseEdge = new RoadSegment
            {
                Id = Guid.NewGuid(),
                Name = $"E{to.Name}-{from.Name}",
                StartVertexId = to.Id,
                EndVertexId = from.Id,
                Length = length,
                Lanes = edge.Lanes,
                MaxSpeed = edge.MaxSpeed,
                Type = edge.Type,
                HasCrosswalk = edge.HasCrosswalk,
                CityId = edge.CityId,
                IsBidirectional = true,
                IsBlocked = false,
                BlockedUntil = DateTime.MinValue,
                AccidentCount = 0,
                CongestionLevel = 0,
                Density = 0,
                FlowRate = 0,
                Vehicles = new List<Vehicle>()
            };

            network.Edges.Add(reverseEdge);
            to.OutgoingEdges.Add(reverseEdge.Id);
            from.IncomingEdges.Add(reverseEdge.Id);
        }

        public async Task<RoadNetwork> MergeNetworksAsync(
            string name,
            IEnumerable<Guid> networkIds,
            IEnumerable<Tuple<Guid, Guid>> connections)
        {
            if (networkIds == null || !networkIds.Any())
                throw new ArgumentException("Необходимо указать хотя бы одну сеть для объединения");

            var mergedNetwork = new RoadNetwork(name)
            {
                Description = $"Объединенная сеть создана {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
            };

            var vertexIdMap = new Dictionary<Guid, Guid>();
            var edgeIdMap = new Dictionary<Guid, Guid>();

            // Загружаем и объединяем все сети
            foreach (var networkId in networkIds)
            {
                var network = await LoadNetworkAsync(networkId);

                // Копируем вершины
                foreach (var vertex in network.Vertices)
                {
                    var newId = Guid.NewGuid();
                    vertexIdMap[vertex.Id] = newId;

                    var newVertex = new Vertex
                    {
                        Id = newId,
                        Name = $"{network.Name}_{vertex.Name}",
                        X = vertex.X,
                        Y = vertex.Y,
                        Type = vertex.Type,
                        HasTrafficLights = vertex.HasTrafficLights,
                        CityId = vertex.CityId,
                        TrafficLightPhase = vertex.TrafficLightPhase,
                        IncomingEdges = new List<Guid>(),
                        OutgoingEdges = new List<Guid>()
                    };

                    mergedNetwork.Vertices.Add(newVertex);
                }

                // Копируем ребра
                foreach (var edge in network.Edges)
                {
                    var newId = Guid.NewGuid();
                    edgeIdMap[edge.Id] = newId;

                    if (!vertexIdMap.TryGetValue(edge.StartVertexId, out var newStartId) ||
                        !vertexIdMap.TryGetValue(edge.EndVertexId, out var newEndId))
                    {
                        continue;
                    }

                    var newEdge = new RoadSegment
                    {
                        Id = newId,
                        Name = $"{network.Name}_{edge.Name}",
                        StartVertexId = newStartId,
                        EndVertexId = newEndId,
                        Length = edge.Length,
                        Lanes = edge.Lanes,
                        MaxSpeed = edge.MaxSpeed,
                        Type = edge.Type,
                        HasCrosswalk = edge.HasCrosswalk,
                        CityId = edge.CityId,
                        IsBidirectional = edge.IsBidirectional,
                        IsBlocked = edge.IsBlocked,
                        BlockedUntil = edge.BlockedUntil,
                        AccidentCount = edge.AccidentCount,
                        CongestionLevel = edge.CongestionLevel,
                        Density = edge.Density,
                        FlowRate = edge.FlowRate,
                        Vehicles = new List<Vehicle>()
                    };

                    mergedNetwork.Edges.Add(newEdge);

                    // Обновляем списки ребер в вершинах
                    var startVertex = mergedNetwork.Vertices.First(v => v.Id == newStartId);
                    var endVertex = mergedNetwork.Vertices.First(v => v.Id == newEndId);

                    startVertex.OutgoingEdges.Add(newId);
                    endVertex.IncomingEdges.Add(newId);
                }
            }

            // Добавляем соединения между сетями
            if (connections != null)
            {
                foreach (var connection in connections)
                {
                    if (vertexIdMap.TryGetValue(connection.Item1, out var newStartId) &&
                        vertexIdMap.TryGetValue(connection.Item2, out var newEndId))
                    {
                        var startVertex = mergedNetwork.Vertices.First(v => v.Id == newStartId);
                        var endVertex = mergedNetwork.Vertices.First(v => v.Id == newEndId);

                        var edge = new RoadSegment
                        {
                            Id = Guid.NewGuid(),
                            Name = $"Connection_{startVertex.Name}_{endVertex.Name}",
                            StartVertexId = newStartId,
                            EndVertexId = newEndId,
                            Length = CalculateDistance(startVertex, endVertex),
                            Lanes = 2,
                            MaxSpeed = 60,
                            Type = RoadType.Urban,
                            HasCrosswalk = false,
                            CityId = 0,
                            IsBidirectional = true,
                            IsBlocked = false,
                            BlockedUntil = DateTime.MinValue,
                            AccidentCount = 0,
                            CongestionLevel = 0,
                            Density = 0,
                            FlowRate = 0,
                            Vehicles = new List<Vehicle>()
                        };

                        mergedNetwork.Edges.Add(edge);
                        startVertex.OutgoingEdges.Add(edge.Id);
                        endVertex.IncomingEdges.Add(edge.Id);
                    }
                }
            }

            await _repository.SaveAsync(mergedNetwork);
            return mergedNetwork;
        }

        public async Task<IEnumerable<RoadNetwork>> GetAllNetworksAsync()
        {
            return await _repository.GetAllAsync();
        }

        public async Task<bool> DeleteNetworkAsync(Guid networkId)
        {
            try
            {
                await _repository.DeleteAsync(networkId);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка удаления сети {networkId}: {ex.Message}");
                return false;
            }
        }

        #region Private Helper Methods

        private double CalculateDistance(Vertex v1, Vertex v2)
        {
            return Math.Sqrt(Math.Pow(v2.X - v1.X, 2) + Math.Pow(v2.Y - v1.Y, 2));
        }

        private Dictionary<Guid, Point> CalculateCircularLayout(RoadNetwork network)
        {
            var layout = new Dictionary<Guid, Point>();
            var vertices = network.Vertices.ToList();
            var centerX = 400;
            var centerY = 400;
            var radius = 300;

            for (int i = 0; i < vertices.Count; i++)
            {
                var angle = 2 * Math.PI * i / vertices.Count;
                var x = (int)(centerX + radius * Math.Cos(angle));
                var y = (int)(centerY + radius * Math.Sin(angle));

                layout[vertices[i].Id] = new Point(x, y);
            }

            return layout;
        }

        private Dictionary<Guid, Point> CalculateGridLayout(RoadNetwork network)
        {
            var layout = new Dictionary<Guid, Point>();
            var vertices = network.Vertices.ToList();
            var cols = (int)Math.Ceiling(Math.Sqrt(vertices.Count));

            for (int i = 0; i < vertices.Count; i++)
            {
                var row = i / cols;
                var col = i % cols;
                var x = 100 + col * 150;
                var y = 100 + row * 150;

                layout[vertices[i].Id] = new Point(x, y);
            }

            return layout;
        }

        private Dictionary<Guid, Point> CalculateRandomLayout(RoadNetwork network)
        {
            var layout = new Dictionary<Guid, Point>();
            var random = new Random();

            foreach (var vertex in network.Vertices)
            {
                var x = random.Next(100, 700);
                var y = random.Next(100, 700);
                layout[vertex.Id] = new Point(x, y);
            }

            return layout;
        }

        private Dictionary<Guid, Point> CalculateFruchtermanReingoldLayout(RoadNetwork network)
        {
            // Упрощенная реализация алгоритма Fruchterman-Reingold
            var layout = CalculateRandomLayout(network);
            var vertices = network.Vertices.ToList();
            var edges = network.Edges.ToList();

            const int iterations = 100;
            const double temperature = 100.0;
            double k = Math.Sqrt(400 * 400 / vertices.Count);

            for (int iter = 0; iter < iterations; iter++)
            {
                var displacements = new Dictionary<Guid, PointF>();
                foreach (var vertex in vertices)
                {
                    displacements[vertex.Id] = PointF.Empty;
                }

                // Отталкивание между всеми парами вершин
                for (int i = 0; i < vertices.Count; i++)
                {
                    for (int j = i + 1; j < vertices.Count; j++)
                    {
                        var v1 = vertices[i];
                        var v2 = vertices[j];
                        var pos1 = layout[v1.Id];
                        var pos2 = layout[v2.Id];

                        var dx = pos1.X - pos2.X;
                        var dy = pos1.Y - pos2.Y;
                        var distance = Math.Sqrt(dx * dx + dy * dy);

                        if (distance > 0)
                        {
                            var force = (k * k) / distance;
                            displacements[v1.Id] = new PointF(
                                displacements[v1.Id].X + (float)(force * dx / distance),
                                displacements[v1.Id].Y + (float)(force * dy / distance));

                            displacements[v2.Id] = new PointF(
                                displacements[v2.Id].X - (float)(force * dx / distance),
                                displacements[v2.Id].Y - (float)(force * dy / distance));
                        }
                    }
                }

                // Притяжение вдоль ребер
                foreach (var edge in edges)
                {
                    var v1 = vertices.First(v => v.Id == edge.StartVertexId);
                    var v2 = vertices.First(v => v.Id == edge.EndVertexId);

                    if (!layout.ContainsKey(v1.Id) || !layout.ContainsKey(v2.Id))
                        continue;

                    var pos1 = layout[v1.Id];
                    var pos2 = layout[v2.Id];

                    var dx = pos1.X - pos2.X;
                    var dy = pos1.Y - pos2.Y;
                    var distance = Math.Sqrt(dx * dx + dy * dy);

                    if (distance > 0)
                    {
                        var force = (distance * distance) / k;
                        displacements[v1.Id] = new PointF(
                            displacements[v1.Id].X - (float)(force * dx / distance),
                            displacements[v1.Id].Y - (float)(force * dy / distance));

                        displacements[v2.Id] = new PointF(
                            displacements[v2.Id].X + (float)(force * dx / distance),
                            displacements[v2.Id].Y + (float)(force * dy / distance));
                    }
                }

                // Применяем смещения с учетом температуры
                var currentTemp = temperature * (1.0 - (double)iter / iterations);
                foreach (var vertex in vertices)
                {
                    var disp = displacements[vertex.Id];
                    var dispLength = Math.Sqrt(disp.X * disp.X + disp.Y * disp.Y);

                    if (dispLength > 0)
                    {
                        var limitedLength = Math.Min(dispLength, currentTemp);
                        var pos = layout[vertex.Id];

                        layout[vertex.Id] = new Point(
                            (int)(pos.X + (disp.X / dispLength) * limitedLength),
                            (int)(pos.Y + (disp.Y / dispLength) * limitedLength));
                    }
                }
            }

            return layout;
        }

        // Реализация интерфейсного метода с меньшим количеством параметров
        public Task<RoadSegment> AddEdgeAsync(
            Guid networkId,
            Guid startVertexId,
            Guid endVertexId,
            RoadType type = RoadType.Urban,
            double length = 0,
            int lanes = 1,
            double maxSpeed = 50)
        {
            // Вызываем полную версию с параметрами по умолчанию
            return AddEdgeAsync(
                networkId,
                startVertexId,
                endVertexId,
                type,
                length,
                lanes,
                maxSpeed,
                false,  // hasCrosswalk по умолчанию
                true    // isBidirectional по умолчанию
            );
        }

        #endregion
    }
}