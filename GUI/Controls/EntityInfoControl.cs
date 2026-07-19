using System.Windows.Forms;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace GUI.Forms
{
    partial class EntityInfoControl : UserControl
    {
        public DataGridView OutputsGrid => dataGridOutputs;
        public DataGridView InputsGrid => dataGridInputs;

        public EntityInfoControl()
        {
            InitializeComponent();

            components ??= new System.ComponentModel.Container();
            components.Add(tabPageOutputs);
            components.Add(tabPageInputs);
        }

        public EntityInfoControl(VrfGuiContext vrfGuiContext) : this()
        {
            ResourceAddDataGridExternalRef(vrfGuiContext);
        }

        public void ResourceAddDataGridExternalRef(VrfGuiContext vrfGuiContext)
        {
            AddDataGridExternalRefAction(vrfGuiContext, dataGridProperties, ColumnValue.Name);
        }

        public void ShowPropertiesTab()
        {
            tabControl.SelectedIndex = 0;
        }

        private TabPage[] TabPageOrder => [tabPageProperties, tabPageOutputs, tabPageInputs];

        public void ShowPopulatedTabs()
        {
            SetTabVisible(tabPageOutputs, dataGridOutputs.RowCount > 0);
            SetTabVisible(tabPageInputs, dataGridInputs.RowCount > 0);
        }

        private void SetTabVisible(TabPage page, bool shouldShow)
        {
            bool isShown = tabControl.TabPages.Contains(page);

            if (shouldShow && !isShown)
            {
                tabControl.TabPages.Insert(GetInsertIndex(page), page);
            }
            else if (!shouldShow && isShown)
            {
                tabControl.TabPages.Remove(page);
            }
        }

        private int GetInsertIndex(TabPage page)
        {
            int targetOrder = Array.IndexOf(TabPageOrder, page);
            int index = 0;

            for (int i = 0; i < targetOrder; i++)
            {
                if (tabControl.TabPages.Contains(TabPageOrder[i]))
                {
                    index++;
                }
            }

            return index;
        }

        public void Clear()
        {
            dataGridProperties.Rows.Clear();
            dataGridOutputs.Rows.Clear();
            dataGridInputs.Rows.Clear();
        }

        public void PopulateFromEntity(Entity entity)
        {
            foreach (var child in entity.Children)
            {
                AddProperty(child.Key, StringifyValue(child.Value));
            }

            if (entity.Connections != null)
            {
                foreach (var connection in entity.Connections)
                {
                    AddOutputConnection(connection);
                }
            }
        }
        public void PopulateFromEntity(List<Entity> entities, Entity entity)
        {
            foreach (var child in entity.Children)
            {
                AddProperty(child.Key, StringifyValue(child.Value));
            }

            if (entity.Connections != null)
            {
                foreach (var connection in entity.Connections)
                {
                    AddOutputConnection(connection);
                }
            }

            foreach (var connection in entity.GetInputConnections(entities))
            {
                AddInputConnection(connection);
            }
        }

        public void AddProperty(string name, string value)
        {
            dataGridProperties.Rows.Add([name, value]);
        }

        public void AddOutputConnection(Connection connectionData)
        {
            dataGridOutputs.Rows.Add([
                connectionData.OutputName,
                connectionData.TargetName,
                connectionData.InputName,
                connectionData.OverrideParam,
                connectionData.Delay,
                GetStringTimesToFire(connectionData.TimesToFire)
            ]);
        }

        public void AddInputConnection(Connection connectionData)
        {
            var rowIndex = dataGridInputs.Rows.Add([
                connectionData.SourceEntity.TargetName ?? "",
                connectionData.OutputName,
                connectionData.InputName,
                connectionData.OverrideParam,
                connectionData.Delay,
                GetStringTimesToFire(connectionData.TimesToFire)
            ]);

            dataGridInputs.Rows[rowIndex].Tag = connectionData.SourceEntity;
        }

        private static string GetStringTimesToFire(int timesToFire)
        {
            return timesToFire switch
            {
                1 => "Only Once",
                >= 2 => $"Only {timesToFire} Times",
                _ => "Infinite",
            };
        }

        private void AddDataGridExternalRefAction(VrfGuiContext vrfGuiContext, DataGridView dataGrid, string columnName)
        {
            void OnCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || sender is not DataGridView grid)
                {
                    return;
                }

                var row = grid.Rows[e.RowIndex];
                var colName = columnName;
                var name = (string)row.Cells[colName].Value!;

                var found = Types.Viewers.Resource.OpenExternalReference(vrfGuiContext, name);

                if (found && Parent is Form form)
                {
                    form.Close();
                }
            }

            void OnDisposed(object? sender, EventArgs e)
            {
                dataGrid.CellDoubleClick -= OnCellDoubleClick;
                dataGrid.Disposed -= OnDisposed;
            }

            dataGrid.CellDoubleClick += OnCellDoubleClick;
            dataGrid.Disposed += OnDisposed;
        }
    }
}
