using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using TrafficSimulation.Core.Models;
using TrafficSimulation.Core.Services;

namespace TrafficSimulation.UI.Forms
{
    public partial class GraphEditorForm : Form
    {
        private readonly IGraphEditorService _graphEditorService;
        private RoadNetwork _currentNetwork;
        private PictureBox _canvas;
        private Graphics _canvasGraphics;
        private Bitmap _canvasBitmap;
        private Panel _toolsPanel;
        private ListView _networkListView;

        // Режимы и состояния
        private EditorMode _currentMode = EditorMode.Select;
        private Vertex _selectedVertex;
        private RoadSegment _selectedEdge;
        private Vertex _firstVertexForEdge;
        private bool _isPanning = false;
        private Point _lastMousePosition;
        private float _zoom = 1.0f;
        private PointF _panOffset = PointF.Empty;
        private bool _showGrid = true;
        private bool _snapToGrid = true;

        // Цвета
        private readonly Color _vertexColor = Color.Black;
        private readonly Color _selectedVertexColor = Color.Yellow;
        private readonly Color _edgeColor = Color.Gray;
        private readonly Color _selectedEdgeColor = Color.Blue;
        private readonly Color _gridColor = Color.FromArgb(240, 240, 240);
        private readonly Color _highlightColor = Color.FromArgb(255, 200, 100);

        // Константы
        private const int GRID_SIZE = 50;
        private const float MIN_ZOOM = 0.1f;
        private const float MAX_ZOOM = 10.0f;
        private const float ZOOM_FACTOR = 1.2f;

        // UI элементы
        private ComboBox _vertexTypeCombo;
        private CheckBox _vertexTrafficLightsCheck;
        private NumericUpDown _vertexCityNumeric;
        private ComboBox _edgeTypeCombo;
        private NumericUpDown _edgeLanesNumeric;
        private NumericUpDown _edgeSpeedNumeric;
        private CheckBox _edgeCrosswalkCheck;
        private CheckBox _edgeBidirectionalCheck;
        private ToolStripStatusLabel _statusLabel;
        private ToolStripStatusLabel _coordLabel;
        private ToolStripStatusLabel _infoLabel;

        public GraphEditorForm(IGraphEditorService graphEditorService)
        {
            _graphEditorService = graphEditorService;
            InitializeComponent();
            SetupUI();
            LoadNetworksAsync();
        }

        private void InitializeComponent()
        {
            this.Text = "Редактор дорожной сети";
            this.Size = new Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterParent;
            this.KeyPreview = true;
            this.FormClosing += GraphEditorForm_FormClosing;
        }

        private void SetupUI()
        {
            // Основной сплит-контейнер
            var mainSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 250
            };

            // Левая панель - инструменты и список сетей
            var leftPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SystemColors.Control
            };

            SetupToolsPanel(leftPanel);
            SetupNetworkListPanel(leftPanel);

            // Правая панель - холст
            var rightPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };

            SetupCanvas(rightPanel);
            SetupStatusBar(rightPanel);

            mainSplitContainer.Panel1.Controls.Add(leftPanel);
            mainSplitContainer.Panel2.Controls.Add(rightPanel);

            this.Controls.Add(mainSplitContainer);

            // Обработка клавиш
            this.KeyDown += GraphEditorForm_KeyDown;
        }

        private void SetupToolsPanel(Panel container)
        {
            _toolsPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 300,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(5)
            };

            // Группа инструментов
            var toolsGroup = new GroupBox
            {
                Text = "Инструменты",
                Dock = DockStyle.Top,
                Height = 120,
                Font = new Font("Arial", 10, FontStyle.Bold)
            };

            var toolsFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = false
            };

            // Кнопки режимов
            var btnSelect = CreateToolButton("Выбор (S)", EditorMode.Select);
            var btnAddVertex = CreateToolButton("Добавить вершину (V)", EditorMode.AddVertex);
            var btnAddEdge = CreateToolButton("Добавить ребро (E)", EditorMode.AddEdge);
            var btnDelete = CreateToolButton("Удалить (Del)", EditorMode.Delete);

            toolsFlow.Controls.AddRange(new Control[] { btnSelect, btnAddVertex, btnAddEdge, btnDelete });
            toolsGroup.Controls.Add(toolsFlow);

            // Группа свойств
            var propsGroup = new GroupBox
            {
                Text = "Свойства",
                Dock = DockStyle.Fill,
                Font = new Font("Arial", 10, FontStyle.Bold),
                Margin = new Padding(0, 5, 0, 0)
            };

            var propsPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            SetupPropertiesPanel(propsPanel);
            propsGroup.Controls.Add(propsPanel);

            _toolsPanel.Controls.Add(toolsGroup);
            _toolsPanel.Controls.Add(propsGroup);
            container.Controls.Add(_toolsPanel);
        }

        private Button CreateToolButton(string text, EditorMode mode)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(220, 30),
                Margin = new Padding(5),
                Tag = mode,
                TextAlign = ContentAlignment.MiddleLeft
            };

            button.Click += (s, e) => SetEditorMode(mode);
            return button;
        }

        private void SetupPropertiesPanel(Panel panel)
        {
            var y = 10;

            // Свойства вершины
            AddLabel(panel, "Свойства вершины:", ref y, true);

            _vertexTypeCombo = AddPropertyCombo(panel, "Тип вершины:",
                new[] { "Перекресток", "Терминал" }, "vertex_type", ref y);

            _vertexTrafficLightsCheck = AddPropertyCheckbox(panel, "Светофоры:",
                "vertex_traffic_lights", ref y);

            _vertexCityNumeric = AddPropertyNumeric(panel, "Город:", 1, 10,
                "vertex_city", ref y);

            y += 20;

            // Свойства ребра
            AddLabel(panel, "Свойства ребра:", ref y, true);

            _edgeTypeCombo = AddPropertyCombo(panel, "Тип дороги:",
                new[] { "Городская", "Скоростная" }, "edge_type", ref y);

            _edgeLanesNumeric = AddPropertyNumeric(panel, "Полосы:", 1, 6,
                "edge_lanes", ref y);

            _edgeSpeedNumeric = AddPropertyNumeric(panel, "Макс. скорость:", 20, 120,
                "edge_speed", ref y);

            _edgeCrosswalkCheck = AddPropertyCheckbox(panel, "Пешеходный переход:",
                "edge_crosswalk", ref y);

            _edgeBidirectionalCheck = AddPropertyCheckbox(panel, "Двустороннее:",
                "edge_bidirectional", ref y);
        }

        private void SetupNetworkListPanel(Panel container)
        {
            var listPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Top = 300
            };

            var groupBox = new GroupBox
            {
                Text = "Дорожные сети",
                Dock = DockStyle.Fill,
                Font = new Font("Arial", 10, FontStyle.Bold)
            };

            _networkListView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                GridLines = true
            };

            _networkListView.Columns.AddRange(new[]
            {
                new ColumnHeader { Text = "Название", Width = 150 },
                new ColumnHeader { Text = "Вершины", Width = 60 },
                new ColumnHeader { Text = "Ребра", Width = 60 },
                new ColumnHeader { Text = "Дата создания", Width = 100 }
            });

            _networkListView.SelectedIndexChanged += NetworkListView_SelectedIndexChanged;
            _networkListView.MouseDoubleClick += NetworkListView_MouseDoubleClick;

            // Панель кнопок
            var buttonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(5)
            };

            var btnNew = new Button { Text = "Новая", Size = new Size(70, 30) };
            var btnDelete = new Button { Text = "Удалить", Size = new Size(70, 30) };
            var btnClone = new Button { Text = "Клонировать", Size = new Size(90, 30) };
            var btnLoad = new Button { Text = "Загрузить", Size = new Size(80, 30) };
            var btnRefresh = new Button { Text = "Обновить", Size = new Size(80, 30) };

            btnNew.Click += async (s, e) => await CreateNewNetworkAsync();
            btnDelete.Click += async (s, e) => await DeleteSelectedNetworkAsync();
            btnClone.Click += async (s, e) => await CloneSelectedNetworkAsync();
            btnLoad.Click += (s, e) => LoadSelectedNetworkAsync();
            btnRefresh.Click += (s, e) => LoadNetworksAsync();

            buttonsPanel.Controls.AddRange(new Control[] { btnNew, btnDelete, btnClone, btnLoad, btnRefresh });

            groupBox.Controls.Add(_networkListView);
            groupBox.Controls.Add(buttonsPanel);
            listPanel.Controls.Add(groupBox);
            container.Controls.Add(listPanel);
        }

        private void SetupCanvas(Panel container)
        {
            _canvas = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Cursor = Cursors.Cross
            };

            // События мыши
            _canvas.MouseDown += Canvas_MouseDown;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseUp += Canvas_MouseUp;
            _canvas.MouseWheel += Canvas_MouseWheel;
            _canvas.Paint += Canvas_Paint;
            _canvas.Resize += Canvas_Resize;

            // Панель инструментов холста
            var canvasToolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = SystemColors.Control
            };

            var btnZoomIn = new Button { Text = "+", Location = new Point(10, 5), Size = new Size(30, 30) };
            var btnZoomOut = new Button { Text = "-", Location = new Point(45, 5), Size = new Size(30, 30) };
            var btnFit = new Button { Text = "По размеру", Location = new Point(80, 5), Size = new Size(100, 30) };
            var btnGrid = new CheckBox { Text = "Сетка", Location = new Point(185, 10), AutoSize = true, Checked = _showGrid };
            var btnSnap = new CheckBox { Text = "Привязка", Location = new Point(250, 10), AutoSize = true, Checked = _snapToGrid };

            btnZoomIn.Click += (s, e) => ZoomIn();
            btnZoomOut.Click += (s, e) => ZoomOut();
            btnFit.Click += (s, e) => FitToView();
            btnGrid.CheckedChanged += (s, e) => { _showGrid = btnGrid.Checked; _canvas.Invalidate(); };
            btnSnap.CheckedChanged += (s, e) => { _snapToGrid = btnSnap.Checked; };

            canvasToolbar.Controls.AddRange(new Control[] { btnZoomIn, btnZoomOut, btnFit, btnGrid, btnSnap });

            container.Controls.Add(_canvas);
            container.Controls.Add(canvasToolbar);
        }

        private void SetupStatusBar(Panel container)
        {
            var statusBar = new StatusStrip
            {
                Dock = DockStyle.Bottom
            };

            _statusLabel = new ToolStripStatusLabel("Режим: Выбор");
            _coordLabel = new ToolStripStatusLabel("Координаты: 0, 0");
            _infoLabel = new ToolStripStatusLabel("Готов");

            statusBar.Items.AddRange(new ToolStripItem[] { _statusLabel, _coordLabel, _infoLabel });
            container.Controls.Add(statusBar);
        }

        #region Сетка и координаты

        private PointF ScreenToWorld(Point screenPoint)
        {
            return new PointF(
                (screenPoint.X - _panOffset.X) / _zoom,
                (screenPoint.Y - _panOffset.Y) / _zoom
            );
        }

        private PointF WorldToScreen(PointF worldPoint)
        {
            return new PointF(
                worldPoint.X * _zoom + _panOffset.X,
                worldPoint.Y * _zoom + _panOffset.Y
            );
        }

        private Point SnapToGrid(Point worldPoint)
        {
            if (!_snapToGrid) return worldPoint;

            int gridSize = GRID_SIZE;
            int x = ((worldPoint.X + gridSize / 2) / gridSize) * gridSize;
            int y = ((worldPoint.Y + gridSize / 2) / gridSize) * gridSize;
            return new Point(x, y);
        }

        #endregion

        #region Рисование

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            if (_canvasBitmap == null || _canvasBitmap.Width != _canvas.Width || _canvasBitmap.Height != _canvas.Height)
            {
                _canvasBitmap?.Dispose();
                _canvasBitmap = new Bitmap(_canvas.Width, _canvas.Height);
                _canvasGraphics?.Dispose();
                _canvasGraphics = Graphics.FromImage(_canvasBitmap);
            }

            _canvasGraphics.Clear(Color.White);

            // Рисуем сетку
            if (_showGrid)
                DrawGrid(_canvasGraphics);

            // Рисуем дорожную сеть
            if (_currentNetwork != null)
            {
                DrawEdges(_canvasGraphics);
                DrawVertices(_canvasGraphics);
            }

            // Копируем на холст
            e.Graphics.DrawImage(_canvasBitmap, 0, 0);

            // Рисуем временную линию при создании ребра
            if (_currentMode == EditorMode.AddEdge && _firstVertexForEdge != null)
            {
                var startPoint = WorldToScreen(new PointF(_firstVertexForEdge.X, _firstVertexForEdge.Y));
                var currentPos = _canvas.PointToClient(Cursor.Position);
                e.Graphics.DrawLine(Pens.DarkGray, startPoint, currentPos);
            }
        }

        private void DrawGrid(Graphics g)
        {
            var scaledGridSize = GRID_SIZE * _zoom;
            if (scaledGridSize < 5) return;

            using (var gridPen = new Pen(_gridColor))
            {
                // Вертикальные линии
                for (float x = _panOffset.X % scaledGridSize; x < _canvas.Width; x += scaledGridSize)
                {
                    g.DrawLine(gridPen, x, 0, x, _canvas.Height);
                }

                // Горизонтальные линии
                for (float y = _panOffset.Y % scaledGridSize; y < _canvas.Height; y += scaledGridSize)
                {
                    g.DrawLine(gridPen, 0, y, _canvas.Width, y);
                }
            }
        }

        private void DrawVertices(Graphics g)
        {
            if (_currentNetwork?.Vertices == null) return;

            foreach (var vertex in _currentNetwork.Vertices)
            {
                var center = WorldToScreen(new PointF(vertex.X, vertex.Y));
                var radius = 6 * _zoom;

                // Определяем цвет вершины
                Color fillColor;
                if (vertex == _selectedVertex)
                    fillColor = _selectedVertexColor;
                else if (vertex.HasTrafficLights)
                    fillColor = Color.Red;
                else
                    fillColor = _vertexColor;

                // Рисуем вершину
                using (var brush = new SolidBrush(fillColor))
                {
                    g.FillEllipse(brush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
                }

                g.DrawEllipse(Pens.Black, center.X - radius, center.Y - radius, radius * 2, radius * 2);

                // Подпись
                var label = $"{vertex.Name} (C{vertex.CityId})";
                var fontSize = Math.Max(8, 8 * _zoom);
                using (var font = new Font("Arial", fontSize))
                {
                    var size = g.MeasureString(label, font);
                    g.DrawString(label, font, Brushes.Black,
                        center.X + radius + 2, center.Y - size.Height / 2);
                }
            }
        }

        private void DrawEdges(Graphics g)
        {
            if (_currentNetwork?.Edges == null) return;

            foreach (var edge in _currentNetwork.Edges)
            {
                var startVertex = _currentNetwork.Vertices.FirstOrDefault(v => v.Id == edge.StartVertexId);
                var endVertex = _currentNetwork.Vertices.FirstOrDefault(v => v.Id == edge.EndVertexId);

                if (startVertex == null || endVertex == null) continue;

                var startPoint = WorldToScreen(new PointF(startVertex.X, startVertex.Y));
                var endPoint = WorldToScreen(new PointF(endVertex.X, endVertex.Y));

                // Определяем цвет ребра
                Color edgeColor;
                if (edge == _selectedEdge)
                    edgeColor = _selectedEdgeColor;
                else if (edge.Type == RoadType.Highway)
                    edgeColor = Color.DarkBlue;
                else
                    edgeColor = _edgeColor;

                // Определяем ширину линии
                var lineWidth = Math.Max(1, edge.Lanes * 2 * _zoom);

                using (var pen = new Pen(edgeColor, lineWidth))
                {
                    if (edge.Type == RoadType.Highway)
                        pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;

                    g.DrawLine(pen, startPoint, endPoint);

                    // Стрелка направления для односторонних дорог
                    if (!edge.IsBidirectional)
                    {
                        DrawArrow(g, pen, startPoint, endPoint);
                    }
                }

                // Подпись ребра (только если достаточно увеличения)
                if (_zoom > 0.5)
                {
                    var midPoint = new PointF((startPoint.X + endPoint.X) / 2, (startPoint.Y + endPoint.Y) / 2);
                    var label = $"{edge.Name}\n{edge.MaxSpeed} км/ч";

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
                        g.DrawRectangle(Pens.Black, rect.X, rect.Y, rect.Width, rect.Height);
                        g.DrawString(label, font, Brushes.Black, rect);
                    }
                }
            }
        }

        private void DrawArrow(Graphics g, Pen pen, PointF from, PointF to)
        {
            var direction = new PointF(to.X - from.X, to.Y - from.Y);
            var length = (float)Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);

            if (length == 0) return;

            direction = new PointF(direction.X / length, direction.Y / length);
            var perpendicular = new PointF(-direction.Y, direction.X);

            var arrowSize = 8 * _zoom;
            var arrowBack = new PointF(to.X - direction.X * arrowSize, to.Y - direction.Y * arrowSize);

            var arrowPoints = new PointF[]
            {
                to,
                new PointF(arrowBack.X + perpendicular.X * arrowSize / 2,
                          arrowBack.Y + perpendicular.Y * arrowSize / 2),
                new PointF(arrowBack.X - perpendicular.X * arrowSize / 2,
                          arrowBack.Y - perpendicular.Y * arrowSize / 2)
            };

            g.FillPolygon(pen.Brush, arrowPoints);
        }

        #endregion

        #region Обработка событий мыши

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            var worldPoint = SnapToGrid(Point.Truncate(ScreenToWorld(e.Location)));

            if (e.Button == MouseButtons.Left)
            {
                switch (_currentMode)
                {
                    case EditorMode.Select:
                        SelectElementAtPoint(worldPoint);
                        break;

                    case EditorMode.AddVertex:
                        AddVertexAtPoint(worldPoint);
                        break;

                    case EditorMode.AddEdge:
                        HandleEdgeCreation(worldPoint);
                        break;

                    case EditorMode.Delete:
                        DeleteElementAtPoint(worldPoint);
                        break;
                }
            }
            else if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right)
            {
                _isPanning = true;
                _lastMousePosition = e.Location;
                _canvas.Cursor = Cursors.SizeAll;
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            var worldPoint = ScreenToWorld(e.Location);
            _coordLabel.Text = $"Координаты: {worldPoint.X:F0}, {worldPoint.Y:F0}";

            if (_isPanning)
            {
                var dx = e.X - _lastMousePosition.X;
                var dy = e.Y - _lastMousePosition.Y;

                _panOffset.X += dx;
                _panOffset.Y += dy;

                _lastMousePosition = e.Location;
                _canvas.Invalidate();
            }
        }

        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                UpdateCursor();
            }
        }

        private void Canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            var oldZoom = _zoom;
            var zoomFactor = e.Delta > 0 ? ZOOM_FACTOR : 1 / ZOOM_FACTOR;

            _zoom *= zoomFactor;
            _zoom = Math.Max(MIN_ZOOM, Math.Min(MAX_ZOOM, _zoom));

            // Корректируем смещение для сохранения позиции под курсором
            var worldPos = ScreenToWorld(e.Location);
            var newScreenPos = WorldToScreen(worldPos);

            _panOffset.X += (e.Location.X - newScreenPos.X);
            _panOffset.Y += (e.Location.Y - newScreenPos.Y);

            _canvas.Invalidate();
        }

        private void Canvas_Resize(object sender, EventArgs e)
        {
            _canvas.Invalidate();
        }

        #endregion

        #region Режимы редактора

        private void SetEditorMode(EditorMode mode)
        {
            _currentMode = mode;
            _firstVertexForEdge = null;

            UpdateCursor();
            UpdateStatusBar();
            UpdatePropertiesPanel();
        }

        private void UpdateCursor()
        {
            if (_isPanning)
            {
                _canvas.Cursor = Cursors.SizeAll;
                return;
            }

            _canvas.Cursor = _currentMode switch
            {
                EditorMode.Select => Cursors.Cross,
                EditorMode.AddVertex => Cursors.Hand,
                EditorMode.AddEdge => Cursors.UpArrow,
                EditorMode.Delete => Cursors.No,
                _ => Cursors.Default
            };
        }

        private void UpdateStatusBar()
        {
            var modeName = _currentMode switch
            {
                EditorMode.Select => "Выбор",
                EditorMode.AddVertex => "Добавление вершины",
                EditorMode.AddEdge => "Добавление ребра",
                EditorMode.Delete => "Удаление",
                _ => "Неизвестно"
            };

            _statusLabel.Text = $"Режим: {modeName}";

            if (_currentMode == EditorMode.AddEdge && _firstVertexForEdge != null)
            {
                _infoLabel.Text = $"Выбрана вершина: {_firstVertexForEdge.Name}. Выберите конечную вершину.";
            }
            else
            {
                _infoLabel.Text = _currentNetwork != null ?
                    $"Сеть: {_currentNetwork.Name} ({_currentNetwork.Vertices.Count} вершин, {_currentNetwork.Edges.Count} ребер)" :
                    "Нет активной сети";
            }
        }

        #endregion

        #region Операции с элементами графа

        private void SelectElementAtPoint(Point worldPoint)
        {
            float selectionRadius = 10 / _zoom;

            // Проверяем вершины
            if (_currentNetwork?.Vertices != null)
            {
                foreach (var vertex in _currentNetwork.Vertices)
                {
                    var distance = Math.Sqrt(
                        Math.Pow(vertex.X - worldPoint.X, 2) +
                        Math.Pow(vertex.Y - worldPoint.Y, 2));

                    if (distance < selectionRadius)
                    {
                        _selectedVertex = vertex;
                        _selectedEdge = null;
                        UpdatePropertiesPanel();
                        _canvas.Invalidate();
                        return;
                    }
                }
            }

            // Проверяем ребра
            if (_currentNetwork?.Edges != null)
            {
                foreach (var edge in _currentNetwork.Edges)
                {
                    var startVertex = _currentNetwork.Vertices.FirstOrDefault(v => v.Id == edge.StartVertexId);
                    var endVertex = _currentNetwork.Vertices.FirstOrDefault(v => v.Id == edge.EndVertexId);

                    if (startVertex == null || endVertex == null) continue;

                    var distance = DistanceToLineSegment(
                        worldPoint,
                        new Point(startVertex.X, startVertex.Y),
                        new Point(endVertex.X, endVertex.Y));

                    if (distance < selectionRadius)
                    {
                        _selectedVertex = null;
                        _selectedEdge = edge;
                        UpdatePropertiesPanel();
                        _canvas.Invalidate();
                        return;
                    }
                }
            }

            // Если ничего не выбрано
            _selectedVertex = null;
            _selectedEdge = null;
            ClearPropertiesPanel();
            _canvas.Invalidate();
        }

        private async void AddVertexAtPoint(Point worldPoint)
        {
            if (_currentNetwork == null) return;

            try
            {
                var vertexType = _vertexTypeCombo.SelectedItem.ToString() == "Перекресток" ?
                    VertexType.Intersection : VertexType.Terminal;
                var cityId = (int)_vertexCityNumeric.Value;
                var hasTrafficLights = _vertexTrafficLightsCheck.Checked;

                var vertex = await _graphEditorService.AddVertexAsync(
                    _currentNetwork.Id,
                    worldPoint.X,
                    worldPoint.Y,
                    vertexType,
                    cityId,
                    hasTrafficLights);

                _currentNetwork.Vertices.Add(vertex);
                _canvas.Invalidate();
                UpdateStatusBar();
                _infoLabel.Text = $"Добавлена вершина: {vertex.Name}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка добавления вершины: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void HandleEdgeCreation(Point worldPoint)
        {
            if (_currentNetwork?.Vertices == null) return;

            float selectionRadius = 10 / _zoom;
            var vertex = _currentNetwork.Vertices.FirstOrDefault(v =>
                Math.Sqrt(Math.Pow(v.X - worldPoint.X, 2) + Math.Pow(v.Y - worldPoint.Y, 2)) < selectionRadius);

            if (vertex != null)
            {
                if (_firstVertexForEdge == null)
                {
                    _firstVertexForEdge = vertex;
                    UpdateStatusBar();
                    _canvas.Invalidate();
                }
                else if (_firstVertexForEdge != vertex)
                {
                    AddEdgeBetweenVerticesAsync(_firstVertexForEdge, vertex);
                    _firstVertexForEdge = null;
                    UpdateStatusBar();
                }
            }
        }

        private async void AddEdgeBetweenVerticesAsync(Vertex startVertex, Vertex endVertex)
        {
            if (_currentNetwork == null) return;

            try
            {
                var roadType = _edgeTypeCombo.SelectedItem.ToString() == "Городская" ?
                    RoadType.Urban : RoadType.Highway;
                var lanes = (int)_edgeLanesNumeric.Value;
                var maxSpeed = (int)_edgeSpeedNumeric.Value;
                var hasCrosswalk = _edgeCrosswalkCheck.Checked;
                var isBidirectional = _edgeBidirectionalCheck.Checked;

                var edge = await _graphEditorService.AddEdgeAsync(
                    _currentNetwork.Id,
                    startVertex.Id,
                    endVertex.Id,
                    roadType,
                    0, // Длина будет вычислена автоматически
                    lanes,
                    maxSpeed,
                    hasCrosswalk,
                    isBidirectional);

                _currentNetwork.Edges.Add(edge);
                _canvas.Invalidate();
                _infoLabel.Text = $"Добавлено ребро: {edge.Name}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка добавления ребра: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void DeleteElementAtPoint(Point worldPoint)
        {
            if (_currentNetwork == null) return;

            float selectionRadius = 10 / _zoom;

            // Проверяем вершины
            var vertex = _currentNetwork.Vertices.FirstOrDefault(v =>
                Math.Sqrt(Math.Pow(v.X - worldPoint.X, 2) + Math.Pow(v.Y - worldPoint.Y, 2)) < selectionRadius);

            if (vertex != null)
            {
                var result = MessageBox.Show($"Удалить вершину '{vertex.Name}'?", "Подтверждение",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    try
                    {
                        await _graphEditorService.RemoveVertexAsync(_currentNetwork.Id, vertex.Id);
                        _currentNetwork.Vertices.Remove(vertex);
                        _canvas.Invalidate();
                        _infoLabel.Text = $"Удалена вершина: {vertex.Name}";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка удаления вершины: {ex.Message}", "Ошибка",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                return;
            }

            // Проверяем ребра
            if (_currentNetwork.Edges != null)
            {
                foreach (var edge in _currentNetwork.Edges)
                {
                    var startVertex = _currentNetwork.Vertices.First(v => v.Id == edge.StartVertexId);
                    var endVertex = _currentNetwork.Vertices.First(v => v.Id == edge.EndVertexId);

                    var distance = DistanceToLineSegment(
                        worldPoint,
                        new Point(startVertex.X, startVertex.Y),
                        new Point(endVertex.X, endVertex.Y));

                    if (distance < selectionRadius)
                    {
                        var result = MessageBox.Show($"Удалить ребро '{edge.Name}'?", "Подтверждение",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                        if (result == DialogResult.Yes)
                        {
                            try
                            {
                                await _graphEditorService.RemoveEdgeAsync(_currentNetwork.Id, edge.Id);
                                _currentNetwork.Edges.Remove(edge);
                                _canvas.Invalidate();
                                _infoLabel.Text = $"Удалено ребро: {edge.Name}";
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Ошибка удаления ребра: {ex.Message}", "Ошибка",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                        return;
                    }
                }
            }
        }

        private float DistanceToLineSegment(Point point, Point lineStart, Point lineEnd)
        {
            var lineLength = Math.Sqrt(Math.Pow(lineEnd.X - lineStart.X, 2) + Math.Pow(lineEnd.Y - lineStart.Y, 2));
            if (lineLength == 0)
                return (float)Math.Sqrt(Math.Pow(point.X - lineStart.X, 2) + Math.Pow(point.Y - lineStart.Y, 2));

            var t = Math.Max(0, Math.Min(1,
                ((point.X - lineStart.X) * (lineEnd.X - lineStart.X) +
                 (point.Y - lineStart.Y) * (lineEnd.Y - lineStart.Y)) / (lineLength * lineLength)));

            var projection = new Point(
                (int)(lineStart.X + t * (lineEnd.X - lineStart.X)),
                (int)(lineStart.Y + t * (lineEnd.Y - lineStart.Y)));

            return (float)Math.Sqrt(Math.Pow(point.X - projection.X, 2) + Math.Pow(point.Y - projection.Y, 2));
        }

        #endregion

        #region Управление сетями

        private async void LoadNetworksAsync()
        {
            try
            {
                _networkListView.Items.Clear();
                var networks = await _graphEditorService.GetAllNetworksAsync();

                foreach (var network in networks)
                {
                    var item = new ListViewItem(network.Name);
                    item.SubItems.Add(network.Vertices.Count.ToString());
                    item.SubItems.Add(network.Edges.Count.ToString());
                    item.SubItems.Add(network.CreatedDate.ToString("dd.MM.yyyy"));
                    item.Tag = network.Id;
                    _networkListView.Items.Add(item);
                }

                _infoLabel.Text = $"Загружено {networks.Count()} сетей";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки сетей: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task CreateNewNetworkAsync()
        {
            var name = Prompt.ShowDialog("Введите название сети:", "Новая дорожная сеть");
            if (!string.IsNullOrEmpty(name))
            {
                try
                {
                    _currentNetwork = await _graphEditorService.CreateNetworkAsync(name);
                    _canvas.Invalidate();
                    LoadNetworksAsync();
                    _infoLabel.Text = $"Создана новая сеть: {name}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка создания сети: {ex.Message}", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private async Task DeleteSelectedNetworkAsync()
        {
            if (_networkListView.SelectedItems.Count == 0) return;

            var item = _networkListView.SelectedItems[0];
            var networkId = (Guid)item.Tag;
            var networkName = item.Text;

            var result = MessageBox.Show($"Удалить сеть '{networkName}'?", "Подтверждение",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                try
                {
                    await _graphEditorService.DeleteNetworkAsync(networkId);

                    if (_currentNetwork?.Id == networkId)
                    {
                        _currentNetwork = null;
                        _canvas.Invalidate();
                    }

                    LoadNetworksAsync();
                    _infoLabel.Text = $"Сеть '{networkName}' удалена";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка удаления сети: {ex.Message}", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private async Task CloneSelectedNetworkAsync()
        {
            if (_networkListView.SelectedItems.Count == 0) return;

            var item = _networkListView.SelectedItems[0];
            var networkId = (Guid)item.Tag;
            var networkName = item.Text;

            var newName = Prompt.ShowDialog("Введите название для копии:", "Клонирование сети");
            if (!string.IsNullOrEmpty(newName))
            {
                try
                {
                    _currentNetwork = await _graphEditorService.CloneNetworkAsync(networkId, newName);
                    _canvas.Invalidate();
                    LoadNetworksAsync();
                    _infoLabel.Text = $"Сеть скопирована как: {newName}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка клонирования сети: {ex.Message}", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private async void LoadSelectedNetworkAsync()
        {
            if (_networkListView.SelectedItems.Count == 0) return;

            var item = _networkListView.SelectedItems[0];
            var networkId = (Guid)item.Tag;

            try
            {
                _currentNetwork = await _graphEditorService.LoadNetworkAsync(networkId);
                FitToView();
                UpdateStatusBar();
                _infoLabel.Text = $"Загружена сеть: {_currentNetwork.Name}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки сети: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void NetworkListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_networkListView.SelectedItems.Count > 0)
            {
                var item = _networkListView.SelectedItems[0];
                _infoLabel.Text = $"Выбрана сеть: {item.Text} ({item.SubItems[1].Text} вершин, {item.SubItems[2].Text} ребер)";
            }
        }

        private void NetworkListView_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            LoadSelectedNetworkAsync();
        }

        #endregion

        #region Панель свойств

        private void UpdatePropertiesPanel()
        {
            if (_selectedVertex != null)
            {
                UpdateVertexPropertiesPanel();
            }
            else if (_selectedEdge != null)
            {
                UpdateEdgePropertiesPanel();
            }
            else
            {
                ClearPropertiesPanel();
            }
        }

        private void UpdateVertexPropertiesPanel()
        {
            _vertexTypeCombo.SelectedItem = _selectedVertex.Type == VertexType.Intersection ?
                "Перекресток" : "Терминал";
            _vertexCityNumeric.Value = _selectedVertex.CityId;
            _vertexTrafficLightsCheck.Checked = _selectedVertex.HasTrafficLights;
        }

        private void UpdateEdgePropertiesPanel()
        {
            _edgeTypeCombo.SelectedItem = _selectedEdge.Type == RoadType.Urban ?
                "Городская" : "Скоростная";
            _edgeLanesNumeric.Value = _selectedEdge.Lanes;
            _edgeSpeedNumeric.Value = (int)_selectedEdge.MaxSpeed;
            _edgeCrosswalkCheck.Checked = _selectedEdge.HasCrosswalk;
            _edgeBidirectionalCheck.Checked = _selectedEdge.IsBidirectional;
        }

        private void ClearPropertiesPanel()
        {
            _vertexTypeCombo.SelectedIndex = 0;
            _vertexCityNumeric.Value = 1;
            _vertexTrafficLightsCheck.Checked = true;

            _edgeTypeCombo.SelectedIndex = 0;
            _edgeLanesNumeric.Value = 1;
            _edgeSpeedNumeric.Value = 50;
            _edgeCrosswalkCheck.Checked = false;
            _edgeBidirectionalCheck.Checked = true;
        }

        // Вспомогательные методы для создания элементов управления
        private Label AddLabel(Panel panel, string text, ref int y, bool bold = false)
        {
            var label = new Label
            {
                Text = text,
                Location = new Point(10, y),
                Font = bold ? new Font("Arial", 9, FontStyle.Bold) : new Font("Arial", 9),
                AutoSize = true
            };
            panel.Controls.Add(label);
            y += 25;
            return label;
        }

        private ComboBox AddPropertyCombo(Panel panel, string label, string[] items, string tag, ref int y)
        {
            var lbl = new Label
            {
                Text = label,
                Location = new Point(20, y),
                Size = new Size(120, 20)
            };
            panel.Controls.Add(lbl);

            var combo = new ComboBox
            {
                Location = new Point(140, y),
                Size = new Size(100, 20),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Tag = tag
            };
            combo.Items.AddRange(items);
            combo.SelectedIndex = 0;
            panel.Controls.Add(combo);

            y += 25;
            return combo;
        }

        private NumericUpDown AddPropertyNumeric(Panel panel, string label, int min, int max, string tag, ref int y)
        {
            var lbl = new Label
            {
                Text = label,
                Location = new Point(20, y),
                Size = new Size(120, 20)
            };
            panel.Controls.Add(lbl);

            var numeric = new NumericUpDown
            {
                Location = new Point(140, y),
                Size = new Size(100, 20),
                Minimum = min,
                Maximum = max,
                Tag = tag
            };
            panel.Controls.Add(numeric);

            y += 25;
            return numeric;
        }

        private CheckBox AddPropertyCheckbox(Panel panel, string label, string tag, ref int y)
        {
            var checkbox = new CheckBox
            {
                Text = label,
                Location = new Point(20, y),
                Size = new Size(200, 20),
                Tag = tag
            };
            panel.Controls.Add(checkbox);

            y += 25;
            return checkbox;
        }

        #endregion

        #region Управление видом

        private void ZoomIn()
        {
            var oldZoom = _zoom;
            _zoom *= ZOOM_FACTOR;
            _zoom = Math.Min(MAX_ZOOM, _zoom);
            AdjustPanOffset(oldZoom);
            _canvas.Invalidate();
        }

        private void ZoomOut()
        {
            var oldZoom = _zoom;
            _zoom /= ZOOM_FACTOR;
            _zoom = Math.Max(MIN_ZOOM, _zoom);
            AdjustPanOffset(oldZoom);
            _canvas.Invalidate();
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

            var scaleX = _canvas.Width / (float)width;
            var scaleY = _canvas.Height / (float)height;
            _zoom = Math.Min(scaleX, scaleY) * 0.9f;

            var centerX = (minX + maxX) / 2;
            var centerY = (minY + maxY) / 2;

            _panOffset = new PointF(
                _canvas.Width / 2 - centerX * _zoom,
                _canvas.Height / 2 - centerY * _zoom
            );

            _canvas.Invalidate();
        }

        private void AdjustPanOffset(float oldZoom)
        {
            if (oldZoom == 0) return;

            var centerX = _canvas.Width / 2;
            var centerY = _canvas.Height / 2;

            var worldCenterX = (centerX - _panOffset.X) / oldZoom;
            var worldCenterY = (centerY - _panOffset.Y) / oldZoom;

            _panOffset.X = centerX - worldCenterX * _zoom;
            _panOffset.Y = centerY - worldCenterY * _zoom;
        }

        #endregion

        #region Обработка клавиш

        private void GraphEditorForm_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.S:
                    SetEditorMode(EditorMode.Select);
                    break;

                case Keys.V:
                    SetEditorMode(EditorMode.AddVertex);
                    break;

                case Keys.E:
                    SetEditorMode(EditorMode.AddEdge);
                    break;

                case Keys.Delete:
                    if (_selectedVertex != null)
                    {
                        DeleteElementAtPoint(new Point(_selectedVertex.X, _selectedVertex.Y));
                    }
                    else if (_selectedEdge != null)
                    {
                        var startVertex = _currentNetwork.Vertices.First(v => v.Id == _selectedEdge.StartVertexId);
                        var endVertex = _currentNetwork.Vertices.First(v => v.Id == _selectedEdge.EndVertexId);
                        var midPoint = new Point(
                            (startVertex.X + endVertex.X) / 2,
                            (startVertex.Y + endVertex.Y) / 2);
                        DeleteElementAtPoint(midPoint);
                    }
                    break;

                case Keys.Escape:
                    _firstVertexForEdge = null;
                    _selectedVertex = null;
                    _selectedEdge = null;
                    SetEditorMode(EditorMode.Select);
                    break;

                case Keys.Add:
                case Keys.Oemplus:
                    ZoomIn();
                    break;

                case Keys.Subtract:
                case Keys.OemMinus:
                    ZoomOut();
                    break;

                case Keys.F:
                    FitToView();
                    break;

                case Keys.Space:
                    _isPanning = true;
                    UpdateCursor();
                    break;
            }
        }

        #endregion

        #region Очистка ресурсов

        private void GraphEditorForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _canvasGraphics?.Dispose();
            _canvasBitmap?.Dispose();
        }

        #endregion
    }

    // Вспомогательный класс для диалога ввода
    public static class Prompt
    {
        public static string ShowDialog(string text, string caption)
        {
            Form prompt = new Form()
            {
                Width = 300,
                Height = 150,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = caption,
                StartPosition = FormStartPosition.CenterScreen
            };

            Label textLabel = new Label() { Left = 20, Top = 20, Text = text, Width = 260 };
            TextBox textBox = new TextBox() { Left = 20, Top = 50, Width = 260 };
            Button confirmation = new Button() { Text = "OK", Left = 110, Width = 80, Top = 80, DialogResult = DialogResult.OK };

            confirmation.Click += (sender, e) => { prompt.Close(); };
            prompt.Controls.Add(textLabel);
            prompt.Controls.Add(textBox);
            prompt.Controls.Add(confirmation);
            prompt.AcceptButton = confirmation;

            return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : "";
        }
    }

    // Перечисление режимов редактора
    public enum EditorMode
    {
        Select,
        AddVertex,
        AddEdge,
        Delete
    }
}