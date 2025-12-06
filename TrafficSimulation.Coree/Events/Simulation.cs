using TrafficSimulation.Core.Models;

namespace TrafficSimulation.Core.Events
{
    public class SimulationStepEventArgs : EventArgs
    {
        public int StepNumber { get; set; }
        public int CurrentTime { get; set; }
        public SimulationMetric Metrics { get; set; }
    }

    public class SimulationIncidentEventArgs : EventArgs
    {
        public TrafficIncident Incident { get; set; }
        public int SimulationTime { get; set; }
    }
}