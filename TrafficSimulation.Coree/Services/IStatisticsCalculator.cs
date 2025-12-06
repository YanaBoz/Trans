using TrafficSimulation.Core.Models;

namespace TrafficSimulation.Core.Services
{
    public interface IStatisticsCalculator
    {
        SimulationMetric CalculateMetrics(SimulationSession session);
        ComparisonCriteria GetDefaultCriteria();
        List<KeyValuePair<string, double>> CalculatePerformanceIndicators(SimulationSession session);
    }
}