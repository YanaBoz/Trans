using System.Drawing;
using TrafficSimulation.Core.Models;

namespace TrafficSimulation.Core.Services
{
    public interface IGraphEditorService
    {
        Task<RoadNetwork> CreateNetworkAsync(string name);
        Task<RoadNetwork> LoadNetworkAsync(Guid networkId);
        Task<bool> SaveNetworkAsync(RoadNetwork network);
        Task<RoadNetwork> CloneNetworkAsync(Guid networkId, string newName);

        Task<Vertex> AddVertexAsync(
            Guid networkId,
            int x,
            int y,
            VertexType type = VertexType.Intersection,
            int cityId = 1,
            bool hasTrafficLights = true);

        Task<RoadSegment> AddEdgeAsync(
            Guid networkId,
            Guid startVertexId,
            Guid endVertexId,
            RoadType type = RoadType.Urban,
            double length = 0,
            int lanes = 1,
            double maxSpeed = 50);

        Task<bool> RemoveVertexAsync(Guid networkId, Guid vertexId);
        Task<bool> RemoveEdgeAsync(Guid networkId, Guid edgeId);

        Task<bool> UpdateVertexAsync(Guid networkId, Vertex vertex);
        Task<bool> UpdateEdgeAsync(Guid networkId, RoadSegment edge);

        Task<bool> SetTrafficLightsAsync(Guid networkId, Guid vertexId, bool enabled);
        Task<bool> SetCrosswalkAsync(Guid networkId, Guid edgeId, bool enabled);

        Task<Dictionary<Guid, Point>> CalculateLayoutAsync(
            Guid networkId,
            LayoutAlgorithm algorithm = LayoutAlgorithm.FruchtermanReingold);

        Task<bool> ValidateNetworkAsync(Guid networkId);
        Task<List<string>> GetNetworkIssuesAsync(Guid networkId);

        Task<RoadNetwork> GenerateGridNetworkAsync(
            string name,
            int width,
            int height,
            int cellSize = 200,
            int cityId = 1);

        Task<RoadNetwork> MergeNetworksAsync(
            string name,
            IEnumerable<Guid> networkIds,
            IEnumerable<Tuple<Guid, Guid>> connections);
        Task<IEnumerable<RoadNetwork>> GetAllNetworksAsync();
        Task<bool> DeleteNetworkAsync(Guid networkId);

        // Обновленный метод AddEdge с дополнительными параметрами
        Task<RoadSegment> AddEdgeAsync(
            Guid networkId,
            Guid startVertexId,
            Guid endVertexId,
            RoadType type = RoadType.Urban,
            double length = 0,
            int lanes = 1,
            double maxSpeed = 50,
            bool hasCrosswalk = false, // Добавить этот параметр
            bool isBidirectional = true); // Добавить этот параметр
    }
    public enum LayoutAlgorithm { FruchtermanReingold, Circular, Grid, Random }
}