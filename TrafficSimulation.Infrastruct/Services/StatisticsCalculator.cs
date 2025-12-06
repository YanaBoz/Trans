using System;
using System.Collections.Generic;
using System.Linq;
using TrafficSimulation.Core.Models;
using TrafficSimulation.Core.Services;

namespace TrafficSimulation.Infrastructure.Services
{
    public class StatisticsCalculator : IStatisticsCalculator
    {
        public SimulationMetric CalculateMetrics(SimulationSession session)
        {
            var metric = new SimulationMetric(session.Id, session.CurrentTime)
            {
                VehicleCount = session.Vehicles.Count,
                PedestrianCount = session.Pedestrians.Count,
                ActiveIncidents = session.Incidents?.Count(i => i.IsActive) ?? 0,
                BlockedRoadsCount = session.Network?.Edges.Count(e => e.IsBlocked) ?? 0
            };

            if (session.Vehicles.Any())
            {
                metric.AverageVehicleSpeed = session.CalculateAverageVehicleSpeed();
                metric.TotalDelay = session.CalculateTotalDelay();
                metric.AverageTravelTime = session.Vehicles.Average(v => v.TotalTravelTime);
                metric.VehicleThroughput = session.CompletedVehiclesCount;
            }

            if (session.Pedestrians.Any())
            {
                metric.AveragePedestrianSpeed = session.CalculateAveragePedestrianSpeed();
                metric.PedestrianThroughput = session.CompletedPedestriansCount;
            }

            // Расчет загруженности
            if (session.Network?.Edges != null)
            {
                var congestionSum = session.Network.Edges.Sum(e => e.CongestionLevel);
                var edgeCount = session.Network.Edges.Count;
                metric.CongestionLevel = edgeCount > 0 ? congestionSum / edgeCount : 0;
            }

            metric.AccidentCount = session.Incidents?.Count(i => i.Type == IncidentType.Accident) ?? 0;
            return metric;
        }

        public ComparisonCriteria GetDefaultCriteria()
        {
            return new ComparisonCriteria();
        }

        public List<KeyValuePair<string, double>> CalculatePerformanceIndicators(SimulationSession session)
        {
            var indicators = new List<KeyValuePair<string, double>>();

            if (session.Vehicles.Any())
            {
                double totalDistance = session.Vehicles.Sum(v => v.DistanceTraveled);
                double totalTime = session.CurrentTime / 3600.0;
                double flowEfficiency = totalTime > 0 ? totalDistance / totalTime : 0;

                indicators.Add(new KeyValuePair<string, double>("Эффективность потока", flowEfficiency));

                double totalVehicles = session.Vehicles.Count + session.CompletedVehiclesCount;
                double accidentRate = (session.Incidents?.Count(i => i.Type == IncidentType.Accident) ?? 0) / (totalVehicles > 0 ? totalVehicles : 1);
                double safetyIndex = Math.Max(0, 100 - accidentRate * 10000);

                indicators.Add(new KeyValuePair<string, double>("Индекс безопасности", safetyIndex));
            }

            return indicators;
        }
    }
}