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
            _repository = repository;
            _random = new Random();
        }

        public async Task<RoadNetwork> CreateNetworkAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Network name cannot be empty", nameof(name));

            var network = new RoadNetwork(name)
            {
                Description = $"Network created on {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
            };

            await _repository.SaveAsync(network);
            return network;
        }

        public async Task<RoadNetwork> LoadNetworkAsync(Guid networkId)
        {
            var network = await _repository.GetByIdAsync(networkId);
            if (network == null)
                throw new KeyNotFoundException($"Network with id {networkId} not found");

            return network;
        }

        public async Task<bool> SaveNetworkAsync(RoadNetwork network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));

            try
            {
                await _repository.SaveAsync(network);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving network: {ex.Message}");
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
            double maxSpeed = 50)
        {
            var network = await LoadNetworkAsync(networkId);

            var startVertex = network.Vertices.FirstOrDefault(v => v.Id == startVertexId);
            var endVertex = network.Vertices.FirstOrDefault(v => v.Id == endVertexId);

            if (startVertex == null)
                throw new ArgumentException($"Start vertex {startVertexId} not found", nameof(startVertexId));
            if (endVertex == null)
                throw new ArgumentException($"End vertex {endVertexId} not found", nameof(endVertexId));

            if (startVertex.Id == endVertex.Id)
                throw new ArgumentException("Cannot create edge from a vertex to itself");

            // Проверяем, не существует ли уже такое ребро
            var existingEdge = network.Edges.FirstOrDefault(e =>
                e.StartVertexId == startVertexId && e.EndVertexId == endVertexId);
            if (existingEdge != null)
                throw new InvalidOperationException($"Edge from {startVertexId} to {endVertexId} already exists");

            // Если длина не указана, вычисляем расстояние между вершинами
            if (length <= 0)
            {
                length = CalculateDistance(startVertex, endVertex);
            }

            var edge = new RoadSegment
            {
                Id = Guid.NewGuid(),
                Name = $"E{startVertex.Name}-{endVertex.Name}",
                StartVertexId = startVertexId,
                EndVertexId = endVertexId,
                Length = length,
                Lanes = Math.Max(1, lanes),
                MaxSpeed = Math.Max(10, Math.Min(120, maxSpeed)), // Ограничиваем скорость 10-120 км/ч
                Type = type,
                HasCrosswalk = type == RoadType.Urban && _random.NextDouble() < 0.4,
                CityId = startVertex.CityId == endVertex.CityId ? startVertex.CityId : 0,
                IsBlocked = false,
                BlockedUntil = DateTime.MinValue,
                AccidentCount = 0,
                CongestionLevel = 0,
                Density = 0,
                FlowRate = 0,
                IsBidirectional = false,
                Vehicles = new List<Vehicle>()
            };

            network.Edges.Add(edge);

            // Обновляем списки ребер в вершинах
            startVertex.OutgoingEdges.Add(edge.Id);
            endVertex.IncomingEdges.Add(edge.Id);

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
                    issues.Add($"Vertex {vertex.Name} ({vertex.Id}) is isolated (no incoming or outgoing edges)");
                }
            }

            // Проверка дублирующих ребер
            var edgeGroups = network.Edges
                .GroupBy(e => new { e.StartVertexId, e.EndVertexId })
                .Where(g => g.Count() > 1);

            foreach (var group in edgeGroups)
            {
                issues.Add($"Multiple edges from {group.Key.StartVertexId} to {group.Key.EndVertexId}");
            }

            // Проверка очень коротких ребер
            foreach (var edge in network.Edges.Where(e => e.Length < 10))
            {
                issues.Add($"Edge {edge.Name} is very short ({edge.Length:F1}m)");
            }

            // Проверка некорректных скоростей
            foreach (var edge in network.Edges)
            {
                if (edge.MaxSpeed < 10 || edge.MaxSpeed > 120)
                {
                    issues.Add($"Edge {edge.Name} has invalid speed ({edge.MaxSpeed} km/h)");
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
                throw new ArgumentException("Width and height must be at least 1");

            var network = new RoadNetwork(name)
            {
                Description = $"Grid network {width}x{height}, cell size: {cellSize}m"
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
                        TrafficLightPhase = 0
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

                    var edge = CreateGridEdge(from, to, cellSize);
                    network.Edges.Add(edge);

                    from.OutgoingEdges.Add(edge.Id);
                    to.IncomingEdges.Add(edge.Id);
                }
            }

            // Создаем вертикальные ребра
            for (int row = 0; row < height - 1; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    var from = vertices[row, col];
                    var to = vertices[row + 1, col];

                    var edge = CreateGridEdge(from, to, cellSize);
                    network.Edges.Add(edge);

                    from.OutgoingEdges.Add(edge.Id);
                    to.IncomingEdges.Add(edge.Id);
                }
            }

            await _repository.SaveAsync(network);
            return network;
        }

        public async Task<RoadNetwork> MergeNetworksAsync(
            string name,
            IEnumerable<Guid> networkIds,
            IEnumerable<Tuple<Guid, Guid>> connections)
        {
            var mergedNetwork = new RoadNetwork(name)
            {
                Description = $"Merged network created on {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
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
                        IsBidirectional = edge.IsBidirectional
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
            foreach (var connection in connections ?? Enumerable.Empty<Tuple<Guid, Guid>>())
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
                        IsBidirectional = true
                    };

                    mergedNetwork.Edges.Add(edge);
                    startVertex.OutgoingEdges.Add(edge.Id);
                    endVertex.IncomingEdges.Add(edge.Id);
                }
            }

            await _repository.SaveAsync(mergedNetwork);
            return mergedNetwork;
        }

        public RoadNetwork CreateNewNetwork(string name, int city1Size = 25, int city2Size = 25)
        {
            var network = new RoadNetwork(name)
            {
                Description = $"Two cities network with {city1Size} and {city2Size} vertices"
            };

            // Создаем первый город
            var city1OffsetX = 0;
            var city1OffsetY = 0;
            CreateGridCity(network, 1, city1Size, city1OffsetX, city1OffsetY, 5, 5);

            // Создаем второй город
            var city2OffsetX = 2000;
            var city2OffsetY = 0;
            CreateGridCity(network, 2, city2Size, city2OffsetX, city2OffsetY, 5, 5);

            // Создаем скоростные дороги между городами
            CreateHighwayConnections(network);

            return network;
        }

        #region Private Helper Methods

        private double CalculateDistance(Vertex v1, Vertex v2)
        {
            return Math.Sqrt(Math.Pow(v2.X - v1.X, 2) + Math.Pow(v2.Y - v1.Y, 2));
        }

        private RoadSegment CreateGridEdge(Vertex from, Vertex to, int cellSize)
        {
            var length = CalculateDistance(from, to);

            return new RoadSegment
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
                IsBidirectional = true
            };
        }

        private void CreateGridCity(RoadNetwork network, int cityId, int size, int offsetX, int offsetY, int gridCols, int gridRows)
        {
            var vertices = new List<Vertex>();
            var cellSize = 200;

            // Создаем вершины сетки
            for (int row = 0; row < gridRows; row++)
            {
                for (int col = 0; col < gridCols; col++)
                {
                    var vertex = new Vertex
                    {
                        Id = Guid.NewGuid(),
                        Name = $"City{cityId}_V{row}_{col}",
                        X = offsetX + col * cellSize,
                        Y = offsetY + row * cellSize,
                        Type = VertexType.Intersection,
                        HasTrafficLights = _random.NextDouble() < 0.7,
                        CityId = cityId,
                        TrafficLightPhase = 0
                    };

                    vertices.Add(vertex);
                    network.Vertices.Add(vertex);
                }
            }

            // Горизонтальные связи
            for (int row = 0; row < gridRows; row++)
            {
                for (int col = 0; col < gridCols - 1; col++)
                {
                    var from = vertices[row * gridCols + col];
                    var to = vertices[row * gridCols + col + 1];

                    CreateCityRoad(network, from, to);
                    CreateCityRoad(network, to, from); // Обратное направление
                }
            }

            // Вертикальные связи
            for (int col = 0; col < gridCols; col++)
            {
                for (int row = 0; row < gridRows - 1; row++)
                {
                    var from = vertices[row * gridCols + col];
                    var to = vertices[(row + 1) * gridCols + col];

                    CreateCityRoad(network, from, to);
                    CreateCityRoad(network, to, from); // Обратное направление
                }
            }
        }

        private void CreateCityRoad(RoadNetwork network, Vertex from, Vertex to)
        {
            var road = new RoadSegment
            {
                Id = Guid.NewGuid(),
                Name = $"City{from.CityId}_E{from.Name}-{to.Name}",
                StartVertexId = from.Id,
                EndVertexId = to.Id,
                Length = CalculateDistance(from, to),
                Lanes = new[] { 1, 2 }[_random.Next(2)],
                MaxSpeed = new[] { 40, 50, 60 }[_random.Next(3)],
                Type = RoadType.Urban,
                HasCrosswalk = _random.NextDouble() < 0.4,
                CityId = from.CityId,
                IsBidirectional = true
            };

            network.Edges.Add(road);
            from.OutgoingEdges.Add(road.Id);
            to.IncomingEdges.Add(road.Id);
        }

        private void CreateHighwayConnections(RoadNetwork network)
        {
            var city1Vertices = network.Vertices.Where(v => v.CityId == 1).ToList();
            var city2Vertices = network.Vertices.Where(v => v.CityId == 2).ToList();

            if (!city1Vertices.Any() || !city2Vertices.Any())
                return;

            // Находим ближайшие вершины для соединения
            var city1Edge = city1Vertices.OrderBy(v => v.X).LastOrDefault();
            var city2Edge = city2Vertices.OrderBy(v => v.X).FirstOrDefault();

            if (city1Edge != null && city2Edge != null)
            {
                CreateHighwayRoad(network, city1Edge, city2Edge);
                CreateHighwayRoad(network, city2Edge, city1Edge);
            }
        }

        private void CreateHighwayRoad(RoadNetwork network, Vertex from, Vertex to)
        {
            var road = new RoadSegment
            {
                Id = Guid.NewGuid(),
                Name = $"Highway_{from.Name}-{to.Name}",
                StartVertexId = from.Id,
                EndVertexId = to.Id,
                Length = CalculateDistance(from, to),
                Lanes = 2,
                MaxSpeed = 90,
                Type = RoadType.Highway,
                HasCrosswalk = false,
                CityId = 0,
                IsBidirectional = true
            };

            network.Edges.Add(road);
            from.OutgoingEdges.Add(road.Id);
            to.IncomingEdges.Add(road.Id);
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

        #endregion
    }
}