namespace TrafficSimulation.Core.Models
{
    public class SimulationSession : Entity
    {
        public Guid NetworkId { get; set; }
        public RoadNetwork Network { get; set; }
        public SimulationParameters Parameters { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int CurrentTime { get; set; } // в секундах
        public int StepCount { get; set; }
        public SimulationState State { get; set; }
        public List<Vehicle> Vehicles { get; set; } = new();
        public List<Pedestrian> Pedestrians { get; set; } = new();
        public int CompletedVehiclesCount { get; set; }
        public int CompletedPedestriansCount { get; set; }
        public List<SimulationMetric> Metrics { get; set; } = new();
        public List<TrafficIncident> Incidents { get; set; } = new();
        public bool IsSaved { get; set; }
        public Dictionary<Guid, double> EdgeDensities { get; set; } = new();

        public SimulationSession(string name, Guid networkId, SimulationParameters parameters) : base(name)
        {
            NetworkId = networkId;
            Parameters = parameters;
            StartTime = DateTime.Now;
            State = SimulationState.Stopped;
        }

        public void Start()
        {
            if (State != SimulationState.Stopped)
                throw new InvalidOperationException("Симуляция уже запущена или на паузе");

            StartTime = DateTime.Now;
            State = SimulationState.Running;
            UpdateModifiedDate();
        }

        public void Pause()
        {
            if (State != SimulationState.Running)
                throw new InvalidOperationException("Симуляция не запущена");

            State = SimulationState.Paused;
            UpdateModifiedDate();
        }

        public void Resume()
        {
            if (State != SimulationState.Paused)
                throw new InvalidOperationException("Симуляция не на паузе");

            State = SimulationState.Running;
            UpdateModifiedDate();
        }

        public void Stop()
        {
            EndTime = DateTime.Now;
            State = SimulationState.Stopped;
            UpdateModifiedDate();
        }

        public TimeSpan GetDuration()
        {
            if (EndTime.HasValue)
                return EndTime.Value - StartTime;
            return DateTime.Now - StartTime;
        }

        public void AddMetric(SimulationMetric metric)
        {
            Metrics.Add(metric);
        }

        public void AddIncident(TrafficIncident incident)
        {
            Incidents.Add(incident);
            UpdateModifiedDate();
        }

        public void ClearCompletedAgents()
        {
            Vehicles.RemoveAll(v => v.DestinationReached);
            Pedestrians.RemoveAll(p => p.DestinationReached);
        }

        public double CalculateAverageVehicleSpeed()
        {
            if (!Vehicles.Any()) return 0;
            return Vehicles.Average(v => v.CurrentSpeed);
        }

        public double CalculateAveragePedestrianSpeed()
        {
            if (!Pedestrians.Any()) return 0;
            return Pedestrians.Average(p => p.CurrentSpeed);
        }

        public double CalculateTotalDelay()
        {
            return Vehicles.Sum(v => v.Delay);
        }

        public int GetActiveIncidentsCount()
        {
            return Incidents.Count(i => i.IsActive);
        }

        public int GetBlockedRoadsCount()
        {
            if (Network == null) return 0;
            return Network.Edges.Count(e => e.IsBlocked);
        }
    }

    public enum SimulationState { Stopped, Running, Paused }
    public class SimulationMetric
    {
        public Guid Id { get; set; }
        public Guid SessionId { get; set; }
        public DateTime Timestamp { get; set; }
        public int SimulationTime { get; set; } // в секундах
        public int VehicleCount { get; set; }
        public int PedestrianCount { get; set; }
        public double AverageVehicleSpeed { get; set; }
        public double AveragePedestrianSpeed { get; set; }
        public double CongestionLevel { get; set; }
        public int ActiveIncidents { get; set; }
        public int BlockedRoadsCount { get; set; }
        public double TotalDelay { get; set; }
        public int VehicleThroughput { get; set; }
        public int PedestrianThroughput { get; set; }
        public double AverageTravelTime { get; set; }
        public double NetworkUtilization { get; set; }
        public int AccidentCount { get; set; }

        public SimulationMetric()
        {
            Id = Guid.NewGuid();
            Timestamp = DateTime.Now;
        }

        public SimulationMetric(Guid sessionId, int simulationTime) : this()
        {
            SessionId = sessionId;
            SimulationTime = simulationTime;
        }

        public string ToCsvRow()
        {
            return $"{Timestamp:yyyy-MM-dd HH:mm:ss},{SimulationTime},{VehicleCount},{PedestrianCount}," +
                   $"{AverageVehicleSpeed:F2},{AveragePedestrianSpeed:F2},{CongestionLevel:F2}," +
                   $"{ActiveIncidents},{BlockedRoadsCount},{TotalDelay:F2}," +
                   $"{VehicleThroughput},{PedestrianThroughput},{AverageTravelTime:F2}";
        }

        public static string GetCsvHeader()
        {
            return "Timestamp,SimulationTime,VehicleCount,PedestrianCount," +
                   "AverageVehicleSpeed,AveragePedestrianSpeed,CongestionLevel," +
                   "ActiveIncidents,BlockedRoadsCount,TotalDelay," +
                   "VehicleThroughput,PedestrianThroughput,AverageTravelTime";
        }
    }

    public class SimulationParameters : Entity
    {
        public string? Name { get; set; }
        public DateTime CreationDate { get; set; }

        // Основные параметры
        public int InitialVehicles { get; set; } = 15;
        public int InitialPedestrians { get; set; } = 25;
        public double VehicleIntensity { get; set; } = 0.8;
        public double PedestrianIntensity { get; set; } = 2.0;
        public double AccidentProbabilityFactor { get; set; } = 0.001;
        public double NoAccidentChance { get; set; } = 0.95;
        public int BlockDurationSeconds { get; set; } = 300;
        public double CongestionThreshold { get; set; } = 0.7;
        public int TimeStepSeconds { get; set; } = 5;
        public int SimulationDurationSeconds { get; set; } = 3600;

        // Распределения
        public Dictionary<VehicleType, double> VehicleTypeDistribution { get; set; } = new()
        {
            [VehicleType.Car] = 0.65,
            [VehicleType.NoviceCar] = 0.08,
            [VehicleType.Bus] = 0.04,
            [VehicleType.Truck] = 0.12,
            [VehicleType.Special] = 0.03,
            [VehicleType.Bicycle] = 0.08
        };

        public Dictionary<DriverStyle, double> DriverStyleDistribution { get; set; } = new()
        {
            [DriverStyle.Aggressive] = 0.15,
            [DriverStyle.Normal] = 0.70,
            [DriverStyle.Cautious] = 0.15
        };

        // Временные периоды
        public List<TimePeriod> TimePeriods { get; set; } = new()
        {
            new("Ночь", 0, 6, 0.3),
            new("Утренний час пик", 6, 10, 1.5),
            new("День", 10, 16, 1.0),
            new("Вечерний час пик", 16, 20, 1.8),
            new("Вечер", 20, 24, 0.7)
        };

        public TimeOfDay CurrentTimeOfDay { get; set; } = TimeOfDay.Day;

        public double GetCurrentIntensityFactor()
        {
            var period = TimePeriods.FirstOrDefault(p =>
                p.StartHour <= (int)CurrentTimeOfDay && (int)CurrentTimeOfDay < p.EndHour);
            return period?.IntensityFactor ?? 1.0;
        }
    }

    public class TimePeriod
    {
        public string Name { get; set; }
        public int StartHour { get; set; }
        public int EndHour { get; set; }
        public double IntensityFactor { get; set; }

        public TimePeriod(string name, int start, int end, double factor)
        {
            Name = name;
            StartHour = start;
            EndHour = end;
            IntensityFactor = factor;
        }
    }

    public enum TimeOfDay { Night = 2, MorningPeak = 8, Day = 12, EveningPeak = 18, Evening = 22 }
}