namespace TrafficSimulation.Core.Models
{
    public class SimulationStepResult
    {
        public double AverageVehicleSpeed { get; set; }
        public double CongestionLevel { get; set; }
        public List<TrafficIncident> Incidents { get; set; } = new List<TrafficIncident>();
    }
}