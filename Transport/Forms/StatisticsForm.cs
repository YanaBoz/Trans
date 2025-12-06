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
using TrafficSimulation.Core.Services;
namespace TrafficSimulation.UI.Forms
{
    public partial class StatisticsForm : Form
    {
        private readonly ITrafficSimulationService _simulationService;
        private SimulationSession _currentSession;
        private System.Timers.Timer _updateTimer;

        public StatisticsForm(ITrafficSimulationService simulationService)
        {
            InitializeComponent();
            _simulationService = simulationService;
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.Text = "Статистика симуляции";
            this.Size = new Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterParent;

            var tabControl = new TabControl
            {
                Dock = DockStyle.Fill
            };

            // Вкладка общих метрик
            var generalTab = new TabPage("Общие метрики");
            SetupGeneralTab(generalTab);

            // Вкладка графиков
            var chartsTab = new TabPage("Графики");
            SetupChartsTab(chartsTab);

            // Вкладка детальной статистики
            var detailedTab = new TabPage("Детальная статистика");
            SetupDetailedTab(detailedTab);

            // Вкладка анализа
            var analysisTab = new TabPage("Анализ");
            SetupAnalysisTab(analysisTab);

            tabControl.TabPages.AddRange(new[] { generalTab, chartsTab, detailedTab, analysisTab });
            this.Controls.Add(tabControl);

            // Панель управления
            var controlPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                BackColor = SystemColors.Control
            };

            var btnExport = new Button
            {
                Text = "Экспорт",
                Location = new Point(10, 10),
                Size = new Size(100, 30)
            };
            btnExport.Click += (s, e) => ExportStatistics();

            var btnRefresh = new Button
            {
                Text = "Обновить",
                Location = new Point(120, 10),
                Size = new Size(100, 30)
            };
            btnRefresh.Click += async (s, e) => await RefreshStatistics();

            var btnClose = new Button
            {
                Text = "Закрыть",
                Location = new Point(230, 10),
                Size = new Size(100, 30)
            };
            btnClose.Click += (s, e) => this.Close();

            controlPanel.Controls.AddRange(new Control[] { btnExport, btnRefresh, btnClose });
            this.Controls.Add(controlPanel);

            // Таймер обновления
            _updateTimer = new System.Timers.Timer { Interval = 2000 };
            _updateTimer.Elapsed += async (s, e) =>
            {
                // Используем Invoke для обновления UI из другого потока
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(async () => await RefreshStatistics()));
                }
                else
                {
                    await RefreshStatistics();
                }
            };
        }

        public void SetSession(SimulationSession session)
        {
            _currentSession = session;
            if (session != null && session.State == SimulationState.Running)
            {
                _updateTimer.Start();
            }
            else
            {
                _updateTimer.Stop();
            }
            RefreshStatistics().Wait();
        }

        private void SetupGeneralTab(TabPage tab)
        {
            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical
            };

            // Верхняя панель - ключевые показатели
            var keyMetricsPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            var metricsGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 10,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
            };

            metricsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            metricsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            AddMetricRow(metricsGrid, 0, "Время симуляции", "currentTime");
            AddMetricRow(metricsGrid, 1, "Всего ТС", "totalVehicles");
            AddMetricRow(metricsGrid, 2, "Текущие ТС", "currentVehicles");
            AddMetricRow(metricsGrid, 3, "Завершенные ТС", "completedVehicles");
            AddMetricRow(metricsGrid, 4, "Средняя скорость", "averageSpeed");
            AddMetricRow(metricsGrid, 5, "Загруженность сети", "congestion");
            AddMetricRow(metricsGrid, 6, "ДТП", "accidents");
            AddMetricRow(metricsGrid, 7, "Активные инциденты", "activeIncidents");
            AddMetricRow(metricsGrid, 8, "Пропускная способность", "throughput");
            AddMetricRow(metricsGrid, 9, "Общее время задержки", "totalDelay");

            keyMetricsPanel.Controls.Add(metricsGrid);
            splitContainer.Panel1.Controls.Add(keyMetricsPanel);

            // Нижняя панель - индикаторы
            var indicatorsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };

            CreateIndicator(indicatorsPanel, "Эффективность", Color.Green);
            CreateIndicator(indicatorsPanel, "Безопасность", Color.Blue);
            CreateIndicator(indicatorsPanel, "Загруженность", Color.Orange);
            CreateIndicator(indicatorsPanel, "Стабильность", Color.Purple);

            splitContainer.Panel2.Controls.Add(indicatorsPanel);
            tab.Controls.Add(splitContainer);
        }

        private void SetupChartsTab(TabPage tab)
        {
            var chart = new Chart
            {
                Dock = DockStyle.Fill
            };

            var chartArea = new ChartArea("MainArea");
            chartArea.AxisX.Title = "Время (сек)";
            chartArea.AxisY.Title = "Значение";
            chart.ChartAreas.Add(chartArea);

            // Серии данных
            var speedSeries = new Series("Средняя скорость")
            {
                ChartType = SeriesChartType.Line,
                Color = Color.Blue,
                BorderWidth = 2
            };
            chart.Series.Add(speedSeries);

            var congestionSeries = new Series("Загруженность")
            {
                ChartType = SeriesChartType.Line,
                Color = Color.Red,
                BorderWidth = 2
            };
            chart.Series.Add(congestionSeries);

            var throughputSeries = new Series("Пропускная способность")
            {
                ChartType = SeriesChartType.Line,
                Color = Color.Green,
                BorderWidth = 2
            };
            chart.Series.Add(throughputSeries);

            tab.Controls.Add(chart);
        }

        private void SetupDetailedTab(TabPage tab)
        {
            var dataGridView = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };

            // Колонки для детальной статистики
            dataGridView.Columns.AddRange(new[]
            {
                new DataGridViewTextBoxColumn { HeaderText = "Время", DataPropertyName = "Time" },
                new DataGridViewTextBoxColumn { HeaderText = "ТС", DataPropertyName = "VehicleCount" },
                new DataGridViewTextBoxColumn { HeaderText = "Скорость", DataPropertyName = "AverageSpeed" },
                new DataGridViewTextBoxColumn { HeaderText = "Загруженность", DataPropertyName = "CongestionLevel" },
                new DataGridViewTextBoxColumn { HeaderText = "Пропускная способность", DataPropertyName = "Throughput" },
                new DataGridViewTextBoxColumn { HeaderText = "ДТП", DataPropertyName = "AccidentCount" }
            });

            tab.Controls.Add(dataGridView);
        }

        private void SetupAnalysisTab(TabPage tab)
        {
            var richTextBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Consolas", 10)
            };

            tab.Controls.Add(richTextBox);
        }

        private void AddMetricRow(TableLayoutPanel panel, int row, string label, string key)
        {
            var labelControl = new Label
            {
                Text = label,
                Font = new Font("Arial", 10, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Padding = new Padding(5)
            };

            var valueControl = new Label
            {
                Text = "0",
                Font = new Font("Arial", 10),
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill,
                Padding = new Padding(5),
                Tag = key
            };

            panel.Controls.Add(labelControl, 0, row);
            panel.Controls.Add(valueControl, 1, row);
        }

        private void CreateIndicator(FlowLayoutPanel panel, string title, Color color)
        {
            var indicatorPanel = new Panel
            {
                Size = new Size(150, 120),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(5)
            };

            var titleLabel = new Label
            {
                Text = title,
                Font = new Font("Arial", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 25
            };

            var gauge = new ProgressBar
            {
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100,
                Value = 50,
                ForeColor = color,
                Height = 50,
                Width = 130,
                Location = new Point(10, 35)
            };

            var valueLabel = new Label
            {
                Text = "50%",
                Font = new Font("Arial", 12, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Bottom,
                Height = 30,
                Tag = title
            };

            indicatorPanel.Controls.AddRange(new Control[] { titleLabel, gauge, valueLabel });
            panel.Controls.Add(indicatorPanel);
        }

        private async Task RefreshStatistics()
        {
            if (_currentSession == null) return;

            try
            {
                var metrics = await _simulationService.GetCurrentMetricsAsync(_currentSession.Id);
                UpdateMetricsDisplay(metrics);

                var metricsHistory = await _simulationService.GetMetricsHistoryAsync(_currentSession.Id);
                UpdateCharts(metricsHistory);

                UpdateDetailedGrid(metricsHistory);
                UpdateAnalysis(metricsHistory);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error refreshing statistics: {ex.Message}");
            }
        }

        private void UpdateMetricsDisplay(SimulationMetric metric)
        {
            // Обновление всех меток с метриками
            foreach (Control control in GetAllControls(this))
            {
                if (control is Label label && label.Tag != null)
                {
                    var key = label.Tag.ToString();
                    var value = GetMetricValue(metric, key);
                    label.Text = value;
                }
            }
        }

        private string GetMetricValue(SimulationMetric metric, string key)
        {
            return key switch
            {
                "currentTime" => $"{_currentSession.CurrentTime} сек",
                "totalVehicles" => $"{_currentSession.Vehicles.Count + _currentSession.CompletedVehiclesCount}",
                "currentVehicles" => $"{_currentSession.Vehicles.Count}",
                "completedVehicles" => $"{_currentSession.CompletedVehiclesCount}",
                "averageSpeed" => $"{metric.AverageVehicleSpeed:F1} км/ч",
                "congestion" => $"{metric.CongestionLevel * 100:F1}%",
                "accidents" => $"{metric.AccidentCount}",
                "activeIncidents" => $"{metric.ActiveIncidents}",
                "throughput" => $"{metric.VehicleThroughput} ТС/час",
                "totalDelay" => $"{metric.TotalDelay:F1} сек",
                _ => "N/A"
            };
        }

        private void UpdateCharts(IEnumerable<SimulationMetric> metricsHistory)
        {
            // Обновление графиков
            var chart = FindControl<Chart>(this);
            if (chart != null && chart.Series.Count >= 3)
            {
                chart.Series[0].Points.Clear();
                chart.Series[1].Points.Clear();
                chart.Series[2].Points.Clear();

                foreach (var metric in metricsHistory)
                {
                    chart.Series[0].Points.AddXY(metric.Timestamp, metric.AverageVehicleSpeed);
                    chart.Series[1].Points.AddXY(metric.Timestamp, metric.CongestionLevel * 100);
                    chart.Series[2].Points.AddXY(metric.Timestamp, metric.VehicleThroughput);
                }
            }
        }

        private void UpdateDetailedGrid(IEnumerable<SimulationMetric> metricsHistory)
        {
            var grid = FindControl<DataGridView>(this);
            if (grid != null)
            {
                grid.DataSource = metricsHistory.ToList();
            }
        }

        private void UpdateAnalysis(IEnumerable<SimulationMetric> metricsHistory)
        {
            var textBox = FindControl<RichTextBox>(this);
            if (textBox != null && metricsHistory.Any())
            {
                var lastMetric = metricsHistory.Last();
                var averageSpeed = metricsHistory.Average(m => m.AverageVehicleSpeed);
                var maxSpeed = metricsHistory.Max(m => m.AverageVehicleSpeed);
                var minSpeed = metricsHistory.Min(m => m.AverageVehicleSpeed);

                var analysis = new System.Text.StringBuilder();
                analysis.AppendLine("=== АНАЛИЗ СТАТИСТИКИ ===");
                analysis.AppendLine();
                analysis.AppendLine($"Средняя скорость за период: {averageSpeed:F1} км/ч");
                analysis.AppendLine($"Максимальная скорость: {maxSpeed:F1} км/ч");
                analysis.AppendLine($"Минимальная скорость: {minSpeed:F1} км/ч");
                analysis.AppendLine();

                if (lastMetric.CongestionLevel > 0.7)
                    analysis.AppendLine("⚠️ Высокий уровень загруженности!");
                else if (lastMetric.CongestionLevel > 0.4)
                    analysis.AppendLine("ℹ️ Средний уровень загруженности");
                else
                    analysis.AppendLine("✓ Низкий уровень загруженности");

                if (lastMetric.AccidentCount > 5)
                    analysis.AppendLine("⚠️ Высокий уровень аварийности!");
                else if (lastMetric.AccidentCount > 0)
                    analysis.AppendLine("ℹ️ Умеренный уровень аварийности");
                else
                    analysis.AppendLine("✓ Отсутствие ДТП");

                textBox.Text = analysis.ToString();
            }
        }

        private void ExportStatistics()
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = $"statistics_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (saveDialog.ShowDialog() == DialogResult.OK)
            {
                // Экспорт статистики в файл
                // Реализация зависит от формата
            }
        }

        private T FindControl<T>(Control control) where T : Control
        {
            foreach (Control child in control.Controls)
            {
                if (child is T found)
                    return found;
                var result = FindControl<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private IEnumerable<Control> GetAllControls(Control control)
        {
            var controls = new List<Control> { control };
            foreach (Control child in control.Controls)
            {
                controls.AddRange(GetAllControls(child));
            }
            return controls;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _updateTimer?.Stop();
            _updateTimer?.Dispose();
            base.OnFormClosing(e);
        }
    }
}