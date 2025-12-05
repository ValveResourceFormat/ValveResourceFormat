using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace GUI.Types.Viewers
{
    public partial class EntityViewer : UserControl
    {
        private readonly SearchDataClass SearchData = new();
        private readonly List<Entity> Entities = [];
        private readonly Action<Entity>? SelectEntityFunc;

        public enum ObjectsToInclude
        {
            Everything,
            MeshEntities,
            PointEntities
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            BackColor = Themer.CurrentThemeColors.AppMiddle;
        }

        public class SearchDataClass
        {
            public ObjectsToInclude ObjectsToInclude { get; set; } = ObjectsToInclude.Everything;

            public string Class { get; set; } = string.Empty;
            public string Key { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }

        internal EntityViewer(VrfGuiContext guiContext, List<Entity> entities, Action<Entity>? selectAndFocusEntity = null)
        {
            Dock = DockStyle.Fill;
            InitializeComponent();

            Entities = entities;
            SelectEntityFunc = selectAndFocusEntity;
            EntityInfo.OutputsGrid.CellDoubleClick += EntityInfoGrid_CellDoubleClick;
            EntityInfo.ResourceAddDataGridExternalRef(guiContext);

            UpdateGrid();
        }

        private void UpdateGrid()
        {
            var filteredEntities = new List<(Entity Entity, string Classname, string Targetname)>(Entities.Count);

            foreach (var entity in Entities)
            {
                var classname = entity.GetProperty("classname", string.Empty);

                if (!string.IsNullOrEmpty(SearchData.Class))
                {
                    if (!classname.Contains(SearchData.Class, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                switch (SearchData.ObjectsToInclude)
                {
                    case ObjectsToInclude.Everything:
                        break;

                    case ObjectsToInclude.MeshEntities:
                        if (!entity.ContainsKey("model"))
                        {
                            continue;
                        }
                        break;

                    case ObjectsToInclude.PointEntities:
                        if (entity.ContainsKey("model"))
                        {
                            continue;
                        }
                        break;
                }

                var isKeyEmpty = string.IsNullOrEmpty(SearchData.Key);
                var isValueEmpty = string.IsNullOrEmpty(SearchData.Value);

                // match key and value together
                if (!isKeyEmpty && !isValueEmpty)
                {
                    if (!ContainsKeyValue(entity, SearchData.Key, SearchData.Value))
                    {
                        continue;
                    }
                }
                // search only by key
                else if (!isKeyEmpty)
                {
                    if (!ContainsKey(entity, SearchData.Key))
                    {
                        continue;
                    }
                }
                // search only by value
                else if (!isValueEmpty)
                {
                    if (!ContainsValue(entity, SearchData.Value))
                    {
                        continue;
                    }
                }

                var targetname = entity.GetProperty("targetname", string.Empty);
                filteredEntities.Add((entity, classname, targetname));
            }

            EntityViewerGrid.SuspendLayout();
            EntityViewerGrid.Rows.Clear();

            if (filteredEntities.Count > 0)
            {
                var rows = new DataGridViewRow[filteredEntities.Count];
                for (var i = 0; i < filteredEntities.Count; i++)
                {
                    var (entity, classname, targetname) = filteredEntities[i];
                    var row = new DataGridViewRow();
                    row.CreateCells(EntityViewerGrid);
                    row.Cells[0].Value = classname;
                    row.Cells[1].Value = targetname;
                    row.Tag = entity;
                    rows[i] = row;
                }
                EntityViewerGrid.Rows.AddRange(rows);
            }

            EntityViewerGrid.ResumeLayout();

            // when search changes set first entity as selected in entity props
            if (filteredEntities.Count > 0)
            {
                ShowEntityProperties(filteredEntities[0].Entity);
            }
        }

        private static bool ContainsKey(Entity entity, string key)
        {
            foreach (var prop in entity.Properties)
            {
                if (prop.Key.Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ContainsValue(Entity entity, string value)
        {
            foreach (var prop in entity.Properties)
            {
                var stringValue = prop.Value.ToString() ?? string.Empty;

                if (KeyValue_MatchWholeValue.Checked)
                {
                    if (stringValue == value)
                    {
                        return true;
                    }
                }
                else
                {
                    if (stringValue.Contains(value, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool ContainsKeyValue(Entity entity, string key, string value)
        {
            foreach (var prop in entity.Properties)
            {
                var stringValue = prop.Value?.ToString() ?? string.Empty;

                if (prop.Key.Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    if (KeyValue_MatchWholeValue.Checked)
                    {
                        if (stringValue == value)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        if (stringValue.Contains(value, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private void ObjectsToInclude_PointEntities_CheckedChanged(object sender, EventArgs e)
        {
            if (((RadioButton)sender).Checked)
            {
                SearchData.ObjectsToInclude = ObjectsToInclude.PointEntities;

                UpdateGrid();
            }
        }

        private void ObjectsToInclude_MeshEntities_CheckedChanged(object sender, EventArgs e)
        {
            if (((RadioButton)sender).Checked)
            {
                SearchData.ObjectsToInclude = ObjectsToInclude.MeshEntities;

                UpdateGrid();
            }
        }

        private void ObjectsToInclude_Everything_CheckedChanged(object sender, EventArgs e)
        {
            if (((RadioButton)sender).Checked)
            {
                SearchData.ObjectsToInclude = ObjectsToInclude.Everything;

                UpdateGrid();
            }
        }

        private void ObjectsToInclude_ClassTextBox_TextChanged(object sender, EventArgs e)
        {
            SearchData.Class = ((TextBox)sender).Text;
            UpdateGrid();
        }

        private void KeyValue_Key_TextChanged(object sender, EventArgs e)
        {
            SearchData.Key = ((TextBox)sender).Text;
            UpdateGrid();
        }

        private void KeyValue_Value_TextChanged(object sender, EventArgs e)
        {
            SearchData.Value = ((TextBox)sender).Text;
            UpdateGrid();
        }

        private void KeyValue_MatchWholeValue_CheckedChanged(object sender, EventArgs e)
        {
            UpdateGrid();
        }

        private void EntityViewerGrid_SelectionChanged(object sender, EventArgs e)
        {
            if (EntityViewerGrid.SelectedCells.Count > 0)
            {
                var rowIndex = EntityViewerGrid.SelectedCells[0].RowIndex;

                if (EntityViewerGrid.Rows[rowIndex].Tag is Entity entity)
                {
                    ShowEntityProperties(entity);
                }
            }
        }

        private void EntityViewerGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (EntityViewerGrid.Rows[e.RowIndex].Tag is Entity entity)
            {
                var classname = entity.GetProperty("classname", string.Empty);
                if (classname == "worldspawn")
                {
                    return;
                }

                SelectEntityFunc?.Invoke(entity);
            }
        }

        private void ShowEntityProperties(Entity entity)
        {
            EntityInfo.Clear();

            foreach (var (key, value) in entity.Properties)
            {
                EntityInfo.AddProperty(key, value switch
                {
                    null => string.Empty,
                    KVObject { IsArray: true } kvArray => string.Join(' ', kvArray.Select(p => p.Value.ToString())),
                    _ => value.ToString(),
                } ?? string.Empty);
            }

            if (entity.Connections != null)
            {
                foreach (var connection in entity.Connections)
                {
                    EntityInfo.AddConnection(connection);
                }
            }

            EntityInfo.ShowOutputsTabIfAnyData();

            var groupBoxName = "Entity Properties";

            var targetname = entity.GetProperty("targetname", string.Empty);
            var classname = entity.GetProperty("classname", string.Empty);

            if (!string.IsNullOrEmpty(targetname))
            {
                groupBoxName += $" - {targetname}";
            }
            else if (!string.IsNullOrEmpty(classname))
            {
                groupBoxName += $" - {classname}";
            }

            if (entity.ParentLump.Resource is { } parentResource)
            {
                groupBoxName += $" - Entity Lump: {parentResource.FileName}";
            }

            EntityPropertiesGroup.Text = groupBoxName;
        }

        private void EntityInfoGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex == 1)
            {
                var entityName = (string)(EntityInfo.OutputsGrid[e.ColumnIndex, e.RowIndex].Value ?? string.Empty);

                if (string.IsNullOrEmpty(entityName))
                {
                    return;
                }

                foreach (var entity in Entities)
                {
                    var targetname = entity.GetProperty("targetname", string.Empty);
                    if (string.IsNullOrEmpty(targetname))
                    {
                        continue;
                    }

                    if (entityName == targetname)
                    {
                        ShowEntityProperties(entity);
                        ResetViewerState();
                    }
                }
            }
        }

        private void ResetViewerState()
        {
            // actually not sure if this is better to do cuz it might be annoying to always lose state when jumping
            // i think the small error of entity property shown not matching selection is fine

            //SearchData.Key = string.Empty;
            //KeyValue_Key.Text = string.Empty;
            //
            //SearchData.Value = string.Empty;
            //KeyValue_Value.Text = string.Empty;
            //
            //SearchData.ObjectsToInclude = ObjectsToInclude.Everything;
            //ObjectsToInclude_Everything.Checked = true;
            //
            //SearchData.Class = string.Empty;
            //ObjectsToInclude_ClassTextBox.Text = string.Empty;
            //
            //KeyValue_MatchWholeValue.Checked = false;

            EntityInfo.ShowPropertiesTab();
        }
    }
}
