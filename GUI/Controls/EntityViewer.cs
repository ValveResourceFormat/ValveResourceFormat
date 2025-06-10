using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Viewers
{
    public partial class EntityViewer : UserControl
    {
        private List<EntityLump.Entity> Entities;

        public enum ObjectsToInclude
        {
            Everything,
            MeshEntities,
            PointEntities,
            Class
        }

        public class SearchDataClass
        {
            public ObjectsToInclude ObjectsToInclude { get; set; } = ObjectsToInclude.Everything;

            public string Class { get; set; } = string.Empty;
            public string Key { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }

        private SearchDataClass SearchData = new();

        internal EntityViewer(VrfGuiContext guiContext, EntityLump entityLump)
        {
            Entities = entityLump.GetEntities();

            Dock = DockStyle.Fill;
            InitializeComponent();

            EntityInfo.OutputsGrid.CellDoubleClick += EntityInfoGrid_CellDoubleClick;
            EntityInfo.ResourceAddDataGridExternalRef(guiContext.FileLoader);

            UpdateGrid();
        }

        private void UpdateGrid()
        {
            EntityViewerGrid.SuspendLayout();

            EntityViewerGrid.Rows.Clear();

            for (int i = 0; i < Entities.Count; i++)
            {
                var entity = Entities[i];

                var classname = entity.GetProperty("classname", string.Empty);

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

                    case ObjectsToInclude.Class:
                        if (!string.IsNullOrEmpty(SearchData.Class))
                        {
                            if (!classname.Contains(SearchData.Class, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
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

                EntityViewerGrid.Rows.Add(classname, entity.GetProperty("targetname", string.Empty));
                EntityViewerGrid.Rows[EntityViewerGrid.Rows.Count - 1].Tag = i;
            }

            EntityViewerGrid.ResumeLayout();

            // when search changes set first entity as selected in entity props
            if (EntityViewerGrid.Rows.Count > 0)
            {
                int entityID = (int)(EntityViewerGrid.Rows[0].Tag ?? -1);
                ShowEntityProperties(Entities[entityID]);
            }
        }

        private static bool ContainsKey(EntityLump.Entity entity, string key)
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

        private bool ContainsValue(EntityLump.Entity entity, string value)
        {
            foreach (var prop in entity.Properties)
            {
                string stringValue = prop.Value.ToString() ?? string.Empty;

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

        private bool ContainsKeyValue(EntityLump.Entity entity, string key, string value)
        {
            foreach (var prop in entity.Properties)
            {
                string stringValue = prop.Value?.ToString() ?? string.Empty;

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

        private void ObjectsToInclude_Class_CheckedChanged(object sender, EventArgs e)
        {
            if (((RadioButton)sender).Checked)
            {
                SearchData.ObjectsToInclude = ObjectsToInclude.Class;
                ObjectsToInclude_ClassTextBox.Enabled = true;

                UpdateGrid();
            }
            else
            {
                ObjectsToInclude_ClassTextBox.Enabled = false;
            }

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

        private void EntityViewerGrid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            int entityID = (int)(EntityViewerGrid.Rows[e.RowIndex].Tag ?? -1);
            var entity = Entities[entityID];

            ShowEntityProperties(entity);
        }

        private void ShowEntityProperties(EntityLump.Entity entity)
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

            string groupBoxName = "Entity Properties";

            var targetname = entity.GetProperty("targetname", string.Empty);
            var classname = entity.GetProperty("classname", string.Empty);

            if (!string.IsNullOrEmpty(targetname))
            {
                EntityPropertiesGroup.Text = groupBoxName += $" - {targetname}";
            }
            else if (!string.IsNullOrEmpty(classname))
            {
                EntityPropertiesGroup.Text = groupBoxName += $" - {classname}";
            }
            else
            {
                EntityPropertiesGroup.Text = groupBoxName;
            }
        }

        private void EntityInfoGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex == 1)
            {
                string entityName = (string)(EntityInfo.OutputsGrid[e.ColumnIndex, e.RowIndex].Value ?? string.Empty);

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
