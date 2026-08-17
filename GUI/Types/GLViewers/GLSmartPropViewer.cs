using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace GUI.Types.GLViewers
{
    class GLSmartPropViewer : GLSingleNodeViewer
    {
        private sealed record HierarchyNodeData(
            SceneNode? Node,
            KVObject? Element,
            object? Payload,
            string Label,
            string ClassName,
            int ElementId)
        {
            public string DisplayLabel => Label.Length > 0 ? Label : "noname";
            public string Title => Label.Length > 0 ? $"{Label} ({ClassName})" : ClassName;
        }

        private sealed record ChoiceOptionItem(string DisplayText, string Name)
        {
            public override string ToString() => DisplayText;
        }

        private const int IdColumnWidth = 40;
        private const int ClassColumnWidth = 85;

        private readonly SmartProp smartProp;
        private readonly List<Resource> loadedResources = [];
        private readonly Dictionary<string, KVObject> nestedSmartProps = new(StringComparer.OrdinalIgnoreCase);

        private readonly List<SceneNode> locatorNodes = [];
        private readonly List<SceneNode> rotatorNodes = [];
        private readonly List<SceneNode> sizerNodes = [];
        private readonly List<SceneNode> pickOneNodes = [];
        private readonly List<SceneNode> pathNodes = [];

        private readonly Dictionary<int, List<(SmartPropEvaluatedModel Model, SceneNode Node)>> modelsByElementId = [];
        private readonly Dictionary<int, List<(SmartPropPathInfo Path, SceneNode Node)>> pathsByElementId = [];
        private readonly Dictionary<int, List<(SmartPropWidget Widget, SceneNode Node)>> widgetsByElementId = [];
        private readonly Dictionary<SceneNode, TreeNode> treeNodesBySceneNode = [];

        private TextBox? filterTextBox;
        private Panel? hierarchyHeader;
        private TreeViewDoubleBuffered? hierarchyTree;
        private DataGridView? inspectorGrid;
        private Panel? variablesPanel;
        private Panel? choicesPanel;
        private DataGridView? choiceOverridesGrid;
        private ThemedTabControl? topTabs;
        private ThemedTabControl? bottomTabs;
        private SplitContainer? contentSplitter;
        private SplitContainer? sidebarSplitter;
        private bool selectingFromViewport;
        private bool updatingVariablesUi;
        private readonly List<SmartPropChoice> choices = [];
        private readonly Dictionary<string, string> selectedChoices = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<SmartPropVariableDefinition> variableDefinitions = [];
        private readonly Dictionary<string, object?> activeVariables = new(StringComparer.OrdinalIgnoreCase);

        public GLSmartPropViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, SmartProp smartProp) : base(vrfGuiContext, rendererContext)
        {
            this.smartProp = smartProp;

            variableDefinitions.AddRange(SmartPropVariableMap.ReadVariableDefinitions(smartProp.Data.Root));
            choices.AddRange(SmartPropChoiceMap.ReadChoices(smartProp.Data.Root));
            foreach (var choice in choices)
            {
                var initialOpt = choice.Options.FirstOrDefault(o => string.Equals(o.Name, choice.DefaultOption, StringComparison.OrdinalIgnoreCase))?.Name
                    ?? (choice.Options.Count > 0 ? choice.Options[0].Name : string.Empty);
                selectedChoices[choice.Name] = initialOpt;
            }

            var initialVars = SmartPropVariableMap.Build(smartProp.Data.Root, selectedChoices);
            foreach (var (k, v) in initialVars)
            {
                activeVariables[k] = v;
            }
        }

        protected override void LoadScene()
        {
            base.LoadScene();

            EvaluateScene();
        }

        private void EvaluateScene()
        {
            SmartPropChoiceMap.ApplyChoices(activeVariables, choices, selectedChoices);
            var context = new SmartPropEvaluationContext(activeVariables);
            var result = SmartPropEvaluator.Evaluate(
                smartProp.Data.Root,
                context: context,
                nestedPropResolver: LoadNestedSmartProp);

            modelsByElementId.Clear();
            pathsByElementId.Clear();
            widgetsByElementId.Clear();

            foreach (var model in result.Models)
            {
                var node = CreateModelSceneNode(model);
                if (node != null)
                {
                    if (!modelsByElementId.TryGetValue(model.ElementId, out var list))
                    {
                        list = [];
                        modelsByElementId[model.ElementId] = list;
                    }

                    list.Add((model, node));
                }
            }

            foreach (var path in result.Paths)
            {
                var node = CreatePathSceneNode(path);
                if (!pathsByElementId.TryGetValue(path.ElementId, out var list))
                {
                    list = [];
                    pathsByElementId[path.ElementId] = list;
                }

                list.Add((path, node));
            }

            foreach (var widget in result.Widgets)
            {
                var node = CreateWidgetSceneNode(widget);
                if (node != null)
                {
                    if (!widgetsByElementId.TryGetValue(widget.ElementId, out var list))
                    {
                        list = [];
                        widgetsByElementId[widget.ElementId] = list;
                    }

                    list.Add((widget, node));
                }
            }
        }

        protected override void AddUiControls()
        {
            base.AddUiControls();

            Debug.Assert(UiControl != null);

            using (UiControl.BeginGroup("Widgets"))
            {
                UiControl.AddCheckBox("Locators", true, v => ToggleNodes(locatorNodes, v));
                UiControl.AddCheckBox("Rotators", true, v => ToggleNodes(rotatorNodes, v));
                UiControl.AddCheckBox("Sizers", true, v => ToggleNodes(sizerNodes, v));
                UiControl.AddCheckBox("PickOne Handles", true, v => ToggleNodes(pickOneNodes, v));
                UiControl.AddCheckBox("Paths", true, v => ToggleNodes(pathNodes, v));
            }

            BuildSidebarControls();

            sidebarSplitter = new SplitContainer();
            ((System.ComponentModel.ISupportInitialize)sidebarSplitter).BeginInit();
            sidebarSplitter.Panel1.SuspendLayout();
            sidebarSplitter.Panel2.SuspendLayout();
            sidebarSplitter.SuspendLayout();

            sidebarSplitter.Dock = DockStyle.Fill;
            sidebarSplitter.Orientation = Orientation.Horizontal;
            sidebarSplitter.Panel1MinSize = 0;
            sidebarSplitter.Panel2MinSize = 0;

            sidebarSplitter.Panel1.Controls.Add(topTabs);
            sidebarSplitter.Panel2.Controls.Add(bottomTabs);

            sidebarSplitter.Panel1.ResumeLayout(false);
            sidebarSplitter.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)sidebarSplitter).EndInit();
            sidebarSplitter.ResumeLayout(false);

            contentSplitter = new SplitContainer();
            ((System.ComponentModel.ISupportInitialize)contentSplitter).BeginInit();
            contentSplitter.Panel1.SuspendLayout();
            contentSplitter.Panel2.SuspendLayout();
            contentSplitter.SuspendLayout();

            contentSplitter.Dock = DockStyle.Fill;
            contentSplitter.Orientation = Orientation.Vertical;
            contentSplitter.FixedPanel = FixedPanel.Panel1;
            contentSplitter.Panel1MinSize = 0;
            contentSplitter.Panel2MinSize = 0;

            if (GLControl != null && UiControl.GLControlContainer.Controls.Contains(GLControl))
            {
                UiControl.GLControlContainer.Controls.Remove(GLControl);
                contentSplitter.Panel1.Controls.Add(sidebarSplitter);
                contentSplitter.Panel2.Controls.Add(GLControl);
                UiControl.GLControlContainer.Controls.Add(contentSplitter);
            }

            contentSplitter.Panel1.ResumeLayout(false);
            contentSplitter.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)contentSplitter).EndInit();
            contentSplitter.ResumeLayout(false);

            contentSplitter.SizeChanged += InitializeContentSplitterDistance;
            sidebarSplitter.SizeChanged += InitializeSidebarSplitterDistance;

            PopulateHierarchyTree();
        }

        private int GetClassColumnX() => Math.Max(80, (hierarchyTree?.ClientSize.Width ?? 300) - IdColumnWidth - ClassColumnWidth);
        private int GetIdColumnX() => Math.Max(120, (hierarchyTree?.ClientSize.Width ?? 300) - IdColumnWidth);

        private void BuildSidebarControls()
        {
            topTabs = new ThemedTabControl
            {
                Dock = DockStyle.Fill,
            };

            bottomTabs = new ThemedTabControl
            {
                Dock = DockStyle.Fill,
            };

            filterTextBox = new TextBox
            {
                Dock = DockStyle.Top,
                PlaceholderText = "Filter…",
            };
            filterTextBox.TextChanged += (_, _) => PopulateHierarchyTree(filterTextBox.Text.Trim());
            filterTextBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    filterTextBox.Clear();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };

            hierarchyHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 22,
            };
            hierarchyHeader.Paint += OnHierarchyHeaderPaint;

            hierarchyTree = new TreeViewDoubleBuffered
            {
                Dock = DockStyle.Fill,
                ShowLines = true,
                HideSelection = false,
                DrawMode = TreeViewDrawMode.OwnerDrawText,
                ImageList = AppIcons.ImageList,
            };
            hierarchyTree.DrawNode += OnHierarchyTreeDrawNode;
            hierarchyTree.AfterSelect += OnHierarchyNodeSelected;
            hierarchyTree.NodeMouseDoubleClick += OnHierarchyNodeDoubleClicked;
            hierarchyTree.SizeChanged += (_, _) =>
            {
                hierarchyHeader?.Invalidate();
                hierarchyTree?.Invalidate();
            };

            inspectorGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ColumnHeadersHeight = 24,
            };
            inspectorGrid.Columns.Add("Property", "Property");
            inspectorGrid.Columns.Add("Value", "Value");

            variablesPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
            };

            choicesPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
            };

            choiceOverridesGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ColumnHeadersHeight = 24,
            };
            choiceOverridesGrid.Columns.Add("Variable", "Variable");
            choiceOverridesGrid.Columns.Add("Value", "Value");

            var hierarchyTab = new ThemedTabPage("Hierarchy");
            hierarchyTab.Controls.Add(hierarchyTree);
            hierarchyTab.Controls.Add(hierarchyHeader);
            hierarchyTab.Controls.Add(filterTextBox);
            topTabs.TabPages.Add(hierarchyTab);

            var variablesTab = new ThemedTabPage("Variables");
            variablesTab.Controls.Add(variablesPanel);
            topTabs.TabPages.Add(variablesTab);

            var choicesTab = new ThemedTabPage("Choices");
            choicesTab.Controls.Add(choicesPanel);
            topTabs.TabPages.Add(choicesTab);

            var inspectorTab = new ThemedTabPage("Properties");
            inspectorTab.Controls.Add(inspectorGrid);
            bottomTabs.TabPages.Add(inspectorTab);

            BuildVariablesUi();
            BuildChoicesUi();
        }

        private void OnHierarchyHeaderPaint(object? sender, PaintEventArgs e)
        {
            if (hierarchyHeader == null)
            {
                return;
            }

            var g = e.Graphics;
            var rect = hierarchyHeader.ClientRectangle;

            using (var backBrush = new SolidBrush(Themer.CurrentThemeColors.AppSoft))
            {
                g.FillRectangle(backBrush, rect);
            }

            using (var borderPen = new Pen(Themer.CurrentThemeColors.Border))
            {
                g.DrawLine(borderPen, 0, rect.Bottom - 1, rect.Width, rect.Bottom - 1);
            }

            var classX = GetClassColumnX();
            var idX = GetIdColumnX();
            var font = hierarchyHeader.Font;
            var textColor = Themer.CurrentThemeColors.Contrast;

            System.Windows.Forms.TextRenderer.DrawText(
                g,
                "Label",
                font,
                new Rectangle(6, 0, Math.Max(10, classX - 10), rect.Height),
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            System.Windows.Forms.TextRenderer.DrawText(
                g,
                "Class",
                font,
                new Rectangle(classX, 0, ClassColumnWidth - 4, rect.Height),
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            System.Windows.Forms.TextRenderer.DrawText(
                g,
                "ID",
                font,
                new Rectangle(idX, 0, IdColumnWidth - 4, rect.Height),
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private void OnHierarchyTreeDrawNode(object? sender, DrawTreeNodeEventArgs e)
        {
            if (e.Node == null || e.Bounds.Height <= 0)
            {
                return;
            }

            var g = e.Graphics;
            var isSelected = (e.State & TreeNodeStates.Selected) != 0 || (hierarchyTree != null && hierarchyTree.SelectedNode == e.Node);
            var classX = GetClassColumnX();
            var idX = GetIdColumnX();
            var nodeLeft = e.Node.Bounds.Left;

            var fullWidth = hierarchyTree?.ClientSize.Width ?? e.Bounds.Width;
            var rowRect = new Rectangle(nodeLeft - 2, e.Bounds.Y, Math.Max(0, fullWidth - nodeLeft + 2), e.Bounds.Height);

            if (isSelected)
            {
                using var selBrush = new SolidBrush(Themer.CurrentThemeColors.Accent);
                g.FillRectangle(selBrush, rowRect);
            }

            var data = e.Node.Tag as HierarchyNodeData;
            var labelText = data != null ? data.DisplayLabel : e.Node.Text;
            var isNoName = data != null && data.Label.Length == 0;

            var textColor = isSelected
                ? Themer.CurrentThemeColors.Contrast
                : (isNoName ? Themer.CurrentThemeColors.ContrastSoft : Themer.CurrentThemeColors.Contrast);

            var font = hierarchyTree?.Font ?? SystemFonts.DefaultFont;

            var labelWidth = Math.Max(10, classX - nodeLeft - 6);
            var labelRect = new Rectangle(nodeLeft, e.Bounds.Y, labelWidth, e.Bounds.Height);
            System.Windows.Forms.TextRenderer.DrawText(
                g,
                labelText,
                font,
                labelRect,
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (data != null && data.ClassName.Length > 0 && classX < fullWidth)
            {
                var classWidth = Math.Max(10, idX - classX - 6);
                var classRect = new Rectangle(classX, e.Bounds.Y, classWidth, e.Bounds.Height);
                var classTextColor = isSelected ? Themer.CurrentThemeColors.Contrast : Themer.CurrentThemeColors.ContrastSoft;
                System.Windows.Forms.TextRenderer.DrawText(
                    g,
                    data.ClassName,
                    font,
                    classRect,
                    classTextColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            if (data != null && data.ElementId > 0 && idX < fullWidth)
            {
                var idWidth = Math.Max(10, fullWidth - idX - 4);
                var idRect = new Rectangle(idX, e.Bounds.Y, idWidth, e.Bounds.Height);
                var idTextColor = isSelected ? Themer.CurrentThemeColors.Contrast : Themer.CurrentThemeColors.ContrastSoft;
                System.Windows.Forms.TextRenderer.DrawText(
                    g,
                    data.ElementId.ToString(),
                    font,
                    idRect,
                    idTextColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        private void BuildChoicesUi()
        {
            if (choicesPanel == null)
            {
                return;
            }

            choicesPanel.SuspendLayout();
            choicesPanel.Controls.Clear();

            if (choices.Count == 0)
            {
                var emptyLabel = new Label
                {
                    Dock = DockStyle.Fill,
                    Text = "No configurable choices in this smart prop.",
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Themer.CurrentThemeColors.ContrastSoft,
                };
                choicesPanel.Controls.Add(emptyLabel);
                choicesPanel.ResumeLayout(false);
                return;
            }

            var choiceControlsContainer = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(6, 6, 6, 6),
            };
            choiceControlsContainer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            choiceControlsContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            for (var i = 0; i < choices.Count; i++)
            {
                var choice = choices[i];
                var choiceLabel = new Label
                {
                    Text = choice.Name,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left | AnchorStyles.Top,
                    Margin = new Padding(0, 6, 8, 2),
                    ForeColor = Themer.CurrentThemeColors.Contrast,
                };

                var choiceCombo = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 2, 0, 6),
                };

                foreach (var opt in choice.Options)
                {
                    choiceCombo.Items.Add(new ChoiceOptionItem(opt.DisplayName, opt.Name));
                }

                var currentOptName = selectedChoices.GetValueOrDefault(choice.Name, choice.DefaultOption);
                var selectedIndex = -1;
                for (var optIdx = 0; optIdx < choice.Options.Count; optIdx++)
                {
                    if (string.Equals(choice.Options[optIdx].Name, currentOptName, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = optIdx;
                        break;
                    }
                }

                choiceCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : (choiceCombo.Items.Count > 0 ? 0 : -1);

                var capturedChoiceName = choice.Name;
                choiceCombo.SelectedIndexChanged += (_, _) =>
                {
                    if (choiceCombo.SelectedItem is ChoiceOptionItem item)
                    {
                        if (selectedChoices.GetValueOrDefault(capturedChoiceName) != item.Name)
                        {
                            selectedChoices[capturedChoiceName] = item.Name;
                            ReevaluateSmartProp();
                        }
                    }
                };

                choiceControlsContainer.Controls.Add(choiceLabel, 0, i);
                choiceControlsContainer.Controls.Add(choiceCombo, 1, i);
            }

            var overridesHeader = new Label
            {
                Dock = DockStyle.Top,
                Text = "Variable Overrides",
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 4, 0, 0),
                ForeColor = Themer.CurrentThemeColors.ContrastSoft,
            };

            var overridesContainer = new Panel
            {
                Dock = DockStyle.Fill,
            };
            if (choiceOverridesGrid != null)
            {
                overridesContainer.Controls.Add(choiceOverridesGrid);
            }

            // Add in reverse docking order: Fill first, then Top controls
            choicesPanel.Controls.Add(overridesContainer);
            choicesPanel.Controls.Add(overridesHeader);
            choicesPanel.Controls.Add(choiceControlsContainer);

            choicesPanel.ResumeLayout(false);
            PopulateChoiceOverrides();
        }

        private void PopulateChoiceOverrides()
        {
            if (choiceOverridesGrid == null)
            {
                return;
            }

            choiceOverridesGrid.Rows.Clear();
            foreach (var choice in choices)
            {
                var optName = selectedChoices.GetValueOrDefault(choice.Name, choice.DefaultOption);
                var option = choice.Options.FirstOrDefault(o => string.Equals(o.Name, optName, StringComparison.OrdinalIgnoreCase))
                    ?? (choice.Options.Count > 0 ? choice.Options[0] : null);

                if (option == null)
                {
                    continue;
                }

                foreach (var (varName, value) in option.VariableValues)
                {
                    choiceOverridesGrid.Rows.Add(varName, FormatObjectValue(value));
                }
            }
        }

        private void ReevaluateSmartProp(bool rebuildVariablesUi = true)
        {
            using var lockedGl = MakeCurrent();

            SelectedNodeRenderer?.SelectNode(null);

            Scene.Clear();

            foreach (var resource in loadedResources)
            {
                resource.Dispose();
            }

            loadedResources.Clear();
            nestedSmartProps.Clear();

            locatorNodes.Clear();
            rotatorNodes.Clear();
            sizerNodes.Clear();
            pickOneNodes.Clear();
            pathNodes.Clear();

            inspectorGrid?.Rows.Clear();

            EvaluateScene();

            PopulateHierarchyTree(filterTextBox?.Text.Trim() ?? string.Empty);
            if (rebuildVariablesUi)
            {
                updatingVariablesUi = true;
                BuildVariablesUi();
                updatingVariablesUi = false;
            }
            PopulateChoiceOverrides();

            NotifyVisible();
        }

        private void PopulateHierarchyTree(string filter = "")
        {
            if (hierarchyTree == null)
            {
                return;
            }

            hierarchyTree.BeginUpdate();
            hierarchyTree.Nodes.Clear();
            treeNodesBySceneNode.Clear();

            PopulateElementHierarchy(
                hierarchyTree.Nodes,
                smartProp.Data.Root,
                modelsByElementId,
                pathsByElementId,
                widgetsByElementId,
                new(StringComparer.OrdinalIgnoreCase),
                filter);

            hierarchyTree.ExpandAll();
            hierarchyTree.EndUpdate();
        }

        private void InitializeContentSplitterDistance(object? sender, EventArgs e)
        {
            var splitter = contentSplitter;
            if (splitter == null || splitter.Width <= 400)
            {
                return;
            }

            // SizeChanged is only used to wait until WinForms has assigned real bounds.
            splitter.SizeChanged -= InitializeContentSplitterDistance;
            var maximum = splitter.Width - splitter.Panel2MinSize - splitter.SplitterWidth;
            splitter.SplitterDistance = Math.Clamp(320, splitter.Panel1MinSize, maximum);
        }

        private void InitializeSidebarSplitterDistance(object? sender, EventArgs e)
        {
            var splitter = sidebarSplitter;
            if (splitter == null || splitter.Height <= 200)
            {
                return;
            }

            splitter.SizeChanged -= InitializeSidebarSplitterDistance;
            var maximum = splitter.Height - splitter.Panel2MinSize - splitter.SplitterWidth;
            splitter.SplitterDistance = Math.Clamp(splitter.Height / 2, splitter.Panel1MinSize, maximum);
        }

        private ModelSceneNode? CreateModelSceneNode(SmartPropEvaluatedModel model)
        {
            if (model.ModelName.Length == 0)
            {
                return null;
            }

            var resource = GuiContext.LoadFileCompiled(model.ModelName);
            if (resource?.DataBlock is not Model modelBlock)
            {
                resource?.Dispose();
                return null;
            }

            loadedResources.Add(resource);

            var modelSceneNode = new ModelSceneNode(Scene, modelBlock, skin: model.MaterialGroup)
            {
                Transform = model.WorldMatrix,
            };

            if (model.TintColor.HasValue)
            {
                modelSceneNode.Tint = model.TintColor.Value;
            }

            Scene.Add(modelSceneNode, false);
            return modelSceneNode;
        }

        private SmartPropPathSceneNode CreatePathSceneNode(SmartPropPathInfo path)
        {
            var pathSceneNode = new SmartPropPathSceneNode(Scene, path);
            Scene.Add(pathSceneNode, false);
            pathNodes.Add(pathSceneNode);
            return pathSceneNode;
        }

        private SceneNode? CreateWidgetSceneNode(SmartPropWidget widget)
        {
            var sceneNode = widget switch
            {
                SmartPropLocatorWidget locator => Track(new SmartPropLocatorSceneNode(Scene, locator), locatorNodes),
                SmartPropRotatorWidget rotator => Track(new SmartPropRotatorSceneNode(Scene, rotator), rotatorNodes),
                SmartPropSizerWidget sizer => Track(new SmartPropSizerSceneNode(Scene, sizer), sizerNodes),
                SmartPropPickOneHandleWidget pickOne => Track(new SmartPropPickOneSceneNode(Scene, pickOne), pickOneNodes),
                _ => null,
            };

            if (sceneNode == null)
            {
                return null;
            }

            Scene.Add(sceneNode, false);
            return sceneNode;
        }

        private static SceneNode Track(SceneNode node, List<SceneNode> list)
        {
            list.Add(node);
            return node;
        }

        private KVObject? LoadNestedSmartProp(string path)
        {
            if (nestedSmartProps.TryGetValue(path, out var root))
            {
                return root;
            }

            var nested = GuiContext.LoadFileCompiled(path);
            if (nested?.DataBlock is SmartProp nestedProp)
            {
                loadedResources.Add(nested);
                root = nestedProp.Data.Root;
                nestedSmartProps[path] = root;
                return root;
            }

            nested?.Dispose();
            return null;
        }

        private void BuildVariablesUi()
        {
            if (variablesPanel == null)
            {
                return;
            }

            variablesPanel.SuspendLayout();
            variablesPanel.Controls.Clear();

            if (variableDefinitions.Count == 0)
            {
                var emptyLabel = new Label
                {
                    Dock = DockStyle.Fill,
                    Text = "No configurable variables in this smart prop.",
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Themer.CurrentThemeColors.ContrastSoft,
                };
                variablesPanel.Controls.Add(emptyLabel);
                variablesPanel.ResumeLayout(false);
                return;
            }

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(6, 6, 6, 6),
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            for (var i = 0; i < variableDefinitions.Count; i++)
            {
                var variable = variableDefinitions[i];
                var displayName = variable.DisplayName ?? variable.Name;
                var varLabel = new Label
                {
                    Text = displayName,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left | AnchorStyles.Top,
                    Margin = new Padding(0, 6, 8, 2),
                    ForeColor = Themer.CurrentThemeColors.Contrast,
                };

                Control control;
                var currentVal = activeVariables.GetValueOrDefault(variable.Name, variable.DefaultValue);

                switch (variable.Type)
                {
                    case "Int":
                    {
                        var min = (decimal)(variable.MinValue ?? -1000000);
                        var max = (decimal)(variable.MaxValue ?? 1000000);
                        var valInt = currentVal is int ci ? ci : (int)(currentVal is float cf ? cf : 0);
                        var num = new NumericUpDown
                        {
                            DecimalPlaces = 0,
                            Minimum = min,
                            Maximum = max,
                            Value = Math.Clamp((decimal)valInt, min, max),
                            Dock = DockStyle.Fill,
                            Margin = new Padding(0, 2, 0, 6),
                        };
                        num.ValueChanged += (_, _) =>
                        {
                            if (!updatingVariablesUi)
                            {
                                activeVariables[variable.Name] = (int)num.Value;
                                ReevaluateSmartProp(false);
                            }
                        };
                        control = num;
                        break;
                    }

                    case "Float":
                    {
                        var min = (decimal)(variable.MinValue ?? -1000000);
                        var max = (decimal)(variable.MaxValue ?? 1000000);
                        var valFloat = currentVal is float cf ? cf : (currentVal is int ci ? ci : 0f);
                        var num = new NumericUpDown
                        {
                            DecimalPlaces = 2,
                            Increment = 0.1m,
                            Minimum = min,
                            Maximum = max,
                            Value = Math.Clamp((decimal)valFloat, min, max),
                            Dock = DockStyle.Fill,
                            Margin = new Padding(0, 2, 0, 6),
                        };
                        num.ValueChanged += (_, _) =>
                        {
                            if (!updatingVariablesUi)
                            {
                                activeVariables[variable.Name] = (float)num.Value;
                                ReevaluateSmartProp(false);
                            }
                        };
                        control = num;
                        break;
                    }

                    case "Bool":
                    {
                        var chk = new CheckBox
                        {
                            Text = "Enabled",
                            AutoSize = true,
                            Checked = currentVal is bool b && b,
                            Margin = new Padding(0, 4, 0, 6),
                        };
                        chk.CheckedChanged += (_, _) =>
                        {
                            if (!updatingVariablesUi)
                            {
                                activeVariables[variable.Name] = chk.Checked;
                                ReevaluateSmartProp(false);
                            }
                        };
                        control = chk;
                        break;
                    }

                    case "Color":
                    {
                        var panel = new Panel
                        {
                            Height = 26,
                            Dock = DockStyle.Fill,
                            Margin = new Padding(0, 2, 0, 6),
                        };
                        var swatch = new Button
                        {
                            Width = 32,
                            Height = 22,
                            Location = new Point(0, 2),
                            FlatStyle = FlatStyle.Flat,
                            Cursor = Cursors.Hand,
                        };
                        swatch.FlatAppearance.BorderSize = 1;
                        swatch.FlatAppearance.BorderColor = Themer.CurrentThemeColors.Border;

                        var rgb = currentVal is float[] fArr && fArr.Length >= 3 ? fArr : [255f, 255f, 255f];
                        var rInt = (int)Math.Clamp(rgb[0] > 1f ? rgb[0] : rgb[0] * 255f, 0, 255);
                        var gInt = (int)Math.Clamp(rgb[1] > 1f ? rgb[1] : rgb[1] * 255f, 0, 255);
                        var bInt = (int)Math.Clamp(rgb[2] > 1f ? rgb[2] : rgb[2] * 255f, 0, 255);
                        swatch.BackColor = System.Drawing.Color.FromArgb(255, rInt, gInt, bInt);

                        var rgbLabel = new Label
                        {
                            Location = new Point(38, 5),
                            AutoSize = true,
                            Text = $"{rInt}, {gInt}, {bInt}",
                            ForeColor = Themer.CurrentThemeColors.Contrast,
                        };

                        swatch.Click += (_, _) =>
                        {
                            using var dlg = new ColorDialog { Color = swatch.BackColor };
                            if (dlg.ShowDialog() == DialogResult.OK)
                            {
                                swatch.BackColor = dlg.Color;
                                rgbLabel.Text = $"{dlg.Color.R}, {dlg.Color.G}, {dlg.Color.B}";
                                activeVariables[variable.Name] = new float[] { dlg.Color.R, dlg.Color.G, dlg.Color.B };
                                ReevaluateSmartProp(false);
                            }
                        };

                        panel.Controls.Add(swatch);
                        panel.Controls.Add(rgbLabel);
                        control = panel;
                        break;
                    }

                    case "MaterialGroup":
                    {
                        var combo = new ComboBox
                        {
                            DropDownStyle = ComboBoxStyle.DropDown,
                            Dock = DockStyle.Fill,
                            Margin = new Padding(0, 2, 0, 6),
                        };

                        if (variable.ModelName != null)
                        {
                            var modelRes = GuiContext.LoadFileCompiled(variable.ModelName);
                            if (modelRes?.DataBlock is Model modelBlock)
                            {
                                foreach (var group in modelBlock.GetMaterialGroups())
                                {
                                    combo.Items.Add(group.Name);
                                }
                            }

                            modelRes?.Dispose();
                        }

                        combo.Text = currentVal?.ToString() ?? string.Empty;
                        combo.SelectedIndexChanged += (_, _) =>
                        {
                            if (!updatingVariablesUi)
                            {
                                activeVariables[variable.Name] = combo.Text;
                                ReevaluateSmartProp(false);
                            }
                        };
                        combo.TextChanged += (_, _) =>
                        {
                            if (!updatingVariablesUi)
                            {
                                activeVariables[variable.Name] = combo.Text;
                                ReevaluateSmartProp(false);
                            }
                        };
                        control = combo;
                        break;
                    }

                    default:
                    {
                        var txt = new TextBox
                        {
                            Dock = DockStyle.Fill,
                            Text = currentVal is float[] fa ? string.Join(", ", fa) : (currentVal?.ToString() ?? string.Empty),
                            Margin = new Padding(0, 2, 0, 6),
                        };
                        txt.TextChanged += (_, _) =>
                        {
                            if (!updatingVariablesUi)
                            {
                                activeVariables[variable.Name] = txt.Text;
                                ReevaluateSmartProp(false);
                            }
                        };
                        control = txt;
                        break;
                    }
                }

                table.Controls.Add(varLabel, 0, i);
                table.Controls.Add(control, 1, i);
            }

            variablesPanel.Controls.Add(table);
            variablesPanel.ResumeLayout(false);
        }

        private static string FormatObjectValue(object? value) => value switch
        {
            null => "<unset>",
            bool b => b ? "true" : "false",
            float[] v => string.Join(", ", v),
            KVObject kv => FormatValue(kv),
            _ => value.ToString() ?? string.Empty,
        };

        private bool PopulateElementHierarchy(
            TreeNodeCollection parentNodes,
            KVObject element,
            Dictionary<int, List<(SmartPropEvaluatedModel Model, SceneNode Node)>> modelsByElementId,
            Dictionary<int, List<(SmartPropPathInfo Path, SceneNode Node)>> pathsByElementId,
            Dictionary<int, List<(SmartPropWidget Widget, SceneNode Node)>> widgetsByElementId,
            HashSet<string> activeNestedProps,
            string filter)
        {
            var className = SmartPropModifierEvaluator.GetClassName(element);
            var elementId = GetInt32(element, "m_nElementID");

            // If this is the root container (CSmartPropRoot or unnamed root with m_Children), process its children directly
            if ((className is "Root" or "CSmartPropRoot" or "" || elementId == 0) && element.TryGetValue("m_Children", out var rootChildren) && rootChildren.IsArray)
            {
                var anyRootMatch = false;
                if (widgetsByElementId.TryGetValue(elementId, out var rootWidgetList))
                {
                    anyRootMatch |= AddWidgetTreeNodes(parentNodes, element, rootWidgetList, filter);
                }

                foreach (var child in rootChildren.AsArraySpan())
                {
                    if (child.ValueType == KVValueType.Collection)
                    {
                        anyRootMatch |= PopulateElementHierarchy(parentNodes, child, modelsByElementId, pathsByElementId, widgetsByElementId, activeNestedProps, filter);
                    }
                }

                return anyRootMatch;
            }

            var label = GetString(element, "m_sLabel");
            if (className.Length == 0)
            {
                className = "Element";
            }

            var selfMatches = filter.Length == 0
                || label.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || className.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (elementId > 0 && elementId.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase));

            var imageIndex = className switch
            {
                "Model" or "ModelEntity" or "PropPhysics" or "PropDynamic" => AppIcons.GetImageIndexForExtension(".vmdl_c"),
                "SmartProp" or "PlaceOnPath" => AppIcons.GetImageIndexForExtension(".vsmart_c"),
                "Group" or "Category" => AppIcons.Icons.GetValueOrDefault("Folder", -1),
                _ => AppIcons.Icons.GetValueOrDefault("cube", AppIcons.Icons.GetValueOrDefault("File", 0)),
            };

            SceneNode? primarySceneNode = null;
            object? primaryPayload = null;

            modelsByElementId.TryGetValue(elementId, out var modelList);
            pathsByElementId.TryGetValue(elementId, out var pathList);

            if (modelList != null && modelList.Count > 0)
            {
                primarySceneNode = modelList[0].Node;
                primaryPayload = modelList[0].Model;
            }
            else if (pathList != null && pathList.Count > 0)
            {
                primarySceneNode = pathList[0].Node;
                primaryPayload = pathList[0].Path;
            }

            var nodeData = new HierarchyNodeData(
                primarySceneNode,
                element,
                primaryPayload ?? element,
                label,
                className,
                elementId);

            var elementNode = new TreeNode(nodeData.DisplayLabel)
            {
                ImageIndex = imageIndex,
                SelectedImageIndex = imageIndex,
                Tag = nodeData,
            };

            var hasMatchingDescendant = false;

            // Add any widgets emitted by this element's modifiers as child nodes
            if (widgetsByElementId.TryGetValue(elementId, out var widgetList))
            {
                hasMatchingDescendant |= AddWidgetTreeNodes(elementNode.Nodes, element, widgetList, filter);
            }

            // Expand nested smart props recursively
            if (className == "SmartProp")
            {
                var nestedPath = GetString(element, "m_sSmartProp");
                if (nestedPath.Length > 0 && activeNestedProps.Add(nestedPath))
                {
                    var nestedRoot = LoadNestedSmartProp(nestedPath);
                    if (nestedRoot != null)
                    {
                        hasMatchingDescendant |= PopulateElementHierarchy(elementNode.Nodes, nestedRoot, modelsByElementId, pathsByElementId, widgetsByElementId, activeNestedProps, filter);
                    }

                    activeNestedProps.Remove(nestedPath);
                }
            }

            // Recursively populate child elements
            if (element.TryGetValue("m_Children", out var children) && children.IsArray)
            {
                foreach (var child in children.AsArraySpan())
                {
                    if (child.ValueType == KVValueType.Collection)
                    {
                        hasMatchingDescendant |= PopulateElementHierarchy(elementNode.Nodes, child, modelsByElementId, pathsByElementId, widgetsByElementId, activeNestedProps, filter);
                    }
                }
            }

            if (!selfMatches && !hasMatchingDescendant)
            {
                return false;
            }

            if (modelList != null)
            {
                foreach (var (_, node) in modelList)
                {
                    treeNodesBySceneNode[node] = elementNode;
                }
            }

            if (pathList != null)
            {
                foreach (var (_, node) in pathList)
                {
                    treeNodesBySceneNode[node] = elementNode;
                }
            }

            parentNodes.Add(elementNode);
            return true;
        }

        private bool AddWidgetTreeNodes(TreeNodeCollection parentNodes, KVObject element, List<(SmartPropWidget Widget, SceneNode Node)> widgetList, string filter)
        {
            var anyMatch = false;
            var cubeIcon = AppIcons.Icons.GetValueOrDefault("cube", AppIcons.Icons.GetValueOrDefault("File", 0));
            foreach (var (widget, node) in widgetList)
            {
                var kind = widget switch
                {
                    SmartPropLocatorWidget => "Locator",
                    SmartPropRotatorWidget => "Rotator",
                    SmartPropSizerWidget => "Sizer",
                    _ => "PickOne",
                };

                var matches = filter.Length == 0
                    || widget.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || kind.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || (widget.ElementId > 0 && widget.ElementId.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase));

                if (!matches)
                {
                    continue;
                }

                var widgetData = new HierarchyNodeData(
                    node,
                    element,
                    widget,
                    widget.Name,
                    kind,
                    widget.ElementId);

                var widgetNode = new TreeNode(widgetData.DisplayLabel)
                {
                    ImageIndex = cubeIcon,
                    SelectedImageIndex = cubeIcon,
                    Tag = widgetData,
                };
                treeNodesBySceneNode[node] = widgetNode;
                parentNodes.Add(widgetNode);
                anyMatch = true;
            }

            return anyMatch;
        }

        private static void ToggleNodes(List<SceneNode> nodes, bool enabled)
        {
            foreach (var node in nodes)
            {
                node.LayerEnabled = enabled;
            }
        }

        private void OnHierarchyNodeSelected(object? sender, TreeViewEventArgs e)
        {
            if (selectingFromViewport || e.Node?.Tag is not HierarchyNodeData nodeData)
            {
                return;
            }

            SelectedNodeRenderer?.SelectNode(nodeData.Node);
            FillInspector(nodeData);
        }

        private void OnHierarchyNodeDoubleClicked(object? sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node?.Tag is not HierarchyNodeData nodeData || nodeData.Node == null)
            {
                return;
            }

            var bounds = nodeData.Node.BoundingBox;
            var extent = MathF.Max(bounds.Size.Length(), 1f);
            var direction = Vector3.Normalize(Input.Camera.Location - bounds.Center);
            if (!float.IsFinite(direction.X))
            {
                direction = Vector3.Normalize(new Vector3(1f, -1f, 1f));
            }

            Input.Camera.SetLocation(bounds.Center + (direction * (extent * 1.6f)));
            Input.Camera.LookAt(bounds.Center);
        }

        protected override void OnPicked(object? sender, PickingTexture.PickingResponse pickingResponse)
        {
            if (pickingResponse.PixelInfo.ObjectId == 0)
            {
                SelectedNodeRenderer?.SelectNode(null);
                return;
            }

            if (pickingResponse.Intent != PickingTexture.PickingIntent.Select)
            {
                return;
            }

            var sceneNode = Scene.Find(pickingResponse.PixelInfo.ObjectId);
            SelectedNodeRenderer?.SelectNode(sceneNode);

            if (hierarchyTree == null || inspectorGrid == null)
            {
                return;
            }

            if (sceneNode != null && treeNodesBySceneNode.TryGetValue(sceneNode, out var treeNode))
            {
                selectingFromViewport = true;
                try
                {
                    hierarchyTree.SelectedNode = treeNode;
                    treeNode.EnsureVisible();
                    if (treeNode.Tag is HierarchyNodeData nodeData)
                    {
                        FillInspector(nodeData);
                    }
                }
                finally
                {
                    selectingFromViewport = false;
                }
            }
        }

        private void FillInspector(HierarchyNodeData selection)
        {
            if (inspectorGrid == null)
            {
                return;
            }

            inspectorGrid.Rows.Clear();

            inspectorGrid.Rows.Add("Selection", selection.Title);
            inspectorGrid.Rows.Add("Class", selection.ClassName);
            if (selection.Label.Length > 0)
            {
                inspectorGrid.Rows.Add("Label", selection.Label);
            }
            if (selection.ElementId > 0)
            {
                inspectorGrid.Rows.Add("Element ID", selection.ElementId.ToString());
            }
            if (selection.Node != null)
            {
                inspectorGrid.Rows.Add("Picking Id", selection.Node.Id.ToString());
            }

            switch (selection.Payload)
            {
                case SmartPropEvaluatedModel model:
                    inspectorGrid.Rows.Add("Model", model.ModelName);
                    inspectorGrid.Rows.Add("Position", FormatVector(model.Position));
                    inspectorGrid.Rows.Add("Rotation", FormatVector(model.PitchYawRoll));
                    inspectorGrid.Rows.Add("Scale", FormatVector(model.Scale));
                    break;

                case SmartPropPathInfo path:
                    inspectorGrid.Rows.Add("Control Points", path.ControlPoints.Length.ToString());
                    inspectorGrid.Rows.Add("Curve Samples", path.CurveSamples.Length.ToString());
                    break;

                case SmartPropLocatorWidget locator:
                    inspectorGrid.Rows.Add("Position", FormatVector(locator.Position));
                    inspectorGrid.Rows.Add("Rotation", FormatVector(locator.PitchYawRoll));
                    inspectorGrid.Rows.Add("Offset", FormatVector(locator.Offset));
                    inspectorGrid.Rows.Add("Display Scale", locator.DisplayScale.ToString());
                    break;

                case SmartPropRotatorWidget rotator:
                    inspectorGrid.Rows.Add("Position", FormatVector(rotator.Position));
                    inspectorGrid.Rows.Add("Rotation", FormatVector(rotator.PitchYawRoll));
                    inspectorGrid.Rows.Add("Offset", FormatVector(rotator.Offset));
                    inspectorGrid.Rows.Add("Axis", FormatVector(rotator.Axis));
                    inspectorGrid.Rows.Add("Radius", rotator.Radius.ToString());
                    inspectorGrid.Rows.Add("Angle", rotator.Angle.ToString());
                    inspectorGrid.Rows.Add("Color", FormatVector(rotator.Color));
                    break;

                case SmartPropSizerWidget sizer:
                    inspectorGrid.Rows.Add("Position", FormatVector(sizer.Position));
                    inspectorGrid.Rows.Add("Rotation", FormatVector(sizer.PitchYawRoll));
                    inspectorGrid.Rows.Add("Min Bounds", FormatVector(sizer.MinBounds));
                    inspectorGrid.Rows.Add("Max Bounds", FormatVector(sizer.MaxBounds));
                    inspectorGrid.Rows.Add("Active Axes", $"X: {sizer.ActiveAxes.X}, Y: {sizer.ActiveAxes.Y}, Z: {sizer.ActiveAxes.Z}");
                    break;

                case SmartPropPickOneHandleWidget pickOne:
                    inspectorGrid.Rows.Add("Position", FormatVector(pickOne.Position));
                    inspectorGrid.Rows.Add("Rotation", FormatVector(pickOne.PitchYawRoll));
                    inspectorGrid.Rows.Add("Offset", FormatVector(pickOne.Offset));
                    inspectorGrid.Rows.Add("Size", pickOne.Size.ToString());
                    inspectorGrid.Rows.Add("Color", FormatVector(pickOne.Color));
                    inspectorGrid.Rows.Add("Shape", pickOne.Shape);
                    break;

                case SmartPropWidget widget:
                    inspectorGrid.Rows.Add("Position", FormatVector(widget.Position));
                    inspectorGrid.Rows.Add("Rotation", FormatVector(widget.PitchYawRoll));
                    break;
            }

            if (selection.Element != null)
            {
                foreach (var key in selection.Element.Keys)
                {
                    if (key is "m_Modifiers" or "m_SelectionCriteria" or "m_Children" or "generic_data_type" or "_class" or "_editor")
                    {
                        continue;
                    }

                    if (!selection.Element.TryGetValue(key, out var value))
                    {
                        continue;
                    }

                    inspectorGrid.Rows.Add(key, FormatValue(value));
                }

                if (selection.Element.TryGetValue("m_SelectionCriteria", out var criteriaNode) && criteriaNode.IsArray && criteriaNode.Count > 0)
                {
                    var span = criteriaNode.AsArraySpan();
                    for (var i = 0; i < span.Length; i++)
                    {
                        var crit = span[i];
                        if (crit.ValueType != KVValueType.Collection)
                        {
                            continue;
                        }

                        var critType = SmartPropModifierEvaluator.GetClassName(crit);
                        if (critType.Length == 0)
                        {
                            critType = $"Criteria #{i + 1}";
                        }

                        inspectorGrid.Rows.Add($"Criteria [{i}]", critType);

                        foreach (var key in crit.Keys)
                        {
                            if (key is "generic_data_type" or "_class" or "_editor")
                            {
                                continue;
                            }

                            if (crit.TryGetValue(key, out var critVal))
                            {
                                inspectorGrid.Rows.Add($"  {key}", FormatValue(critVal));
                            }
                        }
                    }
                }

                if (selection.Element.TryGetValue("m_Modifiers", out var modifiersNode) && modifiersNode.IsArray && modifiersNode.Count > 0)
                {
                    var span = modifiersNode.AsArraySpan();
                    for (var i = 0; i < span.Length; i++)
                    {
                        var mod = span[i];
                        if (mod.ValueType != KVValueType.Collection)
                        {
                            continue;
                        }

                        var modType = SmartPropModifierEvaluator.GetClassName(mod);
                        if (modType.Length == 0)
                        {
                            modType = $"Modifier #{i + 1}";
                        }

                        var isEnabled = true;
                        if (mod.TryGetValue("m_bEnabled", out var enabledNode) && enabledNode.ValueType == KVValueType.Boolean)
                        {
                            isEnabled = (bool)enabledNode;
                        }

                        var modHeader = isEnabled ? modType : $"{modType} (Disabled)";
                        inspectorGrid.Rows.Add($"Modifier [{i}]", modHeader);

                        foreach (var key in mod.Keys)
                        {
                            if (key is "generic_data_type" or "_class" or "_editor")
                            {
                                continue;
                            }

                            if (mod.TryGetValue(key, out var modVal))
                            {
                                inspectorGrid.Rows.Add($"  {key}", FormatValue(modVal));
                            }
                        }
                    }
                }
            }
        }

        private static string FormatValue(KVObject? value)
        {
            if (value == null)
            {
                return "<null>";
            }

            switch (value.ValueType)
            {
                case KVValueType.String:
                    return (string)value;
                case KVValueType.Boolean:
                    return (bool)value ? "true" : "false";
                case KVValueType.Int32:
                case KVValueType.Int64:
                case KVValueType.UInt64:
                case KVValueType.FloatingPoint:
                    return value.ToString() ?? string.Empty;
                case KVValueType.Array:
                {
                    var span = value.AsArraySpan();
                    if (span.Length == 0)
                    {
                        return "[ ]";
                    }

                    var allPrimitives = true;
                    for (var i = 0; i < span.Length; i++)
                    {
                        if (span[i].ValueType is KVValueType.Collection or KVValueType.Array)
                        {
                            allPrimitives = false;
                            break;
                        }
                    }

                    if (allPrimitives && span.Length <= 4)
                    {
                        return string.Join(", ", span.ToArray().Select(FormatValue));
                    }

                    return $"[ {span.Length} items ]";
                }
                case KVValueType.Collection:
                {
                    var hasExpr = value.TryGetValue("m_Expression", out var exprNode) && exprNode.ValueType == KVValueType.String;
                    var hasSource = value.TryGetValue("m_SourceName", out var srcNode) && srcNode.ValueType == KVValueType.String;
                    var hasTarget = value.TryGetValue("m_TargetName", out var tgtNode) && tgtNode.ValueType == KVValueType.String;
                    var hasVal = value.TryGetValue("m_Value", out var valNode);

                    if (hasExpr && hasSource)
                    {
                        return $"Expr: \"{exprNode}\" (Var: {srcNode})";
                    }
                    if (hasExpr)
                    {
                        return $"Expr: \"{exprNode}\"";
                    }
                    if (hasSource)
                    {
                        return $"Var: {srcNode}";
                    }
                    if (hasTarget && hasVal)
                    {
                        return $"{tgtNode} = {FormatValue(valNode)}";
                    }
                    if (hasVal)
                    {
                        return FormatValue(valNode);
                    }

                    var className = SmartPropModifierEvaluator.GetClassName(value);
                    if (className.Length > 0)
                    {
                        return className;
                    }

                    return "{ ... }";
                }
                default:
                    return value.ToString() ?? string.Empty;
            }
        }

        private static string FormatVector(Vector3 vector)
            => $"{vector.X:0.###}, {vector.Y:0.###}, {vector.Z:0.###}";

        private static string LeafName(string fullPath)
        {
            var lastIndex = fullPath.LastIndexOf('/');
            return lastIndex >= 0 ? fullPath[(lastIndex + 1)..] : fullPath;
        }

        private static string GetString(KVObject node, string key, string fallback = "")
        {
            return node.TryGetValue(key, out var value) && value.ValueType == KVValueType.String
                ? (string)value
                : fallback;
        }

        private static int GetInt32(KVObject node, string key)
        {
            return node.TryGetValue(key, out var value) && value.ValueType == KVValueType.Int32
                ? (int)value
                : 0;
        }

        public override void Dispose()
        {
            base.Dispose();

            if (contentSplitter != null)
            {
                contentSplitter.SizeChanged -= InitializeContentSplitterDistance;
            }

            if (sidebarSplitter != null)
            {
                sidebarSplitter.SizeChanged -= InitializeSidebarSplitterDistance;
            }

            filterTextBox?.Dispose();
            hierarchyHeader?.Dispose();
            hierarchyTree?.Dispose();
            inspectorGrid?.Dispose();
            variablesPanel?.Dispose();
            choicesPanel?.Dispose();
            choiceOverridesGrid?.Dispose();
            topTabs?.Dispose();
            bottomTabs?.Dispose();
            sidebarSplitter?.Dispose();
            contentSplitter?.Dispose();

            foreach (var resource in loadedResources)
            {
                resource.Dispose();
            }

            loadedResources.Clear();
            nestedSmartProps.Clear();
        }
    }
}
