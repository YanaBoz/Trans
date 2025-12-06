using TrafficSimulation.Core.Models;

namespace TrafficSimulation.Core.Services
{
    public interface IComparisonService
    {
        Task<SimulationComparison> CompareSessionsAsync(
            IEnumerable<Guid> sessionIds,
            ComparisonCriteria criteria);
        Task<OptimizationResult> FindOptimalConfigurationAsync(
            IEnumerable<SimulationParameters> candidates);
    }
}
