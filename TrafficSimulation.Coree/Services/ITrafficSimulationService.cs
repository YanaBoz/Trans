using TrafficSimulation.Core.Events;
using TrafficSimulation.Core.Models;

namespace TrafficSimulation.Core.Services
{
    public interface ITrafficSimulationService
    {
        event EventHandler<SimulationStepEventArgs> SimulationStepCompleted;
        event EventHandler<SimulationIncidentEventArgs> IncidentOccurred;
        event EventHandler<SimulationStateChangedEventArgs> StateChanged;

        Task<SimulationSession> CreateSessionAsync(
            string name,
            Guid networkId,
            SimulationParameters parameters);

        Task<bool> StartSimulationAsync(Guid sessionId);
        Task<bool> PauseSimulationAsync(Guid sessionId);
        Task<bool> ResumeSimulationAsync(Guid sessionId);
        Task<bool> StopSimulationAsync(Guid sessionId);
        Task<SimulationStepResult> SimulateStepAsync(Guid sessionId);

        Task<SimulationMetric> GetCurrentMetricsAsync(Guid sessionId);
        Task<IEnumerable<SimulationMetric>> GetMetricsHistoryAsync(Guid sessionId);
        Task<IEnumerable<TrafficIncident>> GetIncidentsAsync(Guid sessionId);

        Task<bool> SaveSessionAsync(Guid sessionId);
        Task<bool> LoadSessionAsync(Guid sessionId);

        Task<Vehicle> AddVehicleAsync(Guid sessionId, VehicleType type, DriverStyle style);
        Task<Pedestrian> AddPedestrianAsync(Guid sessionId, PedestrianType type);

        Task<double> CalculateEdgeDensityAsync(Guid sessionId, Guid edgeId);
        Task<double> CalculateNetworkCongestionAsync(Guid sessionId);

        Task<IEnumerable<RoadSegment>> FindOptimalRouteAsync(
            Guid sessionId,
            Guid startVertexId,
            Guid endVertexId,
            VehicleType vehicleType);
    }

    public class SimulationStateChangedEventArgs : EventArgs
    {
        public Guid SessionId { get; set; }
        public SimulationState PreviousState { get; set; }
        public SimulationState NewState { get; set; }
        public DateTime ChangeTime { get; set; }
    }
}