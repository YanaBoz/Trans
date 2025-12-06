using TrafficSimulation.Core.Models;

namespace TrafficSimulation.Core.Repositories
{
    public interface IRoadNetworkRepository
    {
        Task<RoadNetwork> GetByIdAsync(Guid id);
        Task<IEnumerable<RoadNetwork>> GetAllAsync();
        Task SaveAsync(RoadNetwork network);
        Task DeleteAsync(Guid id);
        Task<RoadNetwork> CloneAsync(RoadNetwork network, string newName);
    }
}
