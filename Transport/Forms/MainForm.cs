using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using Newtonsoft.Json;
using TrafficSimulation.Core.Models;
using TrafficSimulation.Core.Services;

namespace TrafficSimulation.UI.Forms
{
    public partial class MainForm : Form
    {
        private readonly ITrafficSimulationService _simulationService;
        private readonly IGraphEditorService _graphEditorService;
        private readonly IComparisonService _comparisonService;

        private RoadNetwork _currentNetwork;
        private SimulationSession _currentSession;
        private Bitmap _networkBitmap;
        private Graphics _graphics;
        private System.Windows.Forms.Timer _renderTimer;

        // Цвета для визуализации
        private readonly Dictionary<RoadSegment, Color> _roadColors = new();
        private readonly Dictionary<Vehicle, Color> _vehicleColors = new();
        private readonly Pen[] _trafficLightPens = new[]
        {
            new Pen(Color.Red, 3),    // Красный
            new Pen(Color.Yellow, 3), // Желтый
            new Pen(Color.Green, 3),  // Зеленый
            new Pen(Color.Yellow, 3)  // Желтый
        };

        public MainForm(
            ITrafficSimulationService simulationService,
            IGraphEditorService graphEditorService,
            IComparisonService comparisonService)
        {
            InitializeComponent();

            _simulationService = simulationService;
            _graphEditorService = graphEditorService;
            _comparisonService = comparisonService;

            // Подписка на события симуляции
            _simulationService.SimulationStepCompleted += OnSimulationStepCompleted;
            _simulationService.IncidentOccurred += OnIncidentOccurred;

            SetupUI();
            // Убрали вызов InitializeSimulation(), так как его нет
        }

        private void SetupUI()
        {
            // Создание вкладок
            var tabControl = new TabControl { Dock = DockStyle.Fill };

            // Вкладка визуализации
            var visualizationTab = new TabPage("Визуализация");
            SetupVisualizationTab(visualizationTab);

            // Вкладка редактора графа
            var editorTab = new TabPage("Редактор графа");
            SetupGraphEditorTab(editorTab);

            // Вкладка параметров
            var parametersTab = new TabPage("Параметры");
            SetupParametersTab(parametersTab);

            // Вкладка сравнения
            var comparisonTab = new TabPage("Сравнение");
            SetupComparisonTab(comparisonTab);

            tabControl.TabPages.AddRange(new[] {
                visualizationTab, editorTab, parametersTab, comparisonTab
            });

            Controls.Add(tabControl);

            // Панель управления
            var controlPanel = CreateControlPanel();
            Controls.Add(controlPanel);

            // Статус бар
            var statusBar = CreateStatusBar();
            Controls.Add(statusBar);
        }

        private Panel CreateControlPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = SystemColors.Control
            };

            var btnStart = new Button
            {
                Text = "Старт",
                Location = new Point(10, 10),
                Size = new Size(80, 30)
            };
            btnStart.Click += (s, e) => StartSimulation();

            var btnPause = new Button
            {
                Text = "Пауза",
                Location = new Point(100, 10),
                Size = new Size(80, 30)
            };
            btnPause.Click += (s, e) => PauseSimulation();

            var btnStop = new Button
            {
                Text = "Стоп",
                Location = new Point(190, 10),
                Size = new Size(80, 30)
            };
            btnStop.Click += (s, e) => StopSimulation();

            var btnStep = new Button
            {
                Text = "Шаг",
                Location = new Point(280, 10),
                Size = new Size(80, 30)
            };
            btnStep.Click += async (s, e) => await StepSimulation();

            var btnSave = new Button
            {
                Text = "Сохранить",
                Location = new Point(370, 10),
                Size = new Size(80, 30)
            };
            btnSave.Click += (s, e) => SaveCurrentState();

            panel.Controls.AddRange(new Control[] {
                btnStart, btnPause, btnStop, btnStep, btnSave
            });

            return panel;
        }

        private StatusStrip CreateStatusBar()
        {
            var statusStrip = new StatusStrip();

            var timeLabel = new ToolStripStatusLabel("Время: 0 сек");
            var vehiclesLabel = new ToolStripStatusLabel("ТС: 0");
            var pedestriansLabel = new ToolStripStatusLabel("Пешеходы: 0");
            var congestionLabel = new ToolStripStatusLabel("Загруженность: 0%");
            var incidentsLabel = new ToolStripStatusLabel("Инциденты: 0");

            statusStrip.Items.AddRange(new ToolStripItem[] {
                timeLabel, vehiclesLabel, pedestriansLabel,
                congestionLabel, incidentsLabel
            });

            return statusStrip;
        }

        private void SetupVisualizationTab(TabPage tab)
        {
            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 700
            };

            // Левая панель - визуализация
            var pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            splitContainer.Panel1.Controls.Add(pictureBox);

            // Правая панель - статистика и происшествия
            var rightPanel = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal
            };

            // Верхняя панель - статистика
            var statsTextBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Consolas", 10)
            };
            rightPanel.Panel1.Controls.Add(statsTextBox);

            // Нижняя панель - происшествия
            var incidentsListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9)
            };
            rightPanel.Panel2.Controls.Add(incidentsListBox);

            splitContainer.Panel2.Controls.Add(rightPanel);
            tab.Controls.Add(splitContainer);

            // Настройка таймера рендеринга
            _renderTimer = new System.Windows.Forms.Timer { Interval = 50 };
            _renderTimer.Tick += (s, e) => RenderNetwork(pictureBox, statsTextBox, incidentsListBox);
            _renderTimer.Start();
        }

        private void SetupGraphEditorTab(TabPage tab)
        {
            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical
            };

            // Панель редактора
            var editorPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };

            // Панель инструментов редактора
            var toolPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight
            };

            var btnAddVertex = new Button { Text = "Добавить вершину", Size = new Size(120, 30) };
            var btnAddEdge = new Button { Text = "Добавить ребро", Size = new Size(120, 30) };
            var btnRemove = new Button { Text = "Удалить", Size = new Size(80, 30) };
            var btnSaveNetwork = new Button { Text = "Сохранить сеть", Size = new Size(120, 30) };
            var btnLoadNetwork = new Button { Text = "Загрузить сеть", Size = new Size(120, 30) };

            btnAddVertex.Click += (s, e) => StartAddVertexMode();
            btnAddEdge.Click += (s, e) => StartAddEdgeMode();
            btnSaveNetwork.Click += async (s, e) => await SaveNetwork();
            btnLoadNetwork.Click += async (s, e) => await LoadNetwork();

            toolPanel.Controls.AddRange(new Control[] {
                btnAddVertex, btnAddEdge, btnRemove, btnSaveNetwork, btnLoadNetwork
            });

            editorPanel.Controls.Add(toolPanel);

            // Список сетей
            var networkList = new ListBox
            {
                Dock = DockStyle.Left,
                Width = 200
            };

            splitContainer.Panel1.Controls.Add(editorPanel);
            splitContainer.Panel2.Controls.Add(networkList);

            tab.Controls.Add(splitContainer);
        }

        private void SetupParametersTab(TabPage tab)
        {
            var propertyGrid = new PropertyGrid
            {
                Dock = DockStyle.Fill,
                SelectedObject = new SimulationParameters()
            };

            tab.Controls.Add(propertyGrid);
        }

        private void SetupComparisonTab(TabPage tab)
        {
            var dataGridView = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };

            // Кнопка сравнения
            var btnCompare = new Button
            {
                Text = "Сравнить выбранные",
                Dock = DockStyle.Bottom,
                Height = 40
            };

            tab.Controls.Add(dataGridView);
            tab.Controls.Add(btnCompare);
        }

        private async void StartSimulation()
        {
            if (_currentNetwork == null)
            {
                MessageBox.Show("Сначала загрузите или создайте дорожную сеть");
                return;
            }

            var parameters = new SimulationParameters
            {
                Name = "Новая симуляция",
                CreationDate = DateTime.Now
            };

            _currentSession = await _simulationService.CreateSessionAsync(
                "Новая сессия",
                _currentNetwork.Id,
                parameters);

            if (_currentSession != null)
            {
                await _simulationService.StartSimulationAsync(_currentSession.Id);
            }
        }

        private async void PauseSimulation()
        {
            if (_currentSession != null)
            {
                await _simulationService.PauseSimulationAsync(_currentSession.Id);
            }
        }

        private async void StopSimulation()
        {
            if (_currentSession != null)
            {
                await _simulationService.StopSimulationAsync(_currentSession.Id);
            }
        }

        private async Task StepSimulation()
        {
            if (_currentSession != null)
            {
                await _simulationService.SimulateStepAsync(_currentSession.Id);
            }
        }

        private async Task SaveNetwork()
        {
            if (_currentNetwork == null)
                return;

            var saveDialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = $"{_currentNetwork.Name}.json"
            };

            if (saveDialog.ShowDialog() == DialogResult.OK)
            {
                var json = JsonConvert.SerializeObject(_currentNetwork, Formatting.Indented);
                await File.WriteAllTextAsync(saveDialog.FileName, json);
                MessageBox.Show("Сеть сохранена успешно");
            }
        }

        private async Task LoadNetwork()
        {
            var openDialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json"
            };

            if (openDialog.ShowDialog() == DialogResult.OK)
            {
                var json = await File.ReadAllTextAsync(openDialog.FileName);
                _currentNetwork = JsonConvert.DeserializeObject<RoadNetwork>(json);
                MessageBox.Show("Сеть загружена успешно");
            }
        }

        private void StartAddVertexMode()
        {
            // Режим добавления вершины по клику
            // Реализация зависит от выбранного элемента управления для рисования
        }

        private void StartAddEdgeMode()
        {
            // Режим добавления ребра между двумя вершинами
        }

        private void RenderNetwork(PictureBox pictureBox, RichTextBox statsBox, ListBox incidentsBox)
        {
            if (_currentNetwork == null)
                return;

            var bitmap = new Bitmap(pictureBox.Width, pictureBox.Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                // Рисование дорог
                foreach (var edge in _currentNetwork.Edges)
                {
                    var startVertex = _currentNetwork.Vertices.First(v => v.Id == edge.StartVertexId);
                    var endVertex = _currentNetwork.Vertices.First(v => v.Id == edge.EndVertexId);

                    // Цвет в зависимости от загруженности
                    Color color = edge.CongestionLevel switch
                    {
                        < 0.3 => Color.Green,
                        < 0.7 => Color.Orange,
                        _ => Color.Red
                    };

                    if (edge.IsBlocked)
                        color = Color.Purple;

                    var pen = new Pen(color, edge.Lanes * 2);
                    if (edge.Type == RoadType.Highway)
                        pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;

                    g.DrawLine(pen, startVertex.X / 10, startVertex.Y / 10,
                        endVertex.X / 10, endVertex.Y / 10);

                    // Подпись для скоростных дорог
                    if (edge.Type == RoadType.Highway && !edge.IsBlocked)
                    {
                        var midX = (startVertex.X + endVertex.X) / 20;
                        var midY = (startVertex.Y + endVertex.Y) / 20;
                        g.DrawString($"{edge.MaxSpeed}km/h",
                            new Font("Arial", 8),
                            Brushes.Black,
                            midX, midY);
                    }
                }

                // Рисование вершин
                foreach (var vertex in _currentNetwork.Vertices)
                {
                    var brush = vertex.HasTrafficLights ?
                        GetTrafficLightBrush(vertex.TrafficLightPhase) :
                        Brushes.Black;

                    g.FillEllipse(brush, vertex.X / 10 - 5, vertex.Y / 10 - 5, 10, 10);
                    g.DrawEllipse(Pens.Black, vertex.X / 10 - 5, vertex.Y / 10 - 5, 10, 10);

                    g.DrawString(vertex.CityId.ToString(),
                        new Font("Arial", 8),
                        Brushes.Black,
                        vertex.X / 10 - 10, vertex.Y / 10 + 10);
                }

                // Рисование транспортных средств
                if (_currentSession != null)
                {
                    foreach (var vehicle in _currentSession.Vehicles)
                    {
                        var edge = _currentSession.Network.Edges
                            .FirstOrDefault(e => e.Id == vehicle.CurrentEdgeId);

                        if (edge == null) continue;

                        var startVertex = _currentSession.Network.Vertices
                            .First(v => v.Id == edge.StartVertexId);
                        var endVertex = _currentSession.Network.Vertices
                            .First(v => v.Id == edge.EndVertexId);

                        double progress = vehicle.Position.Offset / edge.Length;
                        int x = (int)(startVertex.X / 10 + (endVertex.X / 10 - startVertex.X / 10) * progress);
                        int y = (int)(startVertex.Y / 10 + (endVertex.Y / 10 - startVertex.Y / 10) * progress);

                        var vehicleBrush = GetVehicleBrush(vehicle.Type);
                        var size = vehicle.Type == VehicleType.Bicycle ? 4 : 6;

                        g.FillEllipse(vehicleBrush, x - size / 2, y - size / 2, size, size);
                        g.DrawEllipse(Pens.Black, x - size / 2, y - size / 2, size, size);
                    }
                }
            }

            pictureBox.Image = bitmap;

            // Обновление статистики
            UpdateStatistics(statsBox);

            // Обновление списка происшествий
            UpdateIncidentsList(incidentsBox);
        }

        private Brush GetTrafficLightBrush(int phase)
        {
            return phase switch
            {
                0 => Brushes.Red,
                1 => Brushes.Yellow,
                2 => Brushes.Green,
                3 => Brushes.Yellow,
                _ => Brushes.Gray
            };
        }

        private Brush GetVehicleBrush(VehicleType type)
        {
            return type switch
            {
                VehicleType.Car => Brushes.Blue,
                VehicleType.NoviceCar => Brushes.Cyan,
                VehicleType.Bus => Brushes.Green,
                VehicleType.Truck => Brushes.Brown,
                VehicleType.Special => Brushes.Red,
                VehicleType.Bicycle => Brushes.Orange,
                _ => Brushes.Black
            };
        }

        private void UpdateStatistics(RichTextBox statsBox)
        {
            if (_currentSession == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("=== СТАТИСТИКА СИМУЛЯЦИИ ===");
            sb.AppendLine($"Время: {_currentSession.CurrentTime} сек");
            sb.AppendLine($"Шаг: {_currentSession.StepCount}");
            sb.AppendLine($"ТС в системе: {_currentSession.Vehicles.Count}");
            sb.AppendLine($"Завершено ТС: {_currentSession.CompletedVehiclesCount}");

            if (_currentSession.Metrics.Any())
            {
                var lastMetric = _currentSession.Metrics.Last();
                // Используем AverageVehicleSpeed вместо AverageSpeed
                sb.AppendLine($"Средняя скорость: {lastMetric.AverageVehicleSpeed:F1} км/ч");
                sb.AppendLine($"Загруженность: {lastMetric.CongestionLevel * 100:F1}%");
                sb.AppendLine($"Активные инциденты: {lastMetric.ActiveIncidents}");
            }

            statsBox.Text = sb.ToString();
        }

        private void UpdateIncidentsList(ListBox incidentsBox)
        {
            if (_currentSession == null) return;

            incidentsBox.Items.Clear();
            // Здесь нужно получить актуальные инциденты из сессии
            // incidentsBox.Items.AddRange(_currentSession.Incidents.ToArray());
        }

        private void OnSimulationStepCompleted(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => OnSimulationStepCompleted(sender, e)));
                return;
            }
            // Обновление UI
            this.Invalidate();
        }

        private void OnIncidentOccurred(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => OnIncidentOccurred(sender, e)));
                return;
            }
            // Обработка инцидента
        }

        private void SaveCurrentState()
        {
            // Сохранение текущего состояния симуляции
            var saveDialog = new SaveFileDialog
            {
                Filter = "Simulation session (*.json)|*.json",
                FileName = $"simulation_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (saveDialog.ShowDialog() == DialogResult.OK && _currentSession != null)
            {
                // Сохраняем сессию как JSON
                var json = JsonConvert.SerializeObject(_currentSession, Formatting.Indented);
                File.WriteAllText(saveDialog.FileName, json);
                MessageBox.Show("Сессия сохранена успешно");
            }
        }
    }
}