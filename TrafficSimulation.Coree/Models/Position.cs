namespace TrafficSimulation.Core.Models
{
    public class Position
    {
        public Guid EdgeId { get; set; }
        public double Offset { get; set; } // в метрах от начала ребра
    }
}