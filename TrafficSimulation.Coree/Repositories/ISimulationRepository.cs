using TrafficSimulation.Core.Models;

namespace TrafficSimulation.Core.Repositories
{
    public interface ISimulationRepository
    {
        Task<SimulationSession?> GetSessionAsync(Guid sessionId);
        Task<IEnumerable<SimulationSession>> GetSessionsByNetworkAsync(Guid networkId);
        Task SaveSessionAsync(SimulationSession session);
        Task DeleteSessionAsync(Guid sessionId);
        Task<IEnumerable<SimulationSession>> GetAllSessionsAsync();
        Task<IEnumerable<SimulationMetric>> GetSessionMetricsAsync(Guid sessionId);
    }
}