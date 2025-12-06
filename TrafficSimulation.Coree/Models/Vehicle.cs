namespace TrafficSimulation.Core.Models
{
    public enum VehicleType { Car, NoviceCar, Bus, Truck, Special, Bicycle }
    public enum DriverStyle { Aggressive, Normal, Cautious }

    public class Vehicle : Entity
    {
        public VehicleType Type { get; set; }
        public DriverStyle DriverStyle { get; set; }
        public double MaxSpeed { get; set; } // км/ч
        public double CurrentSpeed { get; set; }
        public double Acceleration { get; set; } // м/с²
        public Position Position { get; set; }
        public List<RoadSegment> Route { get; set; } = new();
        public double Delay { get; set; }
        public double TotalTravelTime { get; set; }
        public double FreeFlowTravelTime { get; set; }
        public double ReactionTime { get; set; }
        public double WaitingTime { get; set; }
        public double DistanceTraveled { get; set; }
        public Guid? CurrentEdgeId { get; set; }
        public bool DestinationReached { get; set; }

        private static readonly Dictionary<VehicleType, (double MaxSpeed, double Acceleration)>
            TypeParams = new()
            {
                [VehicleType.Car] = (90, 3.0),
                [VehicleType.NoviceCar] = (70, 2.5),
                [VehicleType.Bus] = (90, 2.0),
                [VehicleType.Truck] = (70, 1.5),
                [VehicleType.Special] = (40, 2.0),
                [VehicleType.Bicycle] = (15, 1.0)
            };

        public Vehicle(VehicleType type, DriverStyle style) : base()
        {
            Type = type;
            DriverStyle = style;
            var parameters = TypeParams[type];
            MaxSpeed = parameters.MaxSpeed;
            Acceleration = parameters.Acceleration;
            CurrentSpeed = MaxSpeed * MathHelpers.RandomLogNormal(-0.1, 0.2);
            ReactionTime = Math.Max(0.3, Math.Min(3.0, MathHelpers.RandomLogNormal(-0.2, 0.3)));
            Position = new Position();
        }

        // Дополнительные методы
        public double CalculateFreeFlowTravelTime(RoadSegment edge)
        {
            double freeFlowSpeed = Math.Min(MaxSpeed, edge.MaxSpeed);
            return edge.Length / (freeFlowSpeed / 3.6); // секунды
        }

        public void ResetForNewEdge(RoadSegment newEdge)
        {
            FreeFlowTravelTime = CalculateFreeFlowTravelTime(newEdge);
            WaitingTime = 0;
        }

        public void UpdateSpeed(double density, double roadMaxSpeed, double dt = 5)
        {
            const double criticalDensity = 0.25;
            const double jamDensity = 1.8;

            double desiredSpeed;
            if (density <= criticalDensity)
                desiredSpeed = Math.Min(MaxSpeed, roadMaxSpeed);
            else
            {
                double alpha = 1.5;
                double reduction = 1 - Math.Pow((density - criticalDensity) /
                    (jamDensity - criticalDensity), alpha);
                desiredSpeed = Math.Min(MaxSpeed, roadMaxSpeed) * Math.Max(0.05, reduction);
            }

            // Учет стиля вождения
            double styleFactor = DriverStyle switch
            {
                DriverStyle.Aggressive => MathHelpers.RandomNormal(1.15, 0.05),
                DriverStyle.Cautious => MathHelpers.RandomNormal(0.85, 0.05),
                _ => MathHelpers.RandomNormal(1.0, 0.03)
            };

            desiredSpeed *= styleFactor;

            // Динамика изменения скорости
            double speedDiff = desiredSpeed - CurrentSpeed;
            double baseAcceleration = speedDiff > 0 ? Acceleration : Acceleration * 2.5;
            double accelerationVariation = MathHelpers.RandomNormal(1.0, 0.1);
            double effectiveAcceleration = baseAcceleration * accelerationVariation;

            CurrentSpeed += speedDiff * dt / (effectiveAcceleration * ReactionTime);
            CurrentSpeed = Math.Max(0, Math.Min(CurrentSpeed, desiredSpeed));

            // Расчет задержки
            double freeFlowSpeed = Math.Min(MaxSpeed, roadMaxSpeed);
            if (freeFlowSpeed > 0)
            {
                double timeLoss = (freeFlowSpeed - CurrentSpeed) * (dt / 3600);
                Delay += Math.Max(0, timeLoss);
            }
        }
    }
}