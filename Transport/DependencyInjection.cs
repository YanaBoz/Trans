using TrafficSimulation.UI.Forms;
using TrafficSimulation.Core.Repositories;
using TrafficSimulation.Core.Services;
using TrafficSimulation.Application.Services;
using TrafficSimulation.Infrastructure.Data;
using TrafficSimulation.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace TrafficSimulation.UI
{
    public static class DependencyInjection
    {
        public static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Регистрация репозиториев
            services.AddSingleton<IRoadNetworkRepository, JsonRoadNetworkRepository>();
            services.AddSingleton<ISimulationRepository, JsonSimulationRepository>();
            services.AddSingleton<IParametersRepository, JsonParametersRepository>();

            // Регистрация сервисов
            services.AddSingleton<ITrafficSimulationService, TrafficSimulationService>();
            services.AddSingleton<IGraphEditorService, GraphEditorService>();
            services.AddSingleton<IComparisonService, ComparisonService>();
            services.AddSingleton<IStatisticsCalculator, Infrastructure.Services.StatisticsCalculator>();

            // Регистрация форм
            services.AddTransient<MainForm>();
            services.AddTransient<ComparisonForm>();
            services.AddTransient<ParametersForm>();
            services.AddTransient<GraphEditorForm>();

            return services.BuildServiceProvider();
        }
    }
}