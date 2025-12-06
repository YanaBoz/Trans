using TrafficSimulation.Core.Models;
using TrafficSimulation.Core.Repositories;
using TrafficSimulation.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TrafficSimulation.Infrastructure.Services
{
    public class ComparisonService : IComparisonService
    {
        private readonly ISimulationRepository _simulationRepository;
        private readonly IRoadNetworkRepository _networkRepository;

        public ComparisonService(
            ISimulationRepository simulationRepository,
            IRoadNetworkRepository networkRepository)
        {
            _simulationRepository = simulationRepository;
            _networkRepository = networkRepository;
        }

        public async Task<SimulationComparison> CompareSessionsAsync(
            IEnumerable<Guid> sessionIds,
            ComparisonCriteria criteria)
        {
            if (sessionIds == null || !sessionIds.Any())
                throw new ArgumentException("At least one session ID is required");

            var sessions = new List<SimulationSession>();
            foreach (var sessionId in sessionIds)
            {
                var session = await _simulationRepository.GetSessionAsync(sessionId);
                if (session != null)
                    sessions.Add(session);
            }

            if (sessions.Count < 2)
                throw new InvalidOperationException("At least two sessions are required for comparison");

            var comparison = new SimulationComparison
            {
                ComparisonDate = DateTime.Now,
                Criteria = criteria ?? new ComparisonCriteria(),
                Results = new List<ComparisonResult>()
            };

            foreach (var session in sessions)
            {
                var result = await CalculateComparisonResult(session, comparison.Criteria);
                comparison.Results.Add(result);
            }

            // Сортировка по общему баллу
            comparison.Results = comparison.Results
                .OrderByDescending(r => r.Score)
                .ToList();

            comparison.Summary = GenerateSummary(comparison);

            return comparison;
        }

        private async Task<ComparisonResult> CalculateComparisonResult(
            SimulationSession session,
            ComparisonCriteria criteria)
        {
            var result = new ComparisonResult
            {
                SessionId = session.Id,
                SessionName = session.Name,
                Parameters = session.Parameters,
                TotalVehicles = session.Vehicles.Count + session.CompletedVehiclesCount,
                TotalPedestrians = session.Pedestrians?.Count + session.CompletedPedestriansCount ?? 0
            };

            // Расчет метрик
            var metrics = session.Metrics;
            if (metrics.Any())
            {
                result.AverageVehicleSpeed = metrics.Average(m => m.AverageVehicleSpeed);
                result.AveragePedestrianSpeed = metrics.Average(m => m.AveragePedestrianSpeed);
                result.AverageCongestionLevel = metrics.Average(m => m.CongestionLevel);
                result.TotalDelay = metrics.LastOrDefault()?.TotalDelay ?? 0;
                result.VehicleThroughput = metrics.LastOrDefault()?.VehicleThroughput ?? 0;
                result.PedestrianThroughput = metrics.LastOrDefault()?.PedestrianThroughput ?? 0;
                result.AccidentCount = session.Incidents?.Count(i => i.Type == IncidentType.Accident) ?? 0;
                result.BlockedRoadsTime = CalculateTotalBlockedTime(session);
            }

            // Расчет общего балла
            result.Score = CalculateScore(result, criteria);

            return result;
        }

        private double CalculateScore(ComparisonResult result, ComparisonCriteria criteria)
        {
            double score = 0;
            double totalWeight = 0;

            if (criteria.IncludeAverageSpeed && criteria.Weights.ContainsKey("AverageSpeed"))
            {
                var weight = criteria.Weights["AverageSpeed"];
                // Нормализуем скорость (предполагаем, что 0-120 км/ч)
                var normalizedSpeed = Math.Min(result.AverageVehicleSpeed / 120.0, 1.0);
                score += normalizedSpeed * weight;
                totalWeight += weight;
            }

            if (criteria.IncludeThroughput && criteria.Weights.ContainsKey("Throughput"))
            {
                var weight = criteria.Weights["Throughput"];
                // Нормализуем пропускную способность (предполагаем, что 0-1000 ТС)
                var normalizedThroughput = Math.Min(result.VehicleThroughput / 1000.0, 1.0);
                score += normalizedThroughput * weight;
                totalWeight += weight;
            }

            if (criteria.IncludeCongestionLevel && criteria.Weights.ContainsKey("CongestionLevel"))
            {
                var weight = criteria.Weights["CongestionLevel"];
                // Меньшая загруженность - лучше
                var normalizedCongestion = 1.0 - Math.Min(result.AverageCongestionLevel, 1.0);
                score += normalizedCongestion * weight;
                totalWeight += weight;
            }

            if (criteria.IncludeAccidents && criteria.Weights.ContainsKey("Accidents"))
            {
                var weight = criteria.Weights["Accidents"];
                // Меньше аварий - лучше
                var accidentScore = result.AccidentCount == 0 ? 1.0 : 1.0 / (1 + result.AccidentCount);
                score += accidentScore * weight;
                totalWeight += weight;
            }

            if (criteria.IncludeTotalDelay && criteria.Weights.ContainsKey("TotalDelay"))
            {
                var weight = criteria.Weights["TotalDelay"];
                // Меньше задержек - лучше
                var delayScore = result.TotalDelay == 0 ? 1.0 : 1.0 / (1 + result.TotalDelay);
                score += delayScore * weight;
                totalWeight += weight;
            }

            // Если включена эффективность светофоров, но нет веса, добавляем с весом 0.1
            if (criteria.IncludeTrafficLightEfficiency && !criteria.Weights.ContainsKey("TrafficLightEfficiency"))
            {
                criteria.Weights["TrafficLightEfficiency"] = 0.1;
            }

            if (criteria.IncludeTrafficLightEfficiency && criteria.Weights.ContainsKey("TrafficLightEfficiency"))
            {
                var weight = criteria.Weights["TrafficLightEfficiency"];
                // Оценка эффективности светофоров (упрощенная)
                var trafficLightScore = CalculateTrafficLightEfficiency(result);
                score += trafficLightScore * weight;
                totalWeight += weight;
            }

            // Нормализуем оценку на основе использованных весов
            if (totalWeight > 0)
            {
                score = (score / totalWeight) * 100;
            }

            return Math.Round(score, 2);
        }

        private double CalculateTrafficLightEfficiency(ComparisonResult result)
        {
            // Упрощенная оценка эффективности светофоров
            // Чем выше средняя скорость и меньше задержек, тем лучше работают светофоры
            var speedFactor = Math.Min(result.AverageVehicleSpeed / 60.0, 1.0);
            var delayFactor = result.TotalDelay == 0 ? 1.0 : Math.Max(0, 1.0 - (result.TotalDelay / 100.0));

            return (speedFactor + delayFactor) / 2.0;
        }

        private double CalculateTotalBlockedTime(SimulationSession session)
        {
            double totalBlockedTime = 0;
            if (session.Incidents != null)
            {
                foreach (var incident in session.Incidents.Where(i => i.Type == IncidentType.Accident))
                {
                    // Упрощенный расчет - предполагаем блокировку на 5 минут
                    totalBlockedTime += 300; // 5 минут в секундах
                }
            }
            return totalBlockedTime;
        }

        private string GenerateSummary(SimulationComparison comparison)
        {
            if (!comparison.Results.Any())
                return "Нет данных для сравнения";

            var best = comparison.Results.First();
            var worst = comparison.Results.Last();
            var averageScore = comparison.Results.Average(r => r.Score);

            var sb = new StringBuilder();
            sb.AppendLine($"=== РЕЗУЛЬТАТЫ СРАВНЕНИЯ ({comparison.Results.Count} конфигураций) ===");
            sb.AppendLine($"Дата сравнения: {comparison.ComparisonDate:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Средний балл: {averageScore:F2}");
            sb.AppendLine();
            sb.AppendLine($"ЛУЧШАЯ КОНФИГУРАЦИЯ: {best.SessionName}");
            sb.AppendLine($"Общий балл: {best.Score:F2}");
            sb.AppendLine($"Средняя скорость: {best.AverageVehicleSpeed:F1} км/ч");
            sb.AppendLine($"Пропускная способность: {best.VehicleThroughput} ТС");
            sb.AppendLine($"ДТП: {best.AccidentCount}");
            sb.AppendLine($"Загруженность: {best.AverageCongestionLevel * 100:F1}%");
            sb.AppendLine();
            sb.AppendLine($"ХУДШАЯ КОНФИГУРАЦИЯ: {worst.SessionName}");
            sb.AppendLine($"Общий балл: {worst.Score:F2}");
            sb.AppendLine($"Средняя скорость: {worst.AverageVehicleSpeed:F1} км/ч");
            sb.AppendLine($"Пропускная способность: {worst.VehicleThroughput} ТС");
            sb.AppendLine($"ДТП: {worst.AccidentCount}");
            sb.AppendLine($"Загруженность: {worst.AverageCongestionLevel * 100:F1}%");

            // Анализ различий
            if (comparison.Results.Count >= 2)
            {
                sb.AppendLine();
                sb.AppendLine("=== АНАЛИЗ РАЗЛИЧИЙ ===");

                var maxSpeed = comparison.Results.Max(r => r.AverageVehicleSpeed);
                var minSpeed = comparison.Results.Min(r => r.AverageVehicleSpeed);
                var speedDifference = maxSpeed - minSpeed;

                var maxThroughput = comparison.Results.Max(r => r.VehicleThroughput);
                var minThroughput = comparison.Results.Min(r => r.VehicleThroughput);
                var throughputDifference = maxThroughput - minThroughput;

                sb.AppendLine($"Разница в скорости: {speedDifference:F1} км/ч");
                sb.AppendLine($"Разница в пропускной способности: {throughputDifference} ТС");

                // Рекомендации
                sb.AppendLine();
                sb.AppendLine("=== РЕКОМЕНДАЦИИ ===");

                if (speedDifference > 20)
                    sb.AppendLine("✓ Рекомендуется оптимизировать светофоры для повышения скорости");

                if (throughputDifference > 50)
                    sb.AppendLine("✓ Рекомендуется увеличить количество полос на загруженных участках");

                var bestAccidents = comparison.Results.Min(r => r.AccidentCount);
                if (bestAccidents == 0)
                    sb.AppendLine("✓ Конфигурация без ДТП достигнута - отличный результат!");
                else
                    sb.AppendLine($"✓ Минимальное количество ДТП: {bestAccidents}");
            }

            return sb.ToString();
        }

        public async Task<OptimizationResult> FindOptimalConfigurationAsync(
            IEnumerable<SimulationParameters> candidates)
        {
            if (candidates == null || !candidates.Any())
                throw new ArgumentException("At least one parameter set is required");

            var optimizationResult = new OptimizationResult();
            var bestParameters = candidates.First();
            var bestScore = 0.0;

            foreach (var parameters in candidates)
            {
                var score = EvaluateParameters(parameters);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestParameters = parameters;
                }
            }

            optimizationResult.OptimalParameters = bestParameters;
            optimizationResult.Score = bestScore;
            optimizationResult.Recommendations = GenerateRecommendations(bestParameters);

            return optimizationResult;
        }

        private double EvaluateParameters(SimulationParameters parameters)
        {
            double score = 0;

            // Более низкая вероятность аварий - лучше
            score += (1 - parameters.AccidentProbabilityFactor * 1000);

            // Выше порог загруженности - лучше (но не слишком высокий)
            if (parameters.CongestionThreshold >= 0.6 && parameters.CongestionThreshold <= 0.8)
                score += 20;
            else
                score += 10 * parameters.CongestionThreshold;

            // Умеренная интенсивность трафика (1.0-1.5 оптимально)
            var intensityDiff = Math.Abs(parameters.VehicleIntensity - 1.25);
            score += 10 / (1 + intensityDiff);

            // Меньшая длительность блокировки - лучше
            score += 100 / (1 + parameters.BlockDurationSeconds / 60.0);

            // Больше начальных ТС - выше пропускная способность, но может быть хуже
            score += Math.Min(parameters.InitialVehicles / 10.0, 10);

            return score;
        }

        private List<string> GenerateRecommendations(SimulationParameters parameters)
        {
            var recommendations = new List<string>();

            if (parameters.AccidentProbabilityFactor > 0.001)
                recommendations.Add("Рекомендуется снизить вероятность аварий до 0.001");
            else if (parameters.AccidentProbabilityFactor < 0.0001)
                recommendations.Add("Можно немного увеличить интенсивность трафика при такой низкой вероятности аварий");

            if (parameters.VehicleIntensity > 1.5)
                recommendations.Add("Рекомендуется снизить интенсивность трафика до 1.0-1.5 для лучшего баланса");
            else if (parameters.VehicleIntensity < 0.5)
                recommendations.Add("Можно увеличить интенсивность трафика до 0.8-1.2 для более реалистичной симуляции");

            if (parameters.CongestionThreshold < 0.5)
                recommendations.Add("Рекомендуется повысить порог загруженности до 0.6-0.7");
            else if (parameters.CongestionThreshold > 0.9)
                recommendations.Add("Порог загруженности очень высокий, возможно, стоит снизить до 0.7-0.8");

            if (parameters.BlockDurationSeconds > 600)
                recommendations.Add("Длительность блокировки слишком высокая, рекомендуется снизить до 300-400 секунд");

            if (parameters.InitialVehicles < 10)
                recommendations.Add("Можно увеличить начальное количество ТС до 15-20 для более быстрого старта симуляции");

            if (!recommendations.Any())
                recommendations.Add("Параметры выглядят оптимальными. Можно провести дополнительные тесты с разной интенсивностью.");

            return recommendations;
        }

        public async Task<ComparisonReport> GenerateDetailedReportAsync(
            IEnumerable<Guid> sessionIds,
            ComparisonCriteria criteria = null)
        {
            var comparison = await CompareSessionsAsync(sessionIds, criteria ?? new ComparisonCriteria());

            var report = new ComparisonReport
            {
                GenerationDate = DateTime.Now,
                Criteria = comparison.Criteria,
                Summary = comparison.Summary,
                Sessions = new List<SessionComparisonData>(),
                Recommendations = new List<string>(),
                ChartsData = new Dictionary<string, List<ChartDataPoint>>()
            };

            // Заполняем данные сессий
            foreach (var result in comparison.Results)
            {
                var sessionData = new SessionComparisonData
                {
                    SessionId = result.SessionId,
                    Name = result.SessionName,
                    Parameters = result.Parameters,
                    Score = result.Score,
                    Metrics = new Dictionary<string, double>
                    {
                        ["AverageSpeed"] = result.AverageVehicleSpeed,
                        ["Throughput"] = result.VehicleThroughput,
                        ["CongestionLevel"] = result.AverageCongestionLevel,
                        ["Accidents"] = result.AccidentCount,
                        ["TotalDelay"] = result.TotalDelay
                    }
                };

                // Определяем категорию производительности
                sessionData.PerformanceCategory = result.Score switch
                {
                    >= 80 => PerformanceCategory.Excellent,
                    >= 60 => PerformanceCategory.Good,
                    >= 40 => PerformanceCategory.Average,
                    >= 20 => PerformanceCategory.Poor,
                    _ => PerformanceCategory.Critical
                };

                // Определяем сильные и слабые стороны
                if (result.AverageVehicleSpeed > 50)
                    sessionData.Strengths.Add("Высокая средняя скорость");
                if (result.AccidentCount == 0)
                    sessionData.Strengths.Add("Отсутствие ДТП");
                if (result.AverageCongestionLevel < 0.3)
                    sessionData.Strengths.Add("Низкий уровень загруженности");

                if (result.AverageVehicleSpeed < 20)
                    sessionData.Weaknesses.Add("Низкая средняя скорость");
                if (result.AccidentCount > 5)
                    sessionData.Weaknesses.Add("Высокий уровень аварийности");
                if (result.AverageCongestionLevel > 0.7)
                    sessionData.Weaknesses.Add("Высокий уровень загруженности");

                report.Sessions.Add(sessionData);
            }

            // Генерируем рекомендации
            report.Recommendations = GenerateReportRecommendations(report.Sessions);

            // Генерируем данные для графиков
            report.ChartsData = GenerateChartData(report.Sessions);

            return report;
        }

        private List<string> GenerateReportRecommendations(List<SessionComparisonData> sessions)
        {
            var recommendations = new List<string>();

            if (!sessions.Any())
                return recommendations;

            // Анализируем лучшую сессию
            var bestSession = sessions.OrderByDescending(s => s.Score).First();

            recommendations.Add($"Рекомендуется использовать параметры сессии '{bestSession.Name}' как базовые");

            // Анализ параметров лучшей сессии
            if (bestSession.Parameters.VehicleIntensity < 1.0)
                recommendations.Add("Рассмотрите возможность увеличения интенсивности трафика для более реалистичной симуляции");

            if (bestSession.Parameters.AccidentProbabilityFactor < 0.0005)
                recommendations.Add("Можно немного увеличить вероятность аварий для более реалистичной модели");

            if (bestSession.Parameters.InitialVehicles < 20)
                recommendations.Add("Для более быстрого достижения стабильного состояния увеличьте начальное количество ТС");

            return recommendations;
        }

        private Dictionary<string, List<ChartDataPoint>> GenerateChartData(List<SessionComparisonData> sessions)
        {
            var chartData = new Dictionary<string, List<ChartDataPoint>>();

            // Данные для графика скорости
            var speedData = sessions.Select(s => new ChartDataPoint
            {
                Label = s.Name,
                Value = s.Metrics["AverageSpeed"],
                Color = GetPerformanceColor(s.Score)
            }).ToList();
            chartData["Speed"] = speedData;

            // Данные для графика пропускной способности
            var throughputData = sessions.Select(s => new ChartDataPoint
            {
                Label = s.Name,
                Value = s.Metrics["Throughput"],
                Color = GetPerformanceColor(s.Score)
            }).ToList();
            chartData["Throughput"] = throughputData;

            // Данные для графика загруженности
            var congestionData = sessions.Select(s => new ChartDataPoint
            {
                Label = s.Name,
                Value = s.Metrics["CongestionLevel"] * 100,
                Color = GetPerformanceColor(s.Score)
            }).ToList();
            chartData["Congestion"] = congestionData;

            return chartData;
        }

        private string GetPerformanceColor(double score)
        {
            return score switch
            {
                >= 80 => "#4CAF50", // Зеленый
                >= 60 => "#8BC34A", // Светло-зеленый
                >= 40 => "#FFC107", // Желтый
                >= 20 => "#FF9800", // Оранжевый
                _ => "#F44336"      // Красный
            };
        }
    }

    public class ComparisonReport
    {
        public DateTime GenerationDate { get; set; }
        public ComparisonCriteria Criteria { get; set; }
        public List<SessionComparisonData> Sessions { get; set; }
        public string Summary { get; set; }
        public List<string> Recommendations { get; set; }
        public Dictionary<string, List<ChartDataPoint>> ChartsData { get; set; }
    }

    public class SessionComparisonData
    {
        public Guid SessionId { get; set; }
        public string Name { get; set; }
        public SimulationParameters Parameters { get; set; }
        public double Score { get; set; }
        public PerformanceCategory PerformanceCategory { get; set; }
        public Dictionary<string, double> Metrics { get; set; } = new();
        public List<string> Strengths { get; set; } = new();
        public List<string> Weaknesses { get; set; } = new();
    }

    public class ChartDataPoint
    {
        public string Label { get; set; }
        public double Value { get; set; }
        public string Color { get; set; }
    }

    public enum PerformanceCategory { Excellent, Good, Average, Poor, Critical }
}