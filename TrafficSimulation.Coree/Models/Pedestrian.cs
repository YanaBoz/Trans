namespace TrafficSimulation.Core.Models
{
    public enum PedestrianType { Adult, Child, Elderly, Disabled }

    public class Pedestrian : Entity
    {
        public PedestrianType Type { get; set; }
        public double MaxSpeed { get; set; } // км/ч
        public double CurrentSpeed { get; set; }
        public Position Position { get; set; }
        public double PanicLevel { get; set; }
        public double WaitingTime { get; set; }
        public bool IsMoving { get; set; }
        public double Patience { get; set; }
        public double DistanceTraveled { get; set; }
        public bool DestinationReached { get; set; }

        private static readonly Dictionary<PedestrianType, double> TypeParams = new()
        {
            [PedestrianType.Adult] = 5.0,
            [PedestrianType.Child] = 4.0,
            [PedestrianType.Elderly] = 3.0,
            [PedestrianType.Disabled] = 2.0
        };

        public Pedestrian(PedestrianType type)
        {
            Type = type;
            MaxSpeed = TypeParams[type];
            CurrentSpeed = MaxSpeed * (0.9 + new Random().NextDouble() * 0.2);
            Patience = MathHelpers.RandomExponential(30);
            IsMoving = true;
        }

        public void UpdateSpeed(double localDensity, double dt = 5)
        {
            const double criticalDensity = 3.5;
            const double jamDensity = 5.0;

            if (localDensity >= criticalDensity)
            {
                double speedReduction = 1 - Math.Pow(
                    (localDensity - criticalDensity) / (jamDensity - criticalDensity), 1.8);
                CurrentSpeed = MaxSpeed * Math.Max(0.1, speedReduction);
            }
            else
            {
                CurrentSpeed = MaxSpeed * MathHelpers.RandomNormal(0.95, 0.08);
            }

            // Учет паники
            if (PanicLevel > 0)
            {
                double panicEffect = 1 + 0.6 / (1 + Math.Exp(-10 * (PanicLevel - 0.5)));
                CurrentSpeed *= panicEffect;
            }

            CurrentSpeed = Math.Min(MaxSpeed * 1.5, Math.Max(0.1, CurrentSpeed));
        }
    }
}