using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TrafficSimulation.Core.Models;
using TrafficSimulation.Core.Services;

namespace TrafficSimulation.UI.Forms
{
    public partial class GraphEditorForm : Form
    {
        private readonly IGraphEditorService _graphEditorService;

        public GraphEditorForm(IGraphEditorService graphEditorService)
        {
            _graphEditorService = graphEditorService;
            InitializeComponent1();
        }

        private void InitializeComponent1()
        {
            this.Text = "Редактор графа";
            this.Size = new Size(1000, 700);
            this.StartPosition = FormStartPosition.CenterParent;

            // Основной контейнер
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

            // Панель инструментов
            var toolPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight
            };

            var btnAddVertex = new Button { Text = "Добавить вершину", Size = new Size(120, 30) };
            var btnAddEdge = new Button { Text = "Добавить ребро", Size = new Size(120, 30) };
            var btnRemove = new Button { Text = "Удалить", Size = new Size(80, 30) };
            var btnSave = new Button { Text = "Сохранить сеть", Size = new Size(120, 30) };
            var btnLoad = new Button { Text = "Загрузить сеть", Size = new Size(120, 30) };

            toolPanel.Controls.AddRange(new Control[] {
                btnAddVertex, btnAddEdge, btnRemove, btnSave, btnLoad
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

            this.Controls.Add(splitContainer);
        }
    }
}