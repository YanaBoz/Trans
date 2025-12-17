using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using TrafficSimulation.Core.Events;
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
        private PictureBox _visualizationCanvas;
        private Graphics _canvasGraphics;
        private Bitmap _canvasBitmap;
        private System.Windows.Forms.Timer _renderTimer;
        private RichTextBox _statsTextBox;
        private ListBox _incidentsListBox;
        private Chart _metricsChart;

        // Управление видом
        private float _zoom = 1.0f;
        private PointF _panOffset = PointF.Empty;
        private bool _isPanning = false;
        private Point _lastMousePosition;

        // Цвета
        private readonly Color[] _vehicleColors = new[]
        {
            Color.Blue,    // Car
            Color.Cyan,    // NoviceCar
            Color.Green,   // Bus
            Color.Brown,   // Truck
            Color.Red,     // Special
            Color.Orange   // Bicycle
        };

        private readonly Color[] _trafficLightColors = new[]
        {
            Color.Red,
            Color.Yellow,
            Color.Green,
            Color.Yellow
        };

        public MainForm(
            ITrafficSimulationService simulationService,
            IGraphEditorService graphEditorService,
            IComparisonService comparisonService)
        {
            _simulationService = simulationService;
            _graphEditorService = graphEditorService;
            _comparisonService = comparisonService;

            InitializeComponent();
            SetupUI();

            // Подписка на события симуляции
            _simulationService.SimulationStepCompleted += OnSimulationStepCompleted;
            _simulationService.IncidentOccurred += OnIncidentOccurred;
            _simulationService.StateChanged += OnSimulationStateChanged;
        }

        private void InitializeComponent()
        {
            this.Text = "Транспортная симуляция";
            this.Size = new Size(1400, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.KeyPreview = true;
        }

        private void SetupUI()
        {
            // Основной таб-контрол
            var tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                ItemSize = new Size(100, 25)
            };

            // Вкладка визуализации
            var visualizationTab = new TabPage("Визуализация");
            SetupVisualizationTab(visualizationTab);

            // Вкладка редактора графа
            var editorTab = new TabPage("Редактор графа");
            SetupGraphEditorTab(editorTab);

            // Вкладка статистики
            var statsTab = new TabPage("Статистика");
            SetupStatisticsTab(statsTab);

            // Вкладка параметров
            var paramsTab = new TabPage("Параметры");
            SetupParametersTab(paramsTab);

            // Вкладка сравнения
            var comparisonTab = new TabPage("Сравнение");
            SetupComparisonTab(comparisonTab);

            tabControl.TabPages.AddRange(new[]
            {
                visualizationTab, editorTab, statsTab, paramsTab, comparisonTab
            });

            this.Controls.Add(tabControl);

            // Панель управления
            var controlPanel = CreateControlPanel();
            this.Controls.Add(controlPanel);

            // Статус бар
            var statusBar = CreateStatusBar();
            this.Controls.Add(statusBar);
        }

        private Panel CreateControlPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                BackColor = SystemColors.Control,
                Padding = new Padding(5)
            };

            var flowPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            var btnNewNetwork = new Button { Text = "Новая сеть", Size = new Size(100, 30) };
            var btnLoadNetwork = new Button { Text = "Загрузить сеть", Size = new Size(100, 30) };
            var btnSaveNetwork = new Button { Text = "Сохранить сеть", Size = new Size(100, 30) };
            var btnOpenEditor = new Button { Text = "Редактор", Size = new Size(100, 30) };

            var separator1 = new Label { Text = "|", AutoSize = true, Font = new Font("Arial", 12), Margin = new Padding(10, 8, 10, 0) };

            var btnStart = new Button { Text = "Старт", Size = new Size(80, 30), BackColor = Color.LightGreen };
            var btnPause = new Button { Text = "Пауза", Size = new Size(80, 30), BackColor = Color.LightYellow };
            var btnStop = new Button { Text = "Стоп", Size = new Size(80, 30), BackColor = Color.LightCoral };
            var btnStep = new Button { Text = "Шаг", Size = new Size(80, 30) };

            var separator2 = new Label { Text = "|", AutoSize = true, Font = new Font("Arial", 12), Margin = new Padding(10, 8, 10, 0) };

            var btnZoomIn = new Button { Text = "+", Size = new Size(40, 30) };
            var btnZoomOut = new Button { Text = "-", Size = new Size(40, 30) };
            var btnFit = new Button { Text = "По размеру", Size = new Size(90, 30) };
            var btnSaveSession = new Button { Text = "Сохранить сессию", Size = new Size(120, 30) };

            // Обработчики событий
            btnNewNetwork.Click += async (s, e) => await CreateNewNetwork();
            btnLoadNetwork.Click += async (s, e) => await LoadNetwork();
            btnSaveNetwork.Click += async (s, e) => await SaveNetwork();
            btnOpenEditor.Click += (s, e) => OpenGraphEditor();
            btnStart.Click += async (s, e) => await StartSimulation();
            btnPause.Click += async (s, e) => await PauseSimulation();
            btnStop.Click += async (s, e) => await StopSimulation();
            btnStep.Click += async (s, e) => await StepSimulation();
            btnZoomIn.Click += (s, e) => ZoomIn();
            btnZoomOut.Click += (s, e) => ZoomOut();
            btnFit.Click += (s, e) => FitToView();
            btnSaveSession.Click += (s, e) => SaveCurrentSession();

            flowPanel.Controls.AddRange(new Control[]
            {
                btnNewNetwork, btnLoadNetwork, btnSaveNetwork, btnOpenEditor,
                separator1,
                btnStart, btnPause, btnStop, btnStep,
                separator2,
                btnZoomIn, btnZoomOut, btnFit, btnSaveSession
            });

            panel.Controls.Add(flowPanel);
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
            var zoomLabel = new ToolStripStatusLabel($"Масштаб: {_zoom:F1}x");
            var networkLabel = new ToolStripStatusLabel("Сеть: нет");

            statusStrip.Items.AddRange(new ToolStripItem[]
            {
                timeLabel, vehiclesLabel, pedestriansLabel,
                congestionLabel, incidentsLabel, zoomLabel, networkLabel
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

            // Левая панель - визуализация с поддержкой зума
            _visualizationCanvas = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                SizeMode = PictureBoxSizeMode.Zoom
            };

            _visualizationCanvas.MouseDown += VisualizationCanvas_MouseDown;
            _visualizationCanvas.MouseMove += VisualizationCanvas_MouseMove;
            _visualizationCanvas.MouseUp += VisualizationCanvas_MouseUp;
            _visualizationCanvas.MouseWheel += VisualizationCanvas_MouseWheel;
            _visualizationCanvas.Paint += VisualizationCanvas_Paint;
            _visualizationCanvas.Resize += VisualizationCanvas_Resize;

            splitContainer.Panel1.Controls.Add(_visualizationCanvas);

            // Правая панель - статистика и происшествия
            var rightPanel = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 200
            };

            // Верхняя панель - статистика
            _statsTextBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Consolas", 10),
                BackColor = Color.Black,
                ForeColor = Color.Lime
            };
            rightPanel.Panel1.Controls.Add(_statsTextBox);

            // Нижняя панель - происшествия
            _incidentsListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9),
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 40
            };
            _incidentsListBox.DrawItem += (s, e) => DrawIncidentItem(s, e);
            rightPanel.Panel2.Controls.Add(_incidentsListBox);

            splitContainer.Panel2.Controls.Add(rightPanel);
            tab.Controls.Add(splitContainer);

            // Настройка таймера рендеринга
            _renderTimer = new System.Windows.Forms.Timer { Interval = 50 };
            _renderTimer.Tick += (s, e) => RenderVisualization();
            _renderTimer.Start();
        }

        private void SetupGraphEditorTab(TabPage tab)
        {
            var label = new Label
            {
                Text = "Редактор графа открывается в отдельном окне.\nНажмите кнопку 'Редактор' на панели управления.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Arial", 12)
            };
            tab.Controls.Add(label);
        }

        private void SetupStatisticsTab(TabPage tab)
        {
            _metricsChart = new Chart
            {
                Dock = DockStyle.Fill
            };

            var chartArea = new ChartArea("MainArea");
            chartArea.AxisX.Title = "Время симуляции (сек)";
            chartArea.AxisY.Title = "Значение";
            chartArea.AxisX.Minimum = 0;
            chartArea.AxisX.Interval = 60;
            _metricsChart.ChartAreas.Add(chartArea);

            // Серия для средней скорости
            var speedSeries = new Series("Средняя скорость")
            {
                ChartType = SeriesChartType.Line,
                Color = Color.Blue,
                BorderWidth = 2,
                XValueType = ChartValueType.Int32,
                YValueType = ChartValueType.Double
            };
            _metricsChart.Series.Add(speedSeries);

            // Серия для загруженности
            var congestionSeries = new Series("Загруженность")
            {
                ChartType = SeriesChartType.Line,
                Color = Color.Red,
                BorderWidth = 2,
                XValueType = ChartValueType.Int32,
                YValueType = ChartValueType.Double
            };
            _metricsChart.Series.Add(congestionSeries);

            // Серия для пропускной способности
            var throughputSeries = new Series("Пропускная способность")
            {
                ChartType = SeriesChartType.Line,
                Color = Color.Green,
                BorderWidth = 2,
                XValueType = ChartValueType.Int32,
                YValueType = ChartValueType.Int32
            };
            _metricsChart.Series.Add(throughputSeries);

            tab.Controls.Add(_metricsChart);
        }

        private void SetupParametersTab(TabPage tab)
        {
            var propertyGrid = new PropertyGrid
            {
                Dock = DockStyle.Fill,
                SelectedObject = new SimulationParameters(),
                ToolbarVisible = true
            };
            tab.Controls.Add(propertyGrid);
        }

        private void SetupComparisonTab(TabPage tab)
        {
            var dataGridView = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ReadOnly = true
            };

            // Кнопка сравнения
            var btnCompare = new Button
            {
                Text = "Сравнить сессии",
                Dock = DockStyle.Bottom,
                Height = 40
            };
            btnCompare.Click += async (s, e) => await ShowComparisonForm();

            tab.Controls.Add(dataGridView);
            tab.Controls.Add(btnCompare);
        }

        #region Управление видом

        private void VisualizationCanvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right)
            {
                _isPanning = true;
                _lastMousePosition = e.Location;
                _visualizationCanvas.Cursor = Cursors.SizeAll;
            }
        }

        private void VisualizationCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                var dx = e.X - _lastMousePosition.X;
                var dy = e.Y - _lastMousePosition.Y;

                _panOffset.X += dx;
                _panOffset.Y += dy;

                _lastMousePosition = e.Location;
                _visualizationCanvas.Invalidate();
            }
        }

        private void VisualizationCanvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                _visualizationCanvas.Cursor = Cursors.Default;
            }
        }

        private void VisualizationCanvas_MouseWheel(object sender, MouseEventArgs e)
        {
            var oldZoom = _zoom;
            var zoomFactor = 1.1f;

            if (e.Delta > 0)
                _zoom *= zoomFactor;
            else
                _zoom /= zoomFactor;

            _zoom = Math.Max(0.1f, Math.Min(10f, _zoom));

            // Корректируем смещение
            var centerX = _visualizationCanvas.Width / 2;
            var centerY = _visualizationCanvas.Height / 2;

            var worldCenterX = (centerX - _panOffset.X) / oldZoom;
            var worldCenterY = (centerY - _panOffset.Y) / oldZoom;

            _panOffset.X = centerX - worldCenterX * _zoom;
            _panOffset.Y = centerY - worldCenterY * _zoom;

            UpdateZoomLabel();
            _visualizationCanvas.Invalidate();
        }

        private void VisualizationCanvas_Paint(object sender, PaintEventArgs e)
        {
            RenderNetwork(e.Graphics);
        }

        private void VisualizationCanvas_Resize(object sender, EventArgs e)
        {
            _visualizationCanvas.Invalidate();
        }

        private void ZoomIn()
        {
            var oldZoom = _zoom;
            _zoom *= 1.2f;
            _zoom = Math.Min(10f, _zoom);
            AdjustPanOffset(oldZoom);
            UpdateZoomLabel();
            _visualizationCanvas.Invalidate();
        }

        private void ZoomOut()
        {
            var oldZoom = _zoom;
            _zoom /= 1.2f;
            _zoom = Math.Max(0.1f, _zoom);
            AdjustPanOffset(oldZoom);
            UpdateZoomLabel();
            _visualizationCanvas.Invalidate();
        }

        private void FitToView()
        {
            if (_currentNetwork == null || !_currentNetwork.Vertices.Any()) return;

            var vertices = _currentNetwork.Vertices.ToList();
            var minX = vertices.Min(v => v.X);
            var maxX = vertices.Max(v => v.X);
            var minY = vertices.Min(v => v.Y);
            var maxY = vertices.Max(v => v.Y);

            var width = maxX - minX + 200;
            var height = maxY - minY + 200;

            var scaleX = _visualizationCanvas.Width / (float)width;
            var scaleY = _visualizationCanvas.Height / (float)height;
            _zoom = Math.Min(scaleX, scaleY) * 0.9f;

            var centerX = (minX + maxX) / 2;
            var centerY = (minY + maxY) / 2;

            _panOffset = new PointF(
                _visualizationCanvas.Width / 2 - centerX * _zoom,
                _visualizationCanvas.Height / 2 - centerY * _zoom
            );

            UpdateZoomLabel();
            _visualizationCanvas.Invalidate();
        }

        private void AdjustPanOffset(float oldZoom)
        {
            if (oldZoom == 0) return;

            var centerX = _visualizationCanvas.Width / 2;
            var centerY = _visualizationCanvas.Height / 2;

            var worldCenterX = (centerX - _panOffset.X) / oldZoom;
            var worldCenterY = (centerY - _panOffset.Y) / oldZoom;

            _panOffset.X = centerX - worldCenterX * _zoom;
            _panOffset.Y = centerY - worldCenterY * _zoom;
        }

        private void UpdateZoomLabel()
        {
            var statusBar = this.Controls.OfType<StatusStrip>().FirstOrDefault();
            if (statusBar != null && statusBar.Items.Count > 5)
            {
                statusBar.Items[5].Text = $"Масштаб: {_zoom:F1}x";
            }
        }

        #endregion

        #region Рендеринг

        private void RenderVisualization()
        {
            if (_visualizationCanvas.Width <= 0 || _visualizationCanvas.Height <= 0)
                return;

            if (_canvasBitmap == null ||
                _canvasBitmap.Width != _visualizationCanvas.Width ||
                _canvasBitmap.Height != _visualizationCanvas.Height)
            {
                _canvasBitmap?.Dispose();
                _canvasBitmap = new Bitmap(_visualizationCanvas.Width, _visualizationCanvas.Height);
                _canvasGraphics?.Dispose();
                _canvasGraphics = Graphics.FromImage(_canvasBitmap);
            }

            _canvasGraphics.Clear(Color.White);
            RenderNetwork(_canvasGraphics);
            _visualizationCanvas.Image = _canvasBitmap;
        }

        private void RenderNetwork(Graphics g)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            if (_currentNetwork == null)
            {
                // Отрисовка сообщения об отсутствии сети
                var message = "Дорожная сеть не загружена\nНажмите 'Новая сеть' или 'Загрузить сеть'";
                var font = new Font("Arial", 16, FontStyle.Bold);
                var size = g.MeasureString(message, font);
                var x = (_visualizationCanvas.Width - size.Width) / 2;
                var y = (_visualizationCanvas.Height - size.Height) / 2;

                g.DrawString(message, font, Brushes.Gray, x, y);
                return;
            }

            // Преобразование координат
            Func<int, int, PointF> worldToScreen = (wx, wy) =>
                new PointF(wx * _zoom + _panOffset.X, wy * _zoom + _panOffset.Y);

            // Рисование дорог
            foreach (var edge in _currentNetwork.Edges)
            {
                var startVertex = _currentNetwork.Vertices.FirstOrDefault(v => v.Id == edge.StartVertexId);
                var endVertex = _currentNetwork.Vertices.FirstOrDefault(v => v.Id == edge.EndVertexId);

                if (startVertex == null || endVertex == null) continue;

                var startPoint = worldToScreen(startVertex.X, startVertex.Y);
                var endPoint = worldToScreen(endVertex.X, endVertex.Y);

                // Цвет в зависимости от загруженности
                Color color;
                if (edge.IsBlocked)
                {
                    color = Color.Purple;
                }
                else
                {
                    var congestion = edge.CongestionLevel;
                    if (congestion < 0.3)
                        color = Color.Green;
                    else if (congestion < 0.7)
                        color = Color.Orange;
                    else
                        color = Color.Red;
                }

                var penWidth = Math.Max(1, edge.Lanes * 2 * _zoom);
                using (var pen = new Pen(color, penWidth))
                {
                    if (edge.Type == RoadType.Highway)
                        pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;

                    g.DrawLine(pen, startPoint, endPoint);

                    // Подпись дороги (только если достаточно увеличения)
                    if (_zoom > 0.3)
                    {
                        var midPoint = new PointF((startPoint.X + endPoint.X) / 2,
                                                 (startPoint.Y + endPoint.Y) / 2);
                        var label = $"{edge.MaxSpeed} км/ч";

                        var fontSize = Math.Max(6, 8 * _zoom);
                        using (var font = new Font("Arial", fontSize))
                        {
                            var size = g.MeasureString(label, font);
                            var rect = new RectangleF(
                                midPoint.X - size.Width / 2,
                                midPoint.Y - size.Height / 2,
                                size.Width,
                                size.Height);

                            g.FillRectangle(Brushes.White, rect);
                            g.DrawString(label, font, Brushes.Black, rect);
                        }
                    }
                }
            }

            // Рисование вершин
            foreach (var vertex in _currentNetwork.Vertices)
            {
                var center = worldToScreen(vertex.X, vertex.Y);
                var radius = 5 * _zoom;

                Color vertexColor;
                if (vertex.HasTrafficLights && _currentSession != null)
                {
                    var phase = vertex.TrafficLightPhase % _trafficLightColors.Length;
                    vertexColor = _trafficLightColors[phase];
                }
                else
                {
                    vertexColor = vertex.Type == VertexType.Intersection ? Color.Black : Color.DarkGray;
                }

                using (var brush = new SolidBrush(vertexColor))
                {
                    g.FillEllipse(brush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
                }
                g.DrawEllipse(Pens.Black, center.X - radius, center.Y - radius, radius * 2, radius * 2);

                // Подпись вершины
                if (_zoom > 0.5)
                {
                    var label = $"{vertex.Name} (C{vertex.CityId})";
                    var fontSize = Math.Max(6, 8 * _zoom);
                    using (var font = new Font("Arial", fontSize))
                    {
                        g.DrawString(label, font, Brushes.Black,
                            center.X + radius + 2, center.Y - radius);
                    }
                }
            }

            // Рисование транспортных средств
            if (_currentSession != null)
            {
                foreach (var vehicle in _currentSession.Vehicles)
                {
                    var edge = _currentNetwork.Edges.FirstOrDefault(e => e.Id == vehicle.CurrentEdgeId);
                    if (edge == null) continue;

                    var startVertex = _currentNetwork.Vertices.First(v => v.Id == edge.StartVertexId);
                    var endVertex = _currentNetwork.Vertices.First(v => v.Id == edge.EndVertexId);

                    var progress = vehicle.Position.Offset / edge.Length;
                    var worldX = startVertex.X + (endVertex.X - startVertex.X) * progress;
                    var worldY = startVertex.Y + (endVertex.Y - startVertex.Y) * progress;

                    var screenPoint = worldToScreen((int)worldX, (int)worldY);
                    var size = 4 * _zoom;

                    var vehicleColor = _vehicleColors[(int)vehicle.Type % _vehicleColors.Length];
                    using (var brush = new SolidBrush(vehicleColor))
                    {
                        g.FillEllipse(brush, screenPoint.X - size, screenPoint.Y - size, size * 2, size * 2);
                    }
                    g.DrawEllipse(Pens.Black, screenPoint.X - size, screenPoint.Y - size, size * 2, size * 2);
                }
            }

            // Рисование сетки (если достаточно увеличения)
            if (_zoom > 0.5)
            {
                var gridSize = 100 * _zoom;
                using (var gridPen = new Pen(Color.FromArgb(30, Color.Gray)))
                {
                    for (float x = 0; x < _visualizationCanvas.Width; x += gridSize)
                        g.DrawLine(gridPen, x, 0, x, _visualizationCanvas.Height);

                    for (float y = 0; y < _visualizationCanvas.Height; y += gridSize)
                        g.DrawLine(gridPen, 0, y, _visualizationCanvas.Width, y);
                }
            }
        }

        private void DrawIncidentItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _incidentsListBox.Items.Count)
                return;

            var incident = (TrafficIncident)_incidentsListBox.Items[e.Index];
            e.DrawBackground();

            Color textColor = e.ForeColor;
            Color backColor = e.BackColor;

            switch (incident.Severity)
            {
                case IncidentSeverity.High:
                    backColor = Color.DarkRed;
                    textColor = Color.White;
                    break;
                case IncidentSeverity.Medium:
                    backColor = Color.Orange;
                    textColor = Color.Black;
                    break;
                case IncidentSeverity.Low:
                    backColor = Color.Yellow;
                    textColor = Color.Black;
                    break;
            }

            using (var brush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            using (var brush = new SolidBrush(textColor))
            {
                var font = e.Font;
                var boldFont = new Font(font, FontStyle.Bold);

                var typeRect = new Rectangle(e.Bounds.X + 5, e.Bounds.Y + 2, e.Bounds.Width - 10, 15);
                e.Graphics.DrawString(incident.Type.ToString(), boldFont, brush, typeRect);

                var timeRect = new Rectangle(e.Bounds.X + 5, e.Bounds.Y + 18, e.Bounds.Width - 10, 15);
                e.Graphics.DrawString(incident.Time.ToString("HH:mm:ss"), e.Font, brush, timeRect);
            }

            e.DrawFocusRectangle();
        }

        #endregion

        #region Управление сетями

        private async Task CreateNewNetwork()
        {
            var name = Microsoft.VisualBasic.Interaction.InputBox(
                "Введите название новой дорожной сети:",
                "Новая сеть",
                $"Сеть_{DateTime.Now:yyyyMMdd_HHmmss}");

            if (!string.IsNullOrEmpty(name))
            {
                try
                {
                    _currentNetwork = await _graphEditorService.CreateNetworkAsync(name);
                    FitToView();
                    UpdateNetworkLabel();
                    MessageBox.Show($"Создана новая сеть: {name}", "Успех",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка создания сети: {ex.Message}", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private async Task LoadNetwork()
        {
            var openDialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Загрузить дорожную сеть"
            };

            if (openDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(openDialog.FileName);
                    _currentNetwork = JsonConvert.DeserializeObject<RoadNetwork>(json);
                    FitToView();
                    UpdateNetworkLabel();
                    MessageBox.Show($"Сеть загружена из {Path.GetFileName(openDialog.FileName)}",
                        "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка загрузки сети: {ex.Message}", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private async Task SaveNetwork()
        {
            if (_currentNetwork == null)
            {
                MessageBox.Show("Нет активной сети для сохранения", "Предупреждение",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = $"{_currentNetwork.Name}.json",
                Title = "Сохранить дорожную сеть"
            };

            if (saveDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var json = JsonConvert.SerializeObject(_currentNetwork, Formatting.Indented);
                    await File.WriteAllTextAsync(saveDialog.FileName, json);
                    MessageBox.Show($"Сеть сохранена в {Path.GetFileName(saveDialog.FileName)}",
                        "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка сохранения сети: {ex.Message}", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OpenGraphEditor()
        {
            var editorForm = new GraphEditorForm(_graphEditorService);
            editorForm.Show();
        }

        private void UpdateNetworkLabel()
        {
            var statusBar = this.Controls.OfType<StatusStrip>().FirstOrDefault();
            if (statusBar != null && statusBar.Items.Count > 6)
            {
                var label = _currentNetwork != null ?
                    $"Сеть: {_currentNetwork.Name} ({_currentNetwork.Vertices.Count} вершин, {_currentNetwork.Edges.Count} ребер)" :
                    "Сеть: нет";
                statusBar.Items[6].Text = label;
            }
        }

        #endregion

        #region Управление симуляцией

        private async Task StartSimulation()
        {
            try
            {
                if (_currentNetwork == null)
                {
                    MessageBox.Show("Сначала загрузите или создайте дорожную сеть", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (_currentSession != null && _currentSession.State == SimulationState.Running)
                {
                    MessageBox.Show("Симуляция уже запущена", "Предупреждение",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var parameters = new SimulationParameters
                {
                    Name = "Параметры по умолчанию",
                    CreationDate = DateTime.Now,
                    InitialVehicles = 20,
                    InitialPedestrians = 10,
                    SimulationDurationSeconds = 3600,
                    TimeStepSeconds = 5
                };

                _currentSession = await _simulationService.CreateSessionAsync(
                    $"Сессия_{DateTime.Now:yyyyMMdd_HHmmss}",
                    _currentNetwork.Id,
                    parameters);

                if (_currentSession != null)
                {
                    await _simulationService.StartSimulationAsync(_currentSession.Id);
                    MessageBox.Show("Симуляция запущена", "Успех",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска симуляции: {ex.Message}\n\n{ex.InnerException?.Message}",
                    "Критическая ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task PauseSimulation()
        {
            if (_currentSession != null)
            {
                await _simulationService.PauseSimulationAsync(_currentSession.Id);
            }
        }

        private async Task StopSimulation()
        {
            if (_currentSession != null)
            {
                await _simulationService.StopSimulationAsync(_currentSession.Id);
                _currentSession = null;
            }
        }

        private async Task StepSimulation()
        {
            if (_currentSession != null)
            {
                await _simulationService.SimulateStepAsync(_currentSession.Id);
            }
        }

        private void SaveCurrentSession()
        {
            if (_currentSession == null)
            {
                MessageBox.Show("Нет активной сессии для сохранения", "Предупреждение",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = $"session_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                Title = "Сохранить сессию симуляции"
            };

            if (saveDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var json = JsonConvert.SerializeObject(_currentSession, Formatting.Indented);
                    File.WriteAllText(saveDialog.FileName, json);
                    MessageBox.Show($"Сессия сохранена в {Path.GetFileName(saveDialog.FileName)}",
                        "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка сохранения сессии: {ex.Message}", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        #endregion

        #region События симуляции

        private void OnSimulationStepCompleted(object sender, SimulationStepEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => OnSimulationStepCompleted(sender, e)));
                return;
            }

            UpdateStatistics();
            UpdateChart(e);
        }

        private void OnIncidentOccurred(object sender, SimulationIncidentEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => OnIncidentOccurred(sender, e)));
                return;
            }

            if (_incidentsListBox != null)
            {
                _incidentsListBox.Items.Insert(0, e.Incident);
                if (_incidentsListBox.Items.Count > 100)
                    _incidentsListBox.Items.RemoveAt(100);
            }
        }

        private void OnSimulationStateChanged(object sender, SimulationStateChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => OnSimulationStateChanged(sender, e)));
                return;
            }

            // Обновление статуса симуляции
        }

        private void UpdateStatistics()
        {
            if (_currentSession == null || _statsTextBox == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("=== СТАТИСТИКА СИМУЛЯЦИИ ===");
            sb.AppendLine($"Время: {_currentSession.CurrentTime} сек");
            sb.AppendLine($"Шаг: {_currentSession.StepCount}");
            sb.AppendLine($"ТС в системе: {_currentSession.Vehicles.Count}");
            sb.AppendLine($"Завершено ТС: {_currentSession.CompletedVehiclesCount}");

            if (_currentSession.Metrics.Any())
            {
                var lastMetric = _currentSession.Metrics.Last();
                sb.AppendLine($"Средняя скорость: {lastMetric.AverageVehicleSpeed:F1} км/ч");
                sb.AppendLine($"Загруженность: {lastMetric.CongestionLevel * 100:F1}%");
                sb.AppendLine($"Активные инциденты: {lastMetric.ActiveIncidents}");
                sb.AppendLine($"ДТП: {lastMetric.AccidentCount}");
                sb.AppendLine($"Пропускная способность: {lastMetric.VehicleThroughput} ТС/час");
            }

            _statsTextBox.Text = sb.ToString();

            // Обновление статус-бара
            var statusBar = this.Controls.OfType<StatusStrip>().FirstOrDefault();
            if (statusBar != null && statusBar.Items.Count >= 5)
            {
                statusBar.Items[0].Text = $"Время: {_currentSession.CurrentTime} сек";
                statusBar.Items[1].Text = $"ТС: {_currentSession.Vehicles.Count}";
                statusBar.Items[2].Text = $"Пешеходы: {_currentSession.Pedestrians.Count}";

                if (_currentSession.Metrics.Any())
                {
                    var lastMetric = _currentSession.Metrics.Last();
                    statusBar.Items[3].Text = $"Загруженность: {lastMetric.CongestionLevel * 100:F1}%";
                    statusBar.Items[4].Text = $"Инциденты: {lastMetric.ActiveIncidents}";
                }
            }
        }

        private void UpdateChart(SimulationStepEventArgs e)
        {
            if (_metricsChart == null || e == null) return;

            _metricsChart.Series[0].Points.AddXY(e.CurrentTime, e.Metrics.AverageVehicleSpeed);
            _metricsChart.Series[1].Points.AddXY(e.CurrentTime, e.Metrics.CongestionLevel * 100);
            _metricsChart.Series[2].Points.AddXY(e.CurrentTime, e.Metrics.VehicleThroughput);

            // Ограничиваем количество точек на графике
            const int maxPoints = 100;
            foreach (var series in _metricsChart.Series)
            {
                while (series.Points.Count > maxPoints)
                {
                    series.Points.RemoveAt(0);
                }
            }
        }

        #endregion

        #region Дополнительные функции

        private async Task ShowComparisonForm()
        {
            var comparisonForm = new ComparisonForm(_comparisonService, null);
            comparisonForm.Show();
        }

        #endregion

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _renderTimer?.Stop();
            _renderTimer?.Dispose();
            _canvasGraphics?.Dispose();
            _canvasBitmap?.Dispose();

            base.OnFormClosing(e);
        }
    }
}