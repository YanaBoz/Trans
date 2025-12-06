using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows.Forms;
using TrafficSimulation.Application.Services;
using TrafficSimulation.Core.Repositories;
using TrafficSimulation.Core.Services;
using TrafficSimulation.Infrastructure.Data;
using TrafficSimulation.Infrastructure.Services;

namespace TrafficSimulation.UI
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Используем полное имя с указанием пространства имен
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            // Настройка DI контейнера
            var services = new ServiceCollection();
            ConfigureServices(services);
            var serviceProvider = services.BuildServiceProvider();

            // Запуск главной формы
            var mainForm = serviceProvider.GetRequiredService<Forms.MainForm>();
            System.Windows.Forms.Application.Run(mainForm);
        }

        private static void ConfigureServices(ServiceCollection services)
        {
            // Регистрация репозиториев
            services.AddSingleton<IRoadNetworkRepository, JsonRoadNetworkRepository>();
            services.AddSingleton<ISimulationRepository, JsonSimulationRepository>();
            services.AddSingleton<IParametersRepository, JsonParametersRepository>();

            // Регистрация сервисов
            services.AddSingleton<ITrafficSimulationService, TrafficSimulationService>();
            services.AddSingleton<IGraphEditorService, GraphEditorService>();
            services.AddSingleton<IComparisonService, ComparisonService>();

            // Используем Infrastructure версию StatisticsCalculator
            services.AddSingleton<IStatisticsCalculator, Infrastructure.Services.StatisticsCalculator>();

            // Регистрация форм
            services.AddTransient<Forms.MainForm>();
            services.AddTransient<Forms.ComparisonForm>();
            services.AddTransient<Forms.ParametersForm>();
            services.AddTransient<Forms.StatisticsForm>();
            services.AddTransient<Forms.IncidentForm>();
            services.AddTransient<Forms.GraphEditorForm>();
        }
    }
}