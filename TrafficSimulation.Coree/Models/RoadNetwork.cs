namespace TrafficSimulation.Core.Models
{
    public enum RoadType { Urban, Highway }
    public enum VertexType { Intersection, Terminal }

    public class RoadNetwork : Entity
    {
        public List<Vertex> Vertices { get; set; } = new();
        public List<RoadSegment> Edges { get; set; } = new();
        public string Description { get; set; }
        public int Version { get; set; } = 1;

        public RoadNetwork(string name) : base(name)
        {
            Description = $"Дорожная сеть '{name}'";
        }

        public Vertex GetVertex(Guid id) => Vertices.FirstOrDefault(v => v.Id == id);
        public RoadSegment GetEdge(Guid id) => Edges.FirstOrDefault(e => e.Id == id);

        public List<RoadSegment> GetOutgoingEdges(Guid vertexId)
        {
            return Edges.Where(e => e.StartVertexId == vertexId).ToList();
        }

        public List<RoadSegment> GetIncomingEdges(Guid vertexId)
        {
            return Edges.Where(e => e.EndVertexId == vertexId).ToList();
        }

        public void AddVertex(Vertex vertex)
        {
            if (Vertices.Any(v => v.Id == vertex.Id))
                throw new ArgumentException($"Вершина с Id {vertex.Id} уже существует");
            Vertices.Add(vertex);
            UpdateModifiedDate();
        }

        public void AddEdge(RoadSegment edge)
        {
            if (Edges.Any(e => e.Id == edge.Id))
                throw new ArgumentException($"Ребро с Id {edge.Id} уже существует");

            // Проверяем существование вершин
            if (!Vertices.Any(v => v.Id == edge.StartVertexId))
                throw new ArgumentException($"Начальная вершина {edge.StartVertexId} не существует");
            if (!Vertices.Any(v => v.Id == edge.EndVertexId))
                throw new ArgumentException($"Конечная вершина {edge.EndVertexId} не существует");

            Edges.Add(edge);

            // Обновляем ссылки в вершинах
            var startVertex = GetVertex(edge.StartVertexId);
            var endVertex = GetVertex(edge.EndVertexId);

            startVertex.OutgoingEdges.Add(edge.Id);
            endVertex.IncomingEdges.Add(edge.Id);

            UpdateModifiedDate();
        }

        public void RemoveVertex(Guid vertexId)
        {
            var vertex = GetVertex(vertexId);
            if (vertex == null) return;

            // Удаляем связанные ребра
            var edgesToRemove = Edges.Where(e =>
                e.StartVertexId == vertexId || e.EndVertexId == vertexId).ToList();

            foreach (var edge in edgesToRemove)
            {
                RemoveEdge(edge.Id);
            }

            Vertices.Remove(vertex);
            UpdateModifiedDate();
        }

        public void RemoveEdge(Guid edgeId)
        {
            var edge = GetEdge(edgeId);
            if (edge == null) return;

            // Удаляем ссылки из вершин
            var startVertex = GetVertex(edge.StartVertexId);
            var endVertex = GetVertex(edge.EndVertexId);

            startVertex?.OutgoingEdges.Remove(edgeId);
            endVertex?.IncomingEdges.Remove(edgeId);

            Edges.Remove(edge);
            UpdateModifiedDate();
        }

        public RoadNetwork Clone(string newName)
        {
            var clone = new RoadNetwork(newName)
            {
                Description = $"Копия сети '{Name}'",
                Version = Version
            };

            // Клонируем вершины
            var vertexMap = new Dictionary<Guid, Guid>();
            foreach (var vertex in Vertices)
            {
                var newVertex = new Vertex
                {
                    Id = Guid.NewGuid(),
                    Name = vertex.Name,
                    X = vertex.X,
                    Y = vertex.Y,
                    Type = vertex.Type,
                    HasTrafficLights = vertex.HasTrafficLights,
                    CityId = vertex.CityId,
                    TrafficLightPhase = vertex.TrafficLightPhase
                };
                clone.Vertices.Add(newVertex);
                vertexMap[vertex.Id] = newVertex.Id;
            }

            // Клонируем ребра
            foreach (var edge in Edges)
            {
                var newEdge = new RoadSegment
                {
                    Id = Guid.NewGuid(),
                    Name = edge.Name,
                    StartVertexId = vertexMap[edge.StartVertexId],
                    EndVertexId = vertexMap[edge.EndVertexId],
                    Length = edge.Length,
                    Lanes = edge.Lanes,
                    MaxSpeed = edge.MaxSpeed,
                    Type = edge.Type,
                    HasCrosswalk = edge.HasCrosswalk,
                    CityId = edge.CityId,
                    IsBidirectional = edge.IsBidirectional
                };
                clone.Edges.Add(newEdge);
            }

            // Обновляем списки входящих/исходящих ребер
            foreach (var vertex in clone.Vertices)
            {
                var originalVertex = Vertices.First(v => v.Id == vertexMap.First(kvp => kvp.Value == vertex.Id).Key);
                vertex.IncomingEdges = originalVertex.IncomingEdges
                    .Select(edgeId =>
                    {
                        var originalEdge = GetEdge(edgeId);
                        var clonedEdge = clone.Edges.FirstOrDefault(e =>
                            e.StartVertexId == vertexMap[originalEdge.StartVertexId] &&
                            e.EndVertexId == vertexMap[originalEdge.EndVertexId]);
                        return clonedEdge?.Id ?? Guid.Empty;
                    })
                    .Where(id => id != Guid.Empty)
                    .ToList();

                vertex.OutgoingEdges = originalVertex.OutgoingEdges
                    .Select(edgeId =>
                    {
                        var originalEdge = GetEdge(edgeId);
                        var clonedEdge = clone.Edges.FirstOrDefault(e =>
                            e.StartVertexId == vertexMap[originalEdge.StartVertexId] &&
                            e.EndVertexId == vertexMap[originalEdge.EndVertexId]);
                        return clonedEdge?.Id ?? Guid.Empty;
                    })
                    .Where(id => id != Guid.Empty)
                    .ToList();
            }

            return clone;
        }
    }
    public class Vertex : Entity
    {
        public int X { get; set; }
        public int Y { get; set; }
        public VertexType Type { get; set; }
        public bool HasTrafficLights { get; set; }
        public int CityId { get; set; }
        public int TrafficLightPhase { get; set; }
        public Guid? GreenDirectionEdgeId { get; set; }
        public List<Guid> IncomingEdges { get; set; } = new();
        public List<Guid> OutgoingEdges { get; set; } = new();
    }

    public class RoadSegment : Entity
    {
        public Guid StartVertexId { get; set; }
        public Guid EndVertexId { get; set; }
        public double Length { get; set; } // метры
        public int Lanes { get; set; }
        public double MaxSpeed { get; set; } // км/ч
        public RoadType Type { get; set; }
        public double Density { get; set; }
        public double FlowRate { get; set; }
        public bool HasCrosswalk { get; set; }
        public int CityId { get; set; } // 1, 2 или 0 для highway
        public bool IsBlocked { get; set; }
        public DateTime BlockedUntil { get; set; }
        public int AccidentCount { get; set; }
        public double CongestionLevel { get; set; }
        public bool IsBidirectional { get; set; }

        // Навигационные свойства
        public Vertex StartVertex { get; set; }
        public Vertex EndVertex { get; set; }
        public List<Vehicle> Vehicles { get; set; } = new();
    }

    public class TrafficIncident : Entity
    {
        public IncidentType Type { get; set; }
        public Guid LocationEdgeId { get; set; }
        public DateTime Time { get; set; }
        public IncidentSeverity Severity { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
    }

    public enum IncidentType { Accident, VehiclePedestrianConflict, RoadBlocked, RoadUnblocked }
    public enum IncidentSeverity { Low, Medium, High }
}