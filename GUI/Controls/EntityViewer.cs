using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Utils;
using SkiaSharp;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace GUI.Types.Viewers
{
    public partial class EntityViewer : UserControl
    {
        private readonly SearchDataClass SearchData = new();
        private readonly List<Entity> Entities = [];
        private readonly Action<Entity>? SelectEntityFunc;
        private readonly VrfGuiContext GuiContext;

        public static ImageList? EntityIconImageList { get; private set; }
        public static Dictionary<string, int> EntityIconCache { get; private set; } = [];
        public static HashSet<string> EntityIconLoadAttempted { get; private set; } = [];

        private static int GetDefaultIconIndexForEntity(Entity entity)
        {
            if (entity.ContainsKey("model"))
            {
                return 1;
            }

            if (entity.ContainsKey("effect_name"))
            {
                return 2;
            }

            // Return number matches the order of EntityIconImageList.Images.Add calls
            return 0;
        }

        public static void InitializeImageList()
        {
            if (EntityIconImageList != null)
            {
                return;
            }

            EntityIconImageList = new ImageList
            {
                ColorDepth = ColorDepth.Depth32Bit,
                ImageSize = MainForm.ImageList.ImageSize,
            };

            if (MainForm.ExtensionIcons.TryGetValue("ents", out var entsIconIndex) &&
                MainForm.ImageList.Images[entsIconIndex] is Bitmap entsIcon)
            {
                EntityIconImageList.Images.Add(entsIcon);
            }

            if (MainForm.ExtensionIcons.TryGetValue("mdl", out var mdlIconIndex) &&
                MainForm.ImageList.Images[mdlIconIndex] is Bitmap mdlIcon)
            {
                EntityIconImageList.Images.Add(mdlIcon);
            }

            if (MainForm.ExtensionIcons.TryGetValue("pcf", out var pcfIconIndex) &&
                MainForm.ImageList.Images[pcfIconIndex] is Bitmap pcfIcon)
            {
                EntityIconImageList.Images.Add(pcfIcon);
            }
        }

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

            GuiContext = guiContext;
            Entities = entities;
            SelectEntityFunc = selectAndFocusEntity;
            EntityInfo.OutputsGrid.CellDoubleClick += EntityInfoGrid_CellDoubleClick;
            EntityInfo.ResourceAddDataGridExternalRef(guiContext);
            EntityViewerGrid.OwnerDraw = true;

            InitializeImageList();
            EntityViewerGrid.SmallImageList = EntityIconImageList;

            var allClassnames = Entities
                .Select(static e => e.GetProperty("classname", string.Empty))
                .Where(static cn => !string.IsNullOrEmpty(cn))
                .Where(static cn => !EntityIconLoadAttempted.Contains(cn))
                .ToHashSet();

            if (allClassnames.Count > 0)
            {
                Task.Factory.StartNew(() => LoadEntityIcons(allClassnames)).ContinueWith(t =>
                {
                    if (t.Exception != null)
                    {
                        Log.Error(nameof(EntityViewer), t.Exception.ToString());
                    }
                });
            }

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

            EntityViewerGrid.BeginUpdate();
            EntityViewerGrid.Items.Clear();

            if (filteredEntities.Count > 0)
            {
                var items = filteredEntities
                    .OrderBy(static e => e.Classname, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static e => e.Targetname, StringComparer.OrdinalIgnoreCase)
                    .Select(static e =>
                    {
                        var (entity, classname, targetname) = e;
                        var item = new ListViewItem(classname);
                        item.SubItems.Add(targetname);
                        item.Tag = entity;

                        if (EntityIconCache.TryGetValue(classname, out var iconIndex))
                        {
                            item.ImageIndex = iconIndex;
                        }
                        else
                        {
                            item.ImageIndex = GetDefaultIconIndexForEntity(entity);
                        }

                        return item;
                    })
                    .ToArray();

                EntityViewerGrid.Items.AddRange(items);
            }

            EntityViewerGrid.EndUpdate();

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

        private void EntityViewerGrid_SelectionChanged(object? sender, EventArgs e)
        {
            if (EntityViewerGrid.SelectedItems.Count > 0)
            {
                if (EntityViewerGrid.SelectedItems[0].Tag is Entity entity)
                {
                    ShowEntityProperties(entity);
                }
            }
        }

        private void EntityViewerGrid_CellDoubleClick(object? sender, EventArgs e)
        {
            if (EntityViewerGrid.SelectedItems.Count > 0)
            {
                if (EntityViewerGrid.SelectedItems[0].Tag is Entity entity)
                {
                    var classname = entity.GetProperty("classname", string.Empty);
                    if (classname == "worldspawn")
                    {
                        return;
                    }

                    SelectEntityFunc?.Invoke(entity);
                }
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
                        EntityInfo.ShowPropertiesTab();
                    }
                }
            }
        }

        private void EntityViewerGrid_Resize(object sender, EventArgs e)
        {
            EntityViewerGrid.SuspendLayout();
            var half = EntityViewerGrid.ClientSize.Width / 2;
            EntityViewerGrid.Columns[0].Width = half;
            EntityViewerGrid.Columns[1].Width = half;
            EntityViewerGrid.ResumeLayout();
        }

        private void EntityViewerGrid_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", e.Font,
                e.Bounds, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        private void EntityViewerGrid_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            e.DrawDefault = true;

            if (e.ItemIndex % 2 == 0)
            {
                e.Item.BackColor = Themer.CurrentThemeColors.AppSoft;
            }
            else
            {
                e.Item.BackColor = Themer.CurrentThemeColors.AppMiddle;
            }
        }

        private readonly static string[] ToolSpriteTextureKeys = ["g_tColorA", "g_tColorB", "g_tColorC", "g_tColor"];

        private static string? GetTextureFromMaterial(Material material)
        {
            foreach (var key in ToolSpriteTextureKeys)
            {
                if (material.TextureParams.TryGetValue(key, out var texturePath) && !string.IsNullOrEmpty(texturePath))
                {
                    return texturePath;
                }
            }

            return null;
        }

        private Bitmap? LoadIconBitmap(string iconPath)
        {
            var materialResource = GuiContext.LoadFileCompiled(iconPath);
            if (materialResource?.DataBlock is not Material material)
            {
                return null;
            }

            var texturePath = GetTextureFromMaterial(material);
            if (string.IsNullOrEmpty(texturePath))
            {
                return null;
            }

            var textureResource = GuiContext.LoadFileCompiled(texturePath);
            if (textureResource?.DataBlock is not Texture texture)
            {
                return null;
            }

            using var skBitmap = texture.GenerateBitmap();
            if (skBitmap == null)
            {
                return null;
            }

            if (EntityIconImageList != null &&
                (skBitmap.Width != EntityIconImageList.ImageSize.Width ||
                 skBitmap.Height != EntityIconImageList.ImageSize.Height))
            {
                using var resized = skBitmap.Resize(
                    new SKImageInfo(EntityIconImageList.ImageSize.Width, EntityIconImageList.ImageSize.Height),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
                );
                return resized?.ToBitmap();
            }

            return skBitmap.ToBitmap();
        }

        private int AddBitmapToImageList(Bitmap bitmap)
        {
            var index = -1;

            if (EntityIconImageList == null)
            {
                return index;
            }

            InvokeWorkaround(() =>
            {
                index = EntityIconImageList.Images.Count;
                MainForm.AddFixedImageToImageList(bitmap, EntityIconImageList);
            });

            return index;
        }

        private int LoadEntityIcon(string classname)
        {
            EntityIconLoadAttempted.Add(classname);

            var hammerEntity = HammerEntities.Get(classname);

            if (hammerEntity?.Icons != null)
            {
                foreach (var iconPath in hammerEntity.Icons)
                {
                    if (!iconPath.EndsWith(".vmat", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    using var bitmap = LoadIconBitmap(iconPath);
                    if (bitmap != null)
                    {
                        var index = AddBitmapToImageList(bitmap);
                        EntityIconCache[classname] = index;
                        return index;
                    }
                }
            }

            return -1;
        }

        private void LoadEntityIcons(HashSet<string> classnames)
        {
            foreach (var classname in classnames)
            {
                var iconIndex = LoadEntityIcon(classname);

                InvokeWorkaround(() =>
                {
                    foreach (ListViewItem item in EntityViewerGrid.Items)
                    {
                        if (item.Tag is Entity entity && entity.GetProperty("classname", string.Empty) == classname)
                        {
                            item.ImageIndex = iconIndex >= 0 ? iconIndex : GetDefaultIconIndexForEntity(entity);
                        }
                    }
                });
            }
        }

        private void InvokeWorkaround(Action action)
        {
            if (InvokeRequired)
            {
                Invoke(action);
            }
            else
            {
                action();
            }
        }
    }
}
