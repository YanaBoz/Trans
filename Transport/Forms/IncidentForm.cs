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
    public partial class IncidentForm : Form
    {
        private readonly ITrafficSimulationService _simulationService;
        private SimulationSession _currentSession;
        private System.Windows.Forms.Timer _updateTimer;

        public IncidentForm(ITrafficSimulationService simulationService)
        {
            _simulationService = simulationService;
            InitializeComponent1();
        }

        private void InitializeComponent1()
        {
            this.Text = "Управление инцидентами";
            this.Size = new Size(1000, 700);
            this.StartPosition = FormStartPosition.CenterParent;

            var mainSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 400
            };

            // Верхняя панель - список инцидентов
            var incidentsListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9),
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 40
            };
            incidentsListBox.DrawItem += (s, e) => DrawIncidentItem(s, e);
            incidentsListBox.SelectedIndexChanged += (s, e) => OnIncidentSelected();
            mainSplitContainer.Panel1.Controls.Add(incidentsListBox);

            // Нижняя панель - детали инцидента и управление
            var detailsPanel = new Panel
            {
                Dock = DockStyle.Fill
            };

            var detailsTextBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Consolas", 10)
            };
            detailsPanel.Controls.Add(detailsTextBox);

            var controlPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                BackColor = SystemColors.Control
            };

            var btnResolve = new Button
            {
                Text = "Разрешить инцидент",
                Location = new Point(10, 10),
                Size = new Size(150, 30),
                Enabled = false
            };
            btnResolve.Click += async (s, e) => await ResolveSelectedIncident();

            var btnSimulate = new Button
            {
                Text = "Создать тестовый инцидент",
                Location = new Point(170, 10),
                Size = new Size(180, 30)
            };
            btnSimulate.Click += async (s, e) => await SimulateTestIncident();

            controlPanel.Controls.AddRange(new Control[] { btnResolve, btnSimulate });
            detailsPanel.Controls.Add(controlPanel);

            mainSplitContainer.Panel2.Controls.Add(detailsPanel);

            this.Controls.Add(mainSplitContainer);

            // Таймер обновления
            _updateTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _updateTimer.Tick += async (s, e) => await RefreshIncidents();
        }

        public void SetSession(SimulationSession session)
        {
            _currentSession = session;
            if (session != null)
            {
                _updateTimer.Start();
                RefreshIncidents().Wait();
            }
        }

        private void DrawIncidentItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= ((ListBox)sender).Items.Count)
                return;

            var incident = (TrafficIncident)((ListBox)sender).Items[e.Index];
            e.DrawBackground();

            Color textColor = e.ForeColor;
            Color backColor = e.BackColor;

            // Цвет в зависимости от серьезности
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

                // Рисуем тип инцидента
                var typeRect = new Rectangle(e.Bounds.X + 5, e.Bounds.Y + 2, e.Bounds.Width - 10, 15);
                e.Graphics.DrawString(incident.Type.ToString(), boldFont, brush, typeRect);

                // Рисуем время
                var timeRect = new Rectangle(e.Bounds.X + 5, e.Bounds.Y + 18, e.Bounds.Width - 10, 15);
                e.Graphics.DrawString(incident.Time.ToString("HH:mm:ss"), e.Font, brush, timeRect);
            }

            e.DrawFocusRectangle();
        }

        private async Task RefreshIncidents()
        {
            if (_currentSession == null) return;

            try
            {
                var incidents = await _simulationService.GetIncidentsAsync(_currentSession.Id);
                UpdateIncidentsList(incidents);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error refreshing incidents: {ex.Message}");
            }
        }

        private void UpdateIncidentsList(IEnumerable<TrafficIncident> incidents)
        {
            var listBox = Controls.Find("", true).OfType<ListBox>().FirstOrDefault();
            if (listBox != null)
            {
                var selectedIndex = listBox.SelectedIndex;
                listBox.Items.Clear();
                foreach (var incident in incidents.OrderByDescending(i => i.Time))
                {
                    listBox.Items.Add(incident);
                }
                if (selectedIndex >= 0 && selectedIndex < listBox.Items.Count)
                {
                    listBox.SelectedIndex = selectedIndex;
                }
            }
        }

        private void OnIncidentSelected()
        {
            var listBox = Controls.Find("", true).OfType<ListBox>().FirstOrDefault();
            var detailsBox = Controls.Find("", true).OfType<RichTextBox>().FirstOrDefault();
            var btnResolve = Controls.Find("Разрешить инцидент", true).OfType<Button>().FirstOrDefault();

            if (listBox?.SelectedItem is TrafficIncident incident && detailsBox != null)
            {
                detailsBox.Text = GetIncidentDetails(incident);
                if (btnResolve != null)
                    btnResolve.Enabled = incident.IsActive;
            }
        }

        private string GetIncidentDetails(TrafficIncident incident)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== ИНЦИДЕНТ #{incident.Id.ToString().Substring(0, 8)} ===");
            sb.AppendLine();
            sb.AppendLine($"Тип: {incident.Type}");
            sb.AppendLine($"Время: {incident.Time:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Статус: {(incident.IsActive ? "АКТИВЕН" : "ЗАВЕРШЕН")}");
            sb.AppendLine($"Серьезность: {incident.Severity}");
            sb.AppendLine();
            sb.AppendLine($"Описание:");
            sb.AppendLine(incident.Description);
            sb.AppendLine();

            if (_currentSession?.Network != null)
            {
                var edge = _currentSession.Network.Edges.FirstOrDefault(e => e.Id == incident.LocationEdgeId);
                if (edge != null)
                {
                    sb.AppendLine($"Местоположение: {edge.Name}");
                    sb.AppendLine($"Длина участка: {edge.Length:F1} м");
                    sb.AppendLine($"Текущая загруженность: {edge.CongestionLevel * 100:F1}%");
                }
            }

            return sb.ToString();
        }

        private async Task ResolveSelectedIncident()
        {
            var listBox = Controls.Find("", true).OfType<ListBox>().FirstOrDefault();
            if (listBox?.SelectedItem is TrafficIncident incident && incident.IsActive)
            {
                var result = MessageBox.Show(
                    "Вы уверены, что хотите разрешить этот инцидент?",
                    "Подтверждение",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    incident.IsActive = false;

                    // Разблокировка дороги
                    if (_currentSession?.Network != null)
                    {
                        var edge = _currentSession.Network.Edges.FirstOrDefault(e => e.Id == incident.LocationEdgeId);
                        if (edge != null)
                        {
                            edge.IsBlocked = false;
                        }
                    }

                    await RefreshIncidents();
                }
            }
        }

        private async Task SimulateTestIncident()
        {
            if (_currentSession?.Network == null) return;

            var random = new Random();
            var edges = _currentSession.Network.Edges.Where(e => !e.IsBlocked).ToList();

            if (!edges.Any()) return;

            var edge = edges[random.Next(edges.Count)];
            edge.IsBlocked = true;

            var incident = new TrafficIncident
            {
                Id = Guid.NewGuid(),
                Type = IncidentType.Accident,
                LocationEdgeId = edge.Id,
                Time = DateTime.Now,
                Severity = random.NextDouble() > 0.5 ? IncidentSeverity.Medium : IncidentSeverity.High,
                Description = "Тестовый инцидент: ДТП на участке дороги",
                IsActive = true
            };

            _currentSession.AddIncident(incident);
            await RefreshIncidents();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _updateTimer?.Stop();
            _updateTimer?.Dispose();
            base.OnFormClosing(e);
        }
    }
}