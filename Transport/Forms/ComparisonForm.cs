using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using TrafficSimulation.Core.Models;
using TrafficSimulation.Core.Repositories;
using TrafficSimulation.Core.Services;

namespace TrafficSimulation.UI.Forms
{
    public partial class ComparisonForm : Form
    {
        private readonly IComparisonService _comparisonService;
        private readonly ISimulationRepository _simulationRepository;

        private List<SimulationSession> _selectedSessions = new();
        private ComparisonCriteria _criteria = new();

        public ComparisonForm(
            IComparisonService comparisonService,
            ISimulationRepository simulationRepository)
        {
            InitializeComponent();
            _comparisonService = comparisonService;
            _simulationRepository = simulationRepository;

            SetupUI();
            LoadSessions();
        }

        private void SetupUI()
        {
            Text = "Сравнение симуляций";
            Size = new Size(1000, 700);

            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 300
            };

            // Верхняя панель - список сессий и критерии
            var topPanel = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical
            };

            // Список сессий
            var sessionsList = new CheckedListBox
            {
                Dock = DockStyle.Fill
            };
            sessionsList.ItemCheck += OnSessionChecked; // Изменено: передаем метод напрямую
            topPanel.Panel1.Controls.Add(sessionsList);

            // Критерии сравнения
            var criteriaPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            SetupCriteriaControls(criteriaPanel);
            topPanel.Panel2.Controls.Add(criteriaPanel);

            splitContainer.Panel1.Controls.Add(topPanel);

            // Нижняя панель - результаты сравнения
            var resultsTabControl = new TabControl
            {
                Dock = DockStyle.Fill
            };

            // Таблица сравнения
            var tableTab = new TabPage("Таблица");
            var dataGridView = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            tableTab.Controls.Add(dataGridView);

            // Графики
            var chartsTab = new TabPage("Графики");
            var chartControl = new System.Windows.Forms.DataVisualization.Charting.Chart
            {
                Dock = DockStyle.Fill
            };
            chartsTab.Controls.Add(chartControl);

            // Рекомендации
            var recommendationsTab = new TabPage("Рекомендации");
            var recommendationsBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true
            };
            recommendationsTab.Controls.Add(recommendationsBox);

            resultsTabControl.TabPages.AddRange(new[] { tableTab, chartsTab, recommendationsTab });
            splitContainer.Panel2.Controls.Add(resultsTabControl);

            Controls.Add(splitContainer);

            // Кнопка сравнения
            var compareButton = new Button
            {
                Text = "Сравнить",
                Dock = DockStyle.Bottom,
                Height = 40
            };
            compareButton.Click += async (s, e) => await CompareSessions();
            Controls.Add(compareButton);
        }

        private void SetupCriteriaControls(Panel panel)
        {
            var y = 10;

            var lblTitle = new Label
            {
                Text = "Критерии сравнения:",
                Location = new Point(10, y),
                Font = new Font("Arial", 10, FontStyle.Bold)
            };
            panel.Controls.Add(lblTitle);
            y += 30;

            // Средняя скорость
            AddCheckBox(panel, ref y, "Средняя скорость транспортных средств",
                () => _criteria.IncludeAverageSpeed,
                v => _criteria.IncludeAverageSpeed = v);

            // Пропускная способность
            AddCheckBox(panel, ref y, "Пропускная способность",
                () => _criteria.IncludeThroughput,
                v => _criteria.IncludeThroughput = v);

            // Уровень загруженности
            AddCheckBox(panel, ref y, "Уровень загруженности сети",
                () => _criteria.IncludeCongestionLevel,
                v => _criteria.IncludeCongestionLevel = v);

            // Количество ДТП
            AddCheckBox(panel, ref y, "Количество ДТП",
                () => _criteria.IncludeAccidents,
                v => _criteria.IncludeAccidents = v);

            // Общее время задержки
            AddCheckBox(panel, ref y, "Общее время задержки",
                () => _criteria.IncludeTotalDelay,
                v => _criteria.IncludeTotalDelay = v);

            // Эффективность светофоров
            AddCheckBox(panel, ref y, "Эффективность светофоров",
                () => _criteria.IncludeTrafficLightEfficiency,
                v => _criteria.IncludeTrafficLightEfficiency = v);
        }

        private void AddCheckBox(Panel panel, ref int y, string text,
            Func<bool> getter, Action<bool> setter)
        {
            var checkBox = new CheckBox
            {
                Text = text,
                Location = new Point(20, y),
                Checked = getter(),
                Width = 250
            };
            checkBox.CheckedChanged += (s, e) => setter(checkBox.Checked);
            panel.Controls.Add(checkBox);
            y += 25;
        }

        private async void LoadSessions()
        {
            try
            {
                var sessions = await _simulationRepository.GetAllSessionsAsync();
                var sessionsList = Controls.Find("", true).OfType<CheckedListBox>().FirstOrDefault();
                if (sessionsList != null)
                {
                    sessionsList.Items.Clear();
                    foreach (var session in sessions)
                    {
                        sessionsList.Items.Add(session, false);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки сессий: {ex.Message}");
            }
        }

        // ИЗМЕНЕНИЕ ЗДЕСЬ: добавлен параметр sender
        private void OnSessionChecked(object sender, ItemCheckEventArgs e)
        {
            var sessionsList = (CheckedListBox)sender;
            var session = (SimulationSession)sessionsList.Items[e.Index];

            if (e.NewValue == CheckState.Checked)
            {
                _selectedSessions.Add(session);
            }
            else
            {
                _selectedSessions.Remove(session);
            }
        }

        private async Task CompareSessions()
        {
            if (_selectedSessions.Count < 2)
            {
                MessageBox.Show("Выберите хотя бы 2 сессии для сравнения");
                return;
            }

            try
            {
                var sessionIds = _selectedSessions.Select(s => s.Id).ToList();
                var comparison = await _comparisonService.CompareSessionsAsync(sessionIds, _criteria);
                DisplayComparisonResults(comparison);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сравнения: {ex.Message}");
            }
        }

        private void DisplayComparisonResults(SimulationComparison comparison)
        {
            // Отображение результатов в таблице
            var dataGridView = Controls.Find("", true).OfType<DataGridView>().FirstOrDefault();
            if (dataGridView != null)
            {
                dataGridView.DataSource = comparison.Results;
            }

            // Генерация рекомендаций
            var recommendationsBox = Controls.Find("", true).OfType<RichTextBox>().FirstOrDefault();
            if (recommendationsBox != null)
            {
                recommendationsBox.Text = GenerateRecommendations(comparison);
            }
        }

        private string GenerateRecommendations(SimulationComparison comparison)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== РЕКОМЕНДАЦИИ ПО ОПТИМИЗАЦИИ ===");
            sb.AppendLine();

            // Анализ лучшей конфигурации
            var bestResult = comparison.Results.OrderByDescending(r => r.Score).FirstOrDefault();

            if (bestResult != null)
            {
                sb.AppendLine($"Лучшая конфигурация: {bestResult.SessionName}");
                sb.AppendLine($"Общий балл: {bestResult.Score:F2}");
                sb.AppendLine();

                // Рекомендации на основе параметров лучшей конфигурации
                var parameters = bestResult.Parameters;
                sb.AppendLine("Рекомендуемые параметры:");
                sb.AppendLine($"- Интенсивность ТС: {parameters.VehicleIntensity}");
                sb.AppendLine($"- Длительность блокировки: {parameters.BlockDurationSeconds} сек");
                sb.AppendLine($"- Порог загруженности: {parameters.CongestionThreshold}");

                if (bestResult.AverageVehicleSpeed > comparison.Results.Average(r => r.AverageVehicleSpeed))
                    sb.AppendLine("✓ Хорошая средняя скорость движения");

                if (bestResult.AccidentCount < comparison.Results.Average(r => r.AccidentCount))
                    sb.AppendLine("✓ Низкий уровень аварийности");
            }

            return sb.ToString();
        }
    }
}