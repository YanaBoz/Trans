using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using TrafficSimulation.Core.Models;
using TrafficSimulation.Core.Repositories;

namespace TrafficSimulation.UI.Forms
{
    public partial class ParametersForm : Form
    {
        private readonly IParametersRepository _parametersRepository;
        private SimulationParameters _currentParameters;
        private BindingList<SimulationParameters> _parametersList;

        public ParametersForm(IParametersRepository parametersRepository)
        {
            InitializeComponent();
            _parametersRepository = parametersRepository;
            InitializeUI();
            LoadParametersAsync();
        }

        private void InitializeComponent()
        {
            throw new NotImplementedException();
        }

        private void InitializeUI()
        {
            this.Text = "Управление параметрами симуляции";
            this.Size = new Size(900, 700);
            this.StartPosition = FormStartPosition.CenterParent;

            // Основной SplitContainer
            var mainSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 300
            };

            // Левая панель - список параметров
            var leftPanel = new Panel
            {
                Dock = DockStyle.Fill
            };

            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };

            listView.Columns.AddRange(new[]
            {
                new ColumnHeader { Text = "Название", Width = 200 },
                new ColumnHeader { Text = "Дата создания", Width = 120 },
                new ColumnHeader { Text = "Дата изменения", Width = 120 },
                new ColumnHeader { Text = "ID", Width = 150 }
            });

            listView.SelectedIndexChanged += (s, e) => OnParameterSelected();
            leftPanel.Controls.Add(listView);

            // Кнопки для списка
            var listButtonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight
            };

            var btnNew = new Button { Text = "Новый", Size = new Size(80, 30) };
            var btnDelete = new Button { Text = "Удалить", Size = new Size(80, 30) };
            var btnClone = new Button { Text = "Клонировать", Size = new Size(100, 30) };
            var btnLoad = new Button { Text = "Загрузить", Size = new Size(80, 30) };

            btnNew.Click += async (s, e) => await CreateNewParameters();
            btnDelete.Click += async (s, e) => await DeleteSelectedParameters();
            btnClone.Click += async (s, e) => await CloneSelectedParameters();
            btnLoad.Click += (s, e) => LoadSelectedParameters();

            listButtonsPanel.Controls.AddRange(new Control[] { btnNew, btnDelete, btnClone, btnLoad });
            leftPanel.Controls.Add(listButtonsPanel);

            // Правая панель - редактор параметров
            var rightPanel = new TabControl
            {
                Dock = DockStyle.Fill
            };

            // Вкладка основных параметров
            var basicTab = new TabPage("Основные параметры");
            SetupBasicParametersTab(basicTab);

            // Вкладка распределений
            var distributionsTab = new TabPage("Распределения");
            SetupDistributionsTab(distributionsTab);

            // Вкладка продвинутых параметров
            var advancedTab = new TabPage("Продвинутые");
            SetupAdvancedTab(advancedTab);

            rightPanel.TabPages.AddRange(new[] { basicTab, distributionsTab, advancedTab });

            mainSplitContainer.Panel1.Controls.Add(leftPanel);
            mainSplitContainer.Panel2.Controls.Add(rightPanel);

            // Панель сохранения
            var savePanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                BackColor = SystemColors.Control
            };

            var btnSave = new Button
            {
                Text = "Сохранить",
                Location = new Point(10, 10),
                Size = new Size(100, 30)
            };
            btnSave.Click += async (s, e) => await SaveParameters();

            var btnCancel = new Button
            {
                Text = "Отмена",
                Location = new Point(120, 10),
                Size = new Size(100, 30)
            };
            btnCancel.Click += (s, e) => this.Close();

            var btnApply = new Button
            {
                Text = "Применить",
                Location = new Point(230, 10),
                Size = new Size(100, 30)
            };
            btnApply.Click += (s, e) => ApplyParameters();

            savePanel.Controls.AddRange(new Control[] { btnSave, btnCancel, btnApply });

            this.Controls.Add(mainSplitContainer);
            this.Controls.Add(savePanel);
        }

        private void SetupBasicParametersTab(TabPage tab)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            var y = 10;

            // Название
            AddLabeledTextBox(panel, ref y, "Название:", "Name", 250);

            // Интенсивность трафика
            AddLabeledNumericUpDown(panel, ref y, "Интенсивность ТС:", "VehicleIntensity", 0.1m, 5.0m, 0.1m, 250);

            // Интенсивность пешеходов
            AddLabeledNumericUpDown(panel, ref y, "Интенсивность пешеходов:", "PedestrianIntensity", 0.1m, 5.0m, 0.1m, 250);

            // Начальное количество ТС
            AddLabeledNumericUpDown(panel, ref y, "Начальное количество ТС:", "InitialVehicles", 1, 1000, 1, 250);

            // Начальное количество пешеходов
            AddLabeledNumericUpDown(panel, ref y, "Начальное количество пешеходов:", "InitialPedestrians", 0, 500, 1, 250);

            // Длительность симуляции
            AddLabeledNumericUpDown(panel, ref y, "Длительность симуляции (сек):", "SimulationDurationSeconds", 60, 86400, 60, 250);

            // Шаг симуляции
            AddLabeledNumericUpDown(panel, ref y, "Шаг симуляции (сек):", "TimeStepSeconds", 0.1m, 60, 0.1m, 250);

            // Вероятность ДТП
            AddLabeledNumericUpDown(panel, ref y, "Коэффициент вероятности ДТП:", "AccidentProbabilityFactor", 0.0001m, 0.1m, 0.0001m, 250);

            // Длительность блокировки
            AddLabeledNumericUpDown(panel, ref y, "Длительность блокировки (сек):", "BlockDurationSeconds", 10, 3600, 10, 250);

            // Порог загруженности
            AddLabeledNumericUpDown(panel, ref y, "Порог загруженности:", "CongestionThreshold", 0.1m, 1.0m, 0.05m, 250);

            // Шанс избежать ДТП
            AddLabeledNumericUpDown(panel, ref y, "Шанс избежать ДТП:", "NoAccidentChance", 0.0m, 1.0m, 0.01m, 250);

            tab.Controls.Add(panel);
        }

        private void SetupDistributionsTab(TabPage tab)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            var y = 10;

            // Распределение типов транспортных средств
            AddLabel(panel, ref y, "Распределение типов ТС:", true);
            y += 5;

            AddDistributionControl(panel, ref y, "Легковые", "Car", 100);
            AddDistributionControl(panel, ref y, "Новички", "NoviceCar", 100);
            AddDistributionControl(panel, ref y, "Автобусы", "Bus", 100);
            AddDistributionControl(panel, ref y, "Грузовики", "Truck", 100);
            AddDistributionControl(panel, ref y, "Спецтранспорт", "Special", 100);
            AddDistributionControl(panel, ref y, "Велосипеды", "Bicycle", 100);

            y += 10;

            // Распределение стилей вождения
            AddLabel(panel, ref y, "Распределение стилей вождения:", true);
            y += 5;

            AddDistributionControl(panel, ref y, "Осторожный", "Cautious", 100);
            AddDistributionControl(panel, ref y, "Нормальный", "Normal", 100);
            AddDistributionControl(panel, ref y, "Агрессивный", "Aggressive", 100);

            tab.Controls.Add(panel);
        }

        private void SetupAdvancedTab(TabPage tab)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            var y = 10;

            // Генерация случайных параметров
            var btnRandomize = new Button
            {
                Text = "Сгенерировать случайные параметры",
                Location = new Point(20, y),
                Size = new Size(250, 30)
            };
            btnRandomize.Click += (s, e) => GenerateRandomParameters();
            panel.Controls.Add(btnRandomize);
            y += 40;

            // Период пиковой нагрузки
            AddLabeledNumericUpDown(panel, ref y, "Начало пика (час):", "PeakHourStart", 0, 23, 1, 250);
            AddLabeledNumericUpDown(panel, ref y, "Конец пика (час):", "PeakHourEnd", 0, 23, 1, 250);

            // Фактор пиковой нагрузки
            AddLabeledNumericUpDown(panel, ref y, "Фактор пиковой нагрузки:", "PeakIntensityFactor", 1.0m, 3.0m, 0.1m, 250);

            // Время регенерации инцидентов
            AddLabeledNumericUpDown(panel, ref y, "Время регенерации инцидентов (сек):", "IncidentRegenerationTime", 60, 3600, 60, 250);

            // Максимальное количество ТС
            AddLabeledNumericUpDown(panel, ref y, "Максимальное количество ТС:", "MaxVehicles", 10, 5000, 10, 250);

            // Максимальное количество пешеходов
            AddLabeledNumericUpDown(panel, ref y, "Максимальное количество пешеходов:", "MaxPedestrians", 10, 2000, 10, 250);

            tab.Controls.Add(panel);
        }

        private void AddLabel(Panel panel, ref int y, string text, bool bold = false)
        {
            var label = new Label
            {
                Text = text,
                Location = new Point(20, y),
                Font = bold ? new Font("Arial", 9, FontStyle.Bold) : new Font("Arial", 9),
                AutoSize = true
            };
            panel.Controls.Add(label);
            y += 25;
        }

        private void AddLabeledTextBox(Panel panel, ref int y, string labelText, string propertyName, int textBoxWidth)
        {
            var label = new Label
            {
                Text = labelText,
                Location = new Point(20, y),
                AutoSize = true
            };
            panel.Controls.Add(label);

            var textBox = new TextBox
            {
                Location = new Point(200, y - 3),
                Size = new Size(textBoxWidth, 25),
                Tag = propertyName
            };
            textBox.TextChanged += (s, e) => UpdateParameterProperty(propertyName, textBox.Text);
            panel.Controls.Add(textBox);

            y += 30;
        }

        private void AddLabeledNumericUpDown(Panel panel, ref int y, string labelText, string propertyName,
            decimal min, decimal max, decimal increment, int width)
        {
            var label = new Label
            {
                Text = labelText,
                Location = new Point(20, y),
                AutoSize = true
            };
            panel.Controls.Add(label);

            var numericUpDown = new NumericUpDown
            {
                Location = new Point(200, y - 3),
                Size = new Size(width, 25),
                Minimum = min,
                Maximum = max,
                Increment = increment,
                DecimalPlaces = 4,
                Tag = propertyName
            };
            numericUpDown.ValueChanged += (s, e) => UpdateParameterProperty(propertyName, numericUpDown.Value);
            panel.Controls.Add(numericUpDown);

            y += 30;
        }

        private void AddDistributionControl(Panel panel, ref int y, string labelText, string propertyName, int width)
        {
            var label = new Label
            {
                Text = labelText,
                Location = new Point(40, y),
                Width = 100
            };
            panel.Controls.Add(label);

            var trackBar = new TrackBar
            {
                Location = new Point(150, y - 3),
                Size = new Size(width, 30),
                Minimum = 0,
                Maximum = 100,
                SmallChange = 1,
                LargeChange = 10,
                Tag = propertyName
            };
            trackBar.Scroll += (s, e) => UpdateDistributionProperty(propertyName, trackBar.Value);
            panel.Controls.Add(trackBar);

            var valueLabel = new Label
            {
                Location = new Point(260 + width, y),
                Text = "0",
                AutoSize = true,
                Tag = propertyName + "_value"
            };
            panel.Controls.Add(valueLabel);

            y += 35;
        }

        private async void LoadParametersAsync()
        {
            try
            {
                var parameters = await _parametersRepository.GetAllParametersAsync();
                _parametersList = new BindingList<SimulationParameters>(parameters.ToList());
                UpdateParametersList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки параметров: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateParametersList()
        {
            // Обновление ListView с параметрами
            // Реализация зависит от используемого элемента управления
        }

        private void OnParameterSelected()
        {
            // Загрузка выбранных параметров в редактор
        }

        private async Task CreateNewParameters()
        {
            var parameters = await _parametersRepository.CreateDefaultParametersAsync("Новые параметры");
            _parametersList.Add(parameters);
            _currentParameters = parameters;
            UpdateParametersList();
        }

        private async Task DeleteSelectedParameters()
        {
            // Удаление выбранных параметров
        }

        private async Task CloneSelectedParameters()
        {
            // Клонирование выбранных параметров
        }

        private void LoadSelectedParameters()
        {
            // Загрузка выбранных параметров в форму
        }

        private void UpdateParameterProperty(string propertyName, object value)
        {
            if (_currentParameters == null) return;

            var property = typeof(SimulationParameters).GetProperty(propertyName);
            if (property != null && property.CanWrite)
            {
                try
                {
                    var convertedValue = Convert.ChangeType(value, property.PropertyType);
                    property.SetValue(_currentParameters, convertedValue);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating property {propertyName}: {ex.Message}");
                }
            }
        }

        private void UpdateDistributionProperty(string propertyName, int value)
        {
            // Обновление распределений
        }

        private async Task SaveParameters()
        {
            if (_currentParameters == null)
            {
                MessageBox.Show("Нет параметров для сохранения", "Предупреждение", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                await _parametersRepository.SaveParametersAsync(_currentParameters);
                MessageBox.Show("Параметры сохранены успешно", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ApplyParameters()
        {
            // Применение параметров без сохранения
        }

        private void GenerateRandomParameters()
        {
            if (_currentParameters == null) return;

            var random = new Random();
            _currentParameters.VehicleIntensity = 0.5 + random.NextDouble() * 2.0;
            _currentParameters.PedestrianIntensity = 0.1 + random.NextDouble() * 1.0;
            _currentParameters.AccidentProbabilityFactor = 0.0005 + random.NextDouble() * 0.002;
            _currentParameters.CongestionThreshold = 0.5 + random.NextDouble() * 0.3;

            // Обновление UI
        }
    }
}