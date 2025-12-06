using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TrafficSimulation.Core.Models;
using TrafficSimulation.Core.Services;

namespace TrafficSimulation.UI.Controls
{
    public class SimulationControl : UserControl
    {
        private readonly ITrafficSimulationService _simulationService;
        private SimulationSession _currentSession;
        private RoadNetwork _currentNetwork;
        private Bitmap _networkBitmap;
        private Graphics _graphics;
        private System.Windows.Forms.Timer _renderTimer;
        private Point _lastMousePosition;
        private float _zoom = 1.0f;
        private PointF _panOffset = PointF.Empty;
        private bool _isPanning = false;
        private Vertex _selectedVertex;
        private RoadSegment _selectedEdge;

        // Настройки визуализации
        private readonly Dictionary<RoadType, Color> _roadColors = new Dictionary<RoadType, Color>
        {
            [RoadType.Highway] = Color.FromArgb(70, 130, 180),
            [RoadType.Urban] = Color.FromArgb(105, 105, 105),
            // Удалите эти строки:
            // [RoadType.Rural] = Color.FromArgb(34, 139, 34),
            // [RoadType.Pedestrian] = Color.FromArgb(218, 165, 32)
        };

        private readonly Dictionary<VehicleType, Color> _vehicleColors = new Dictionary<VehicleType, Color>
        {
            [VehicleType.Car] = Color.Blue,
            [VehicleType.NoviceCar] = Color.Cyan,
            [VehicleType.Bus] = Color.Green,
            [VehicleType.Truck] = Color.Brown,
            [VehicleType.Special] = Color.Red,
            [VehicleType.Bicycle] = Color.Orange
        };

        public event EventHandler<VertexSelectedEventArgs> VertexSelected;
        public event EventHandler<EdgeSelectedEventArgs> EdgeSelected;

        public SimulationControl(ITrafficSimulationService simulationService)
        {
            _simulationService = simulationService;
            InitializeComponent();
            SetupRendering();
        }

        private void InitializeComponent()
        {
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
            this.Cursor = Cursors.Cross;

            // Обработка событий мыши
            this.MouseDown += OnMouseDown;
            this.MouseMove += OnMouseMove;
            this.MouseUp += OnMouseUp;
            this.MouseWheel += OnMouseWheel;
            this.Paint += OnPaint;
            this.Resize += OnResize;
        }

        private void SetupRendering()
        {
            _renderTimer = new System.Windows.Forms.Timer { Interval = 50 };
            _renderTimer.Tick += (s, e) => this.Invalidate();
            _renderTimer.Start();
        }

        public void SetNetwork(RoadNetwork network)
        {
            _currentNetwork = network;
            CenterView();
            this.Invalidate();
        }

        public void SetSession(SimulationSession session)
        {
            _currentSession = session;
        }

        private void CenterView()
        {
            if (_currentNetwork == null || !_currentNetwork.Vertices.Any())
                return;

            var minX = _currentNetwork.Vertices.Min(v => v.X);
            var maxX = _currentNetwork.Vertices.Max(v => v.X);
            var minY = _currentNetwork.Vertices.Min(v => v.Y);
            var maxY = _currentNetwork.Vertices.Max(v => v.Y);

            var centerX = (minX + maxX) / 2;
            var centerY = (minY + maxY) / 2;

            var scaleX = this.Width / (maxX - minX + 100);
            var scaleY = this.Height / (maxY - minY + 100);
            _zoom = Math.Min(scaleX, scaleY) * 0.9f;

            _panOffset = new PointF(
                this.Width / 2 - centerX * _zoom,
                this.Height / 2 - centerY * _zoom
            );
        }

        private PointF WorldToScreen(float worldX, float worldY)
        {
            return new PointF(
                worldX * _zoom + _panOffset.X,
                worldY * _zoom + _panOffset.Y
            );
        }

        private PointF ScreenToWorld(float screenX, float screenY)
        {
            return new PointF(
                (screenX - _panOffset.X) / _zoom,
                (screenY - _panOffset.Y) / _zoom
            );
        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            if (_currentNetwork == null)
            {
                e.Graphics.Clear(Color.White);
                e.Graphics.DrawString("Нет дорожной сети",
                    new Font("Arial", 16),
                    Brushes.Gray,
                    new PointF(this.Width / 2 - 100, this.Height / 2 - 20));
                return;
            }

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(Color.White);

            // Рисуем ребра
            foreach (var edge in _currentNetwork.Edges)
            {
                DrawEdge(e.Graphics, edge);
            }

            // Рисуем вершины
            foreach (var vertex in _currentNetwork.Vertices)
            {
                DrawVertex(e.Graphics, vertex);
            }

            // Рисуем транспортные средства
            if (_currentSession != null)
            {
                foreach (var vehicle in _currentSession.Vehicles)
                {
                    DrawVehicle(e.Graphics, vehicle);
                }
            }

            // Рисуем сетку
            DrawGrid(e.Graphics);

            // Рисуем масштаб
            DrawScale(e.Graphics);
        }

        private void DrawGrid(Graphics g)
        {
            var gridSize = 100;
            var scaledGridSize = gridSize * _zoom;

            if (scaledGridSize < 10) return;

            using (var gridPen = new Pen(Color.FromArgb(30, Color.Gray), 1))
            {
                // Вертикальные линии
                for (float x = 0; x < this.Width; x += scaledGridSize)
                {
                    g.DrawLine(gridPen, x, 0, x, this.Height);
                }

                // Горизонтальные линии
                for (float y = 0; y < this.Height; y += scaledGridSize)
                {
                    g.DrawLine(gridPen, 0, y, this.Width, y);
                }
            }
        }

        private void DrawScale(Graphics g)
        {
            var scaleLength = 100; // метров в реальном мире
            var screenLength = scaleLength * _zoom;

            var scaleRect = new RectangleF(10, this.Height - 40, screenLength, 20);
            g.FillRectangle(Brushes.White, scaleRect);
            g.DrawRectangle(Pens.Black, scaleRect.X, scaleRect.Y, scaleRect.Width, scaleRect.Height);

            g.DrawString($"{scaleLength} м",
                new Font("Arial", 8),
                Brushes.Black,
                scaleRect.X + scaleRect.Width / 2 - 20,
                scaleRect.Y - 15);
        }

        private void DrawEdge(Graphics g, RoadSegment edge)
        {
            var startVertex = _currentNetwork.Vertices.First(v => v.Id == edge.StartVertexId);
            var endVertex = _currentNetwork.Vertices.First(v => v.Id == edge.EndVertexId);

            var startPoint = WorldToScreen(startVertex.X, startVertex.Y);
            var endPoint = WorldToScreen(endVertex.X, endVertex.Y);

            // Цвет в зависимости от загруженности
            Color baseColor = _roadColors[edge.Type];
            Color edgeColor;

            if (edge.IsBlocked)
            {
                edgeColor = Color.Purple;
            }
            else
            {
                var congestion = edge.CongestionLevel;
                if (congestion < 0.3)
                    edgeColor = Color.FromArgb((int)(baseColor.R * 0.8), (int)(baseColor.G * 1.2), (int)(baseColor.B * 0.8));
                else if (congestion < 0.7)
                    edgeColor = Color.FromArgb((int)(baseColor.R * 1.2), (int)(baseColor.G * 1.0), (int)(baseColor.B * 0.6));
                else
                    edgeColor = Color.FromArgb((int)(baseColor.R * 1.5), (int)(baseColor.G * 0.6), (int)(baseColor.B * 0.6));
            }

            // Ширина линии в зависимости от количества полос
            float lineWidth = Math.Max(2, edge.Lanes * 2 * _zoom);

            using (var pen = new Pen(edgeColor, lineWidth))
            {
                if (edge.Type == RoadType.Highway)
                    pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;

                if (edge == _selectedEdge)
                {
                    pen.Width = lineWidth + 2;
                    pen.Color = Color.Yellow;
                }

                g.DrawLine(pen, startPoint, endPoint);

                // Стрелка направления
                if (!edge.IsBidirectional)
                {
                    DrawArrow(g, pen, startPoint, endPoint);
                }
            }

            // Подпись дороги
            var midPoint = new PointF((startPoint.X + endPoint.X) / 2, (startPoint.Y + endPoint.Y) / 2);
            var label = $"{edge.Name}\n{edge.MaxSpeed} км/ч\n{edge.CongestionLevel * 100:F0}%";

            using (var format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;

                var labelSize = g.MeasureString(label, new Font("Arial", 8 * _zoom));
                var labelRect = new RectangleF(midPoint.X - labelSize.Width / 2,
                    midPoint.Y - labelSize.Height / 2,
                    labelSize.Width,
                    labelSize.Height);

                g.FillRectangle(Brushes.White, labelRect);
                g.DrawRectangle(Pens.Black, labelRect.X, labelRect.Y, labelRect.Width, labelRect.Height);
                g.DrawString(label, new Font("Arial", 8 * _zoom), Brushes.Black, labelRect, format);
            }
        }

        private void DrawArrow(Graphics g, Pen pen, PointF from, PointF to)
        {
            var direction = new PointF(to.X - from.X, to.Y - from.Y);
            var length = (float)Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);

            if (length == 0) return;

            // Нормализуем вектор направления
            direction = new PointF(direction.X / length, direction.Y / length);

            // Перпендикулярный вектор
            var perpendicular = new PointF(-direction.Y, direction.X);

            var arrowSize = 10 * _zoom;
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

        private void DrawVertex(Graphics g, Vertex vertex)
        {
            var center = WorldToScreen(vertex.X, vertex.Y);
            float radius = 5 * _zoom;

            Color vertexColor;
            if (vertex.HasTrafficLights)
            {
                vertexColor = vertex.TrafficLightPhase switch
                {
                    0 => Color.Red,      // Красный
                    1 => Color.Yellow,   // Желтый
                    2 => Color.Green,    // Зеленый
                    _ => Color.Gray
                };
            }
            else
            {
                vertexColor = vertex.Type == VertexType.Intersection ? Color.Black : Color.DarkGray;
            }

            if (vertex == _selectedVertex)
            {
                radius = 8 * _zoom;
                vertexColor = Color.Yellow;
            }

            using (var brush = new SolidBrush(vertexColor))
            {
                g.FillEllipse(brush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
            }

            g.DrawEllipse(Pens.Black, center.X - radius, center.Y - radius, radius * 2, radius * 2);

            // Подпись вершины
            var label = $"{vertex.Name}\nID: {vertex.CityId}";
            g.DrawString(label,
                new Font("Arial", 7 * _zoom),
                Brushes.Black,
                center.X + radius + 2,
                center.Y - radius);
        }

        private void DrawVehicle(Graphics g, Vehicle vehicle)
        {
            var edge = _currentNetwork.Edges.FirstOrDefault(e => e.Id == vehicle.CurrentEdgeId);
            if (edge == null) return;

            var startVertex = _currentNetwork.Vertices.First(v => v.Id == edge.StartVertexId);
            var endVertex = _currentNetwork.Vertices.First(v => v.Id == edge.EndVertexId);

            var progress = vehicle.Position.Offset / edge.Length;
            double x = startVertex.X + (endVertex.X - startVertex.X) * progress;
            double y = startVertex.Y + (endVertex.Y - startVertex.Y) * progress;

            var screenPoint = WorldToScreen((float)x, (float)y);
            float size = vehicle.Type == VehicleType.Bicycle ? 3 * _zoom : 5 * _zoom;

            Color vehicleColor = _vehicleColors.ContainsKey(vehicle.Type) ?
                _vehicleColors[vehicle.Type] : Color.Black;

            using (var brush = new SolidBrush(vehicleColor))
            {
                g.FillEllipse(brush, screenPoint.X - size, screenPoint.Y - size, size * 2, size * 2);
            }

            g.DrawEllipse(Pens.Black, screenPoint.X - size, screenPoint.Y - size, size * 2, size * 2);

            // Скорость (цветная обводка)
            var speedRatio = vehicle.CurrentSpeed / 100.0f; // 100 км/ч - максимальная
            var speedColor = speedRatio < 0.3 ? Color.Red :
                speedRatio < 0.6 ? Color.Yellow : Color.Green;

            using (var pen = new Pen(speedColor, 2 * _zoom))
            {
                g.DrawEllipse(pen, screenPoint.X - size, screenPoint.Y - size, size * 2, size * 2);
            }
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // Выбор элементов
                var worldPoint = ScreenToWorld(e.X, e.Y);
                SelectElementAtPoint(worldPoint);
            }
            else if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right)
            {
                // Панорамирование
                _lastMousePosition = e.Location;
                _isPanning = true;
                this.Cursor = Cursors.SizeAll;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                var dx = e.X - _lastMousePosition.X;
                var dy = e.Y - _lastMousePosition.Y;

                _panOffset.X += dx;
                _panOffset.Y += dy;

                _lastMousePosition = e.Location;
                this.Invalidate();
            }
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                this.Cursor = Cursors.Cross;
            }
        }

        private void OnMouseWheel(object sender, MouseEventArgs e)
        {
            var zoomFactor = 1.1f;
            var oldZoom = _zoom;

            if (e.Delta > 0)
            {
                _zoom *= zoomFactor;
            }
            else
            {
                _zoom /= zoomFactor;
            }

            // Ограничение масштаба
            _zoom = Math.Max(0.1f, Math.Min(10f, _zoom));

            // Корректировка смещения для сохранения позиции под курсором
            var worldPosBefore = ScreenToWorld(e.X, e.Y);
            var zoomRatio = _zoom / oldZoom;
            _panOffset.X = e.X - worldPosBefore.X * _zoom;
            _panOffset.Y = e.Y - worldPosBefore.Y * _zoom;

            this.Invalidate();
        }

        private void OnResize(object sender, EventArgs e)
        {
            this.Invalidate();
        }

        private void SelectElementAtPoint(PointF worldPoint)
        {
            float selectionRadius = 10 / _zoom;

            // Сначала проверяем вершины
            foreach (var vertex in _currentNetwork.Vertices)
            {
                var distance = Math.Sqrt(
                    Math.Pow(vertex.X - worldPoint.X, 2) +
                    Math.Pow(vertex.Y - worldPoint.Y, 2));

                if (distance < selectionRadius)
                {
                    _selectedVertex = vertex;
                    _selectedEdge = null;
                    VertexSelected?.Invoke(this, new VertexSelectedEventArgs(vertex));
                    this.Invalidate();
                    return;
                }
            }

            // Затем проверяем ребра
            foreach (var edge in _currentNetwork.Edges)
            {
                var startVertex = _currentNetwork.Vertices.First(v => v.Id == edge.StartVertexId);
                var endVertex = _currentNetwork.Vertices.First(v => v.Id == edge.EndVertexId);

                var distance = DistanceToLineSegment(
                    worldPoint,
                    new PointF(startVertex.X, startVertex.Y),
                    new PointF(endVertex.X, endVertex.Y));

                if (distance < selectionRadius)
                {
                    _selectedVertex = null;
                    _selectedEdge = edge;
                    EdgeSelected?.Invoke(this, new EdgeSelectedEventArgs(edge));
                    this.Invalidate();
                    return;
                }
            }

            // Если ничего не выбрано
            _selectedVertex = null;
            _selectedEdge = null;
            this.Invalidate();
        }

        private float DistanceToLineSegment(PointF point, PointF lineStart, PointF lineEnd)
        {
            var lineLength = Math.Sqrt(Math.Pow(lineEnd.X - lineStart.X, 2) + Math.Pow(lineEnd.Y - lineStart.Y, 2));
            if (lineLength == 0) return (float)Math.Sqrt(Math.Pow(point.X - lineStart.X, 2) + Math.Pow(point.Y - lineStart.Y, 2));

            var t = Math.Max(0, Math.Min(1,
                ((point.X - lineStart.X) * (lineEnd.X - lineStart.X) +
                 (point.Y - lineStart.Y) * (lineEnd.Y - lineStart.Y)) / (lineLength * lineLength)));

            var projection = new PointF(
                (float)(lineStart.X + t * (lineEnd.X - lineStart.X)),
                (float)(lineStart.Y + t * (lineEnd.Y - lineStart.Y)));

            return (float)Math.Sqrt(Math.Pow(point.X - projection.X, 2) + Math.Pow(point.Y - projection.Y, 2));
        }

        public void ZoomIn()
        {
            _zoom *= 1.2f;
            this.Invalidate();
        }

        public void ZoomOut()
        {
            _zoom /= 1.2f;
            this.Invalidate();
        }

        public void ResetView()
        {
            CenterView();
            this.Invalidate();
        }

        public void ExportToImage(string filePath)
        {
            using (var bitmap = new Bitmap(this.Width, this.Height))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.Clear(Color.White);

                // Повторяем отрисовку
                var paintArgs = new PaintEventArgs(graphics, new Rectangle(0, 0, this.Width, this.Height));
                OnPaint(this, paintArgs);

                bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _renderTimer?.Stop();
                _renderTimer?.Dispose();
                _networkBitmap?.Dispose();
                _graphics?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public class VertexSelectedEventArgs : EventArgs
    {
        public Vertex Vertex { get; }

        public VertexSelectedEventArgs(Vertex vertex)
        {
            Vertex = vertex;
        }
    }

    public class EdgeSelectedEventArgs : EventArgs
    {
        public RoadSegment Edge { get; }

        public EdgeSelectedEventArgs(RoadSegment edge)
        {
            Edge = edge;
        }
    }
}