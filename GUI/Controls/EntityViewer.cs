using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Types.Renderer;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace GUI.Types.Viewers
{
    public partial class EntityViewer : UserControl
    {
        private List<(Entity entity, string lumpName)> Entities = new();

        public enum ObjectsToInclude
        {
            Everything,
            MeshEntities,
            PointEntities
        }

        public class SearchDataClass
        {
            public ObjectsToInclude ObjectsToInclude { get; set; } = ObjectsToInclude.Everything;

            public string Class { get; set; } = string.Empty;
            public string Key { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }

        private SearchDataClass SearchData = new();

        internal EntityViewer(VrfGuiContext guiContext, EntityLump? entityLump, World? world = null)
        {
            // if we have world, load all entity lumps, if not try to load the specified lump only
            if (world is not null)
            {
                foreach (var lumpName in world.GetEntityLumpNames())
                {
                    if (lumpName == null)
                    {
                        continue;
                    }

                    var newResource = guiContext.LoadFileCompiled(lumpName);

                    if (newResource == null)
                    {
                        continue;
                    }

                    LoadEntitiesFromLump(guiContext, (EntityLump?)newResource.DataBlock, lumpName);
                }
            }
            else
            {
                // could actually use LoadEntitiesFromLump here too, but id rather this only show the loaded lump without anything else
                Entities = EntityListWithLumpName(entityLump!.GetEntities(), entityLump);
            }

            Dock = DockStyle.Fill;
            InitializeComponent();

            EntityInfo.OutputsGrid.CellDoubleClick += EntityInfoGrid_CellDoubleClick;
            EntityInfo.ResourceAddDataGridExternalRef(guiContext.FileLoader);

            UpdateGrid();
        }

        private static List<(Entity entity, string lumpName)> EntityListWithLumpName(List<Entity> entities, EntityLump lump)
        {
            var newList = new List<(Entity entity, string lumpName)>();
            var lumpName = Path.GetFileName(lump.Resource.FileName) ?? string.Empty;

            foreach (var entity in entities)
            {
                newList.Add((entity, lumpName));
            }

            return newList;
        }

        private void LoadEntitiesFromLump(VrfGuiContext guiContext, EntityLump? entityLump, string layerName)
        {
            if (entityLump is null)
            {
                return;
            }

            var childEntities = entityLump.GetChildEntityNames();
            var childEntityLumps = new Dictionary<string, EntityLump>(childEntities?.Length ?? 0);

            if (childEntities is not null)
            {
                foreach (var childEntityName in childEntities)
                {
                    var newResource = guiContext.LoadFileCompiled(childEntityName);

                    if (newResource == null)
                    {
                        continue;
                    }

                    var childLump = (EntityLump?)newResource.DataBlock;
                    var childName = childLump?.Data.GetProperty<string>("m_name");

                    if (childName != null && childLump != null)
                    {
                        childEntityLumps.Add(childName, childLump);
                    }
                }
            }

            var entities = EntityListWithLumpName(entityLump.GetEntities().ToList(), entityLump);

            Entities.AddRange(entities);

            var templates = entities.Where(entity => entity.entity.GetProperty("classname", string.Empty) == "point_template");

            foreach (var template in templates)
            {
                var entityLumpName = template.entity.GetProperty<string>("entitylumpname");

                if (entityLumpName != null && childEntityLumps.TryGetValue(entityLumpName, out var childLump))
                {
                    LoadEntitiesFromLump(guiContext, childLump, entityLumpName);
                }
                else
                {
                    Log.Warn(nameof(WorldLoader), $"Failed to find child entity lump with name {entityLumpName}.");
                }
            }
        }

        private void UpdateGrid()
        {
            EntityViewerGrid.SuspendLayout();

            EntityViewerGrid.Rows.Clear();

            for (int i = 0; i < Entities.Count; i++)
            {
                var entity = Entities[i];

                var classname = entity.entity.GetProperty("classname", string.Empty);

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
                        if (!entity.entity.ContainsKey("model"))
                        {
                            continue;
                        }
                        break;

                    case ObjectsToInclude.PointEntities:
                        if (entity.entity.ContainsKey("model"))
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
                    if (!ContainsKeyValue(entity.entity, SearchData.Key, SearchData.Value))
                    {
                        continue;
                    }
                }
                // search only by key
                else if (!isKeyEmpty)
                {
                    if (!ContainsKey(entity.entity, SearchData.Key))
                    {
                        continue;
                    }
                }
                // search only by value
                else if (!isValueEmpty)
                {
                    if (!ContainsValue(entity.entity, SearchData.Value))
                    {
                        continue;
                    }
                }

                EntityViewerGrid.Rows.Add(classname, entity.entity.GetProperty("targetname", string.Empty));
                EntityViewerGrid.Rows[EntityViewerGrid.Rows.Count - 1].Tag = i;
            }

            EntityViewerGrid.ResumeLayout();

            // when search changes set first entity as selected in entity props
            if (EntityViewerGrid.Rows.Count > 0)
            {
                int entityID = (int)(EntityViewerGrid.Rows[0].Tag ?? -1);
                ShowEntityProperties(Entities[entityID].entity, Entities[entityID].lumpName);
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
                int entityID = (int)(EntityViewerGrid.Rows[rowIndex].Tag ?? -1);

                if (entityID != -1)
                {
                    var entity = Entities[entityID];
                    ShowEntityProperties(entity.entity, entity.lumpName);
                }
            }
        }

        private void ShowEntityProperties(Entity entity, string lumpName)
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

            string groupBoxName = "Entity Properties";

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

            groupBoxName += $" - Entity Lump: {lumpName}";

            EntityPropertiesGroup.Text = groupBoxName;
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
                    var targetname = entity.entity.GetProperty("targetname", string.Empty);
                    if (string.IsNullOrEmpty(targetname))
                    {
                        continue;
                    }

                    if (entityName == targetname)
                    {
                        ShowEntityProperties(entity.entity, entity.lumpName);
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
