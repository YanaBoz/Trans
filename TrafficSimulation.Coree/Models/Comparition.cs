namespace TrafficSimulation.Core.Models
{
    public class ComparisonCriteria
    {
        public bool IncludeAverageSpeed { get; set; } = true;
        public bool IncludeThroughput { get; set; } = true;
        public bool IncludeCongestionLevel { get; set; } = true;
        public bool IncludeAccidents { get; set; } = true;
        public bool IncludeTotalDelay { get; set; } = true;
        public bool IncludeTrafficLightEfficiency { get; set; } = false;
        public Dictionary<string, double> Weights { get; set; } = new()
        {
            ["AverageSpeed"] = 0.25,
            ["Throughput"] = 0.30,
            ["CongestionLevel"] = 0.20,
            ["Accidents"] = 0.15,
            ["TotalDelay"] = 0.10
        };
    }

    public class SimulationComparison
    {
        public List<ComparisonResult> Results { get; set; } = new();
        public ComparisonCriteria Criteria { get; set; }
        public DateTime ComparisonDate { get; set; }
        public string Summary { get; set; }
    }

    public class ComparisonResult
    {
        public Guid SessionId { get; set; }
        public string SessionName { get; set; }
        public SimulationParameters Parameters { get; set; }
        public int TotalVehicles { get; set; }
        public int TotalPedestrians { get; set; }
        public double AverageVehicleSpeed { get; set; }
        public double AveragePedestrianSpeed { get; set; }
        public double AverageCongestionLevel { get; set; }
        public double TotalDelay { get; set; }
        public int VehicleThroughput { get; set; }
        public int PedestrianThroughput { get; set; }
        public int AccidentCount { get; set; }
        public double BlockedRoadsTime { get; set; }
        public double Score { get; set; }
    }

    public class OptimizationResult
    {
        public SimulationParameters OptimalParameters { get; set; }
        public double Score { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }
}