using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
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

        private sealed record LocatorEditState(Vector3 Position, Vector3 Angles, float Scale);
        private sealed record SizerEditState(Vector3 MinBounds, Vector3 MaxBounds);
        private sealed record HandlerListItem(SmartPropWidget Widget, int Occurrence, string DisplayText)
        {
            public override string ToString() => DisplayText;
        }
        private sealed record SizerDragState(string Variable, float InitialValue, Vector2 ScreenDirection, float PixelsPerUnit, Point Start);

        private const int IdColumnWidth = 40;
        private const int ClassColumnWidth = 85;
        private const string LocatorModelName = "models/editor/axis_helper_thick.vmdl";

        private readonly SmartProp smartProp;
        private readonly List<Resource> loadedResources = [];
        private readonly Dictionary<string, KVObject> nestedSmartProps = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<SceneNode> locatorNodes = [];
        private readonly List<SceneNode> rotatorNodes = [];
        private readonly List<SceneNode> sizerNodes = [];
        private readonly List<SceneNode> pickOneNodes = [];
        private readonly List<SceneNode> pathNodes = [];

        private readonly Dictionary<int, List<(SmartPropEvaluatedModel Model, SceneNode Node)>> modelsByElementId = [];
        private readonly Dictionary<int, List<(SmartPropPathInfo Path, SceneNode? Node)>> pathsByElementId = [];
        private readonly Dictionary<int, List<(SmartPropWidget Widget, SceneNode? Node)>> widgetsByElementId = [];
        private readonly Dictionary<SceneNode, TreeNode> treeNodesBySceneNode = [];
        private readonly Dictionary<int, KVObject> elementsByElementId = [];
        private readonly List<SmartPropWidget> overlayWidgets = [];

        private TextBox? filterTextBox;
        private Panel? hierarchyHeader;
        private TreeViewDoubleBuffered? hierarchyTree;
        private DataGridView? inspectorGrid;
        private TextBox? rawSelectionTextBox;
        private CheckBox? showRawSelectionCheckBox;
        private HierarchyNodeData? inspectorSelection;
        private Panel? variablesPanel;
        private DataGridView? variablesGrid;
        private Panel? choicesPanel;
        private DataGridView? choiceOverridesGrid;
        private ThemedGroupBox? parametersGroup;
        private ThemedGroupBox? handlersGroup;
        private ListBox? handlerList;
        private Panel? handlerPropertiesPanel;
        public SplitContainer? StructureControl { get; private set; }
        public SplitContainer? VariablesControl { get; private set; }
        private bool selectingFromViewport;
        private bool updatingVariablesUi;
        private bool reevaluatingSmartProp;
        private bool reevaluationPending;
        private bool pendingVariablesUiRebuild;
        private int? activePickOneElementId;
        private int activePickOneChildCount;
        private SmartPropWidget? activeWidget;
        private bool draggingWidget;
        private Point widgetDragPoint;
        private float widgetDragInitialAngle;
        private LocatorEditState? locatorDragInitialState;
        private SizerDragState? sizerDragState;
        private readonly List<SmartPropChoice> choices = [];
        private readonly Dictionary<string, string> selectedChoices = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<SmartPropVariableDefinition> variableDefinitions = [];
        private readonly Dictionary<string, object?> activeVariables = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, int> pickOneSelections = [];
        private readonly Dictionary<string, float> widgetOutputValues = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, LocatorEditState> locatorEdits = [];
        private readonly Dictionary<int, float> rotatorEdits = [];
        private readonly Dictionary<int, SizerEditState> sizerEdits = [];
        private bool updatingHandlersUi;

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
            var context = new SmartPropEvaluationContext(activeVariables, pickOneSelections: pickOneSelections, widgetOutputValues: widgetOutputValues);
            var result = SmartPropEvaluator.Evaluate(
                smartProp.Data.Root,
                context: context,
                nestedPropResolver: LoadNestedSmartProp);

            if (InitializeSizerOutputs(result.Widgets))
            {
                context = new SmartPropEvaluationContext(activeVariables, pickOneSelections: pickOneSelections, widgetOutputValues: widgetOutputValues);
                result = SmartPropEvaluator.Evaluate(
                    smartProp.Data.Root,
                    context: context,
                    nestedPropResolver: LoadNestedSmartProp);
            }

            modelsByElementId.Clear();
            pathsByElementId.Clear();
            widgetsByElementId.Clear();
            overlayWidgets.Clear();

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

            foreach (var evaluatedWidget in result.Widgets)
            {
                var widget = ApplyWidgetEdit(evaluatedWidget);
                if (activeWidget != null && activeWidget.ElementId == widget.ElementId && activeWidget.GetType() == widget.GetType())
                {
                    activeWidget = widget;
                }
                var node = CreateWidgetSceneNode(widget);
                if (node != null && !widgetsByElementId.TryGetValue(widget.ElementId, out var list))
                {
                    list = [];
                    widgetsByElementId[widget.ElementId] = list;
                }

                if (node != null)
                {
                    widgetsByElementId[widget.ElementId].Add((widget, node));
                }
                overlayWidgets.Add(widget);
            }
        }

        private bool InitializeSizerOutputs(IReadOnlyList<SmartPropWidget> widgets)
        {
            var initialized = false;
            foreach (var sizer in widgets.OfType<SmartPropSizerWidget>())
            {
                initialized |= InitializeOutput(sizer.MinXVariable, sizer.MinBounds.X);
                initialized |= InitializeOutput(sizer.MaxXVariable, sizer.MaxBounds.X);
                initialized |= InitializeOutput(sizer.MinYVariable, sizer.MinBounds.Y);
                initialized |= InitializeOutput(sizer.MaxYVariable, sizer.MaxBounds.Y);
                initialized |= InitializeOutput(sizer.MinZVariable, sizer.MinBounds.Z);
                initialized |= InitializeOutput(sizer.MaxZVariable, sizer.MaxBounds.Z);
            }

            return initialized;

            bool InitializeOutput(string variable, float value)
            {
                if (variable.Length == 0 || widgetOutputValues.ContainsKey(variable))
                {
                    return false;
                }

                widgetOutputValues[variable] = value;
                SetActiveVariableValue(variable, value);
                return true;
            }
        }

        protected override void AddUiControls()
        {
            base.AddUiControls();

            Debug.Assert(UiControl != null);

            BuildSidebarControls();
            Debug.Assert(handlersGroup != null);
            Debug.Assert(parametersGroup != null);
            UiControl.AddControl(handlersGroup);

            using (UiControl.BeginGroup("Draw help widgets"))
            {
                UiControl.AddCheckBox("Paths", true, v => ToggleNodes(pathNodes, v));
            }

            UiControl.AddControl(parametersGroup);

            PopulateHierarchyTree();
            BuildHandlersUi();
        }

        private int GetClassColumnX() => Math.Max(80, (hierarchyTree?.ClientSize.Width ?? 300) - IdColumnWidth - ClassColumnWidth);
        private int GetIdColumnX() => Math.Max(120, (hierarchyTree?.ClientSize.Width ?? 300) - IdColumnWidth);

        private void BuildSidebarControls()
        {
            StructureControl = CreateSplitContainer();
            VariablesControl = CreateSplitContainer();

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

            rawSelectionTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Visible = false,
                WordWrap = false,
            };

            showRawSelectionCheckBox = new CheckBox
            {
                Dock = DockStyle.Top,
                Height = 24,
                Text = "Show raw selection",
            };
            showRawSelectionCheckBox.CheckedChanged += (_, _) => RefreshInspector();

            variablesPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
            };

            variablesGrid = new DataGridView
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
            variablesGrid.Columns.Add("Variable", "Variable");
            variablesGrid.Columns.Add("Value", "Value");

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

            handlerList = new ListBox
            {
                Dock = DockStyle.Top,
                Height = 96,
                IntegralHeight = false,
            };
            handlerList.SelectedIndexChanged += (_, _) =>
            {
                if (!updatingHandlersUi)
                {
                    BuildHandlerProperties();
                }
            };

            handlerPropertiesPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
            };

            StructureControl.Panel1.Controls.Add(hierarchyTree);
            StructureControl.Panel1.Controls.Add(hierarchyHeader);
            StructureControl.Panel1.Controls.Add(filterTextBox);
            StructureControl.Panel1.Controls.Add(CreatePanelHeader("Hierarchy"));

            StructureControl.Panel2.Controls.Add(inspectorGrid);
            StructureControl.Panel2.Controls.Add(rawSelectionTextBox);
            StructureControl.Panel2.Controls.Add(showRawSelectionCheckBox);
            StructureControl.Panel2.Controls.Add(CreatePanelHeader("Properties"));

            VariablesControl.Panel1.Controls.Add(variablesGrid);
            VariablesControl.Panel1.Controls.Add(CreatePanelHeader("Variables"));
            VariablesControl.Panel2.Controls.Add(choicesPanel);
            VariablesControl.Panel2.Controls.Add(CreatePanelHeader("Choices"));

            BuildVariablesUi();
            BuildChoicesUi();
            choicesPanel.Enabled = false;
            PopulateVariablesView();

            parametersGroup = new ThemedGroupBox
            {
                Text = "Parameters",
                Height = 300,
                Padding = new Padding(4, 8, 4, 4),
            };
            parametersGroup.Controls.Add(variablesPanel);

            handlersGroup = new ThemedGroupBox
            {
                Text = "Handlers",
                Height = 180,
                Padding = new Padding(4, 8, 4, 4),
            };
            var handlerEditor = new Panel
            {
                Dock = DockStyle.Fill,
            };
            handlerEditor.Controls.Add(handlerPropertiesPanel);
            handlerEditor.Controls.Add(handlerList);
            handlersGroup.Controls.Add(handlerEditor);
        }

        private static SplitContainer CreateSplitContainer()
        {
            var splitter = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                Panel1MinSize = 0,
                Panel2MinSize = 0,
            };

            splitter.SizeChanged += (_, _) =>
            {
                if (splitter.SplitterDistance == 0 && splitter.Width > splitter.SplitterWidth)
                {
                    splitter.SplitterDistance = (splitter.Width - splitter.SplitterWidth) / 2;
                }
            };

            return splitter;
        }

        private static Label CreatePanelHeader(string text) => new()
        {
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(6, 4, 0, 0),
            Text = text,
            ForeColor = Themer.CurrentThemeColors.Contrast,
        };

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

        private void BuildHandlersUi()
        {
            if (handlerList == null)
            {
                return;
            }

            var selected = handlerList.SelectedItem as HandlerListItem;
            updatingHandlersUi = true;
            handlerList.BeginUpdate();
            handlerList.Items.Clear();

            Dictionary<(Type Type, int ElementId), int> occurrences = [];
            foreach (var widget in overlayWidgets)
            {
                if (!IsOverlayWidgetVisible(widget))
                {
                    continue;
                }

                var key = (widget.GetType(), widget.ElementId);
                var occurrence = occurrences.GetValueOrDefault(key);
                occurrences[key] = occurrence + 1;
                var typeName = widget switch
                {
                    SmartPropPickOneHandleWidget => "PickOne",
                    SmartPropLocatorWidget => "Locator",
                    SmartPropRotatorWidget => "Rotator",
                    SmartPropSizerWidget => "Sizer",
                    _ => "Handler",
                };
                var name = widget.Name.Length > 0 ? $": {widget.Name}" : string.Empty;
                var suffix = occurrence > 0 ? $" #{occurrence + 1}" : string.Empty;
                handlerList.Items.Add(new HandlerListItem(widget, occurrence, $"{typeName}{name}{suffix}"));
            }

            var selectedIndex = -1;
            if (selected != null)
            {
                for (var i = 0; i < handlerList.Items.Count; i++)
                {
                    var item = (HandlerListItem)handlerList.Items[i];
                    if (item.Widget.ElementId == selected.Widget.ElementId
                        && item.Widget.GetType() == selected.Widget.GetType()
                        && item.Occurrence == selected.Occurrence)
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            if (selectedIndex < 0 && handlerList.Items.Count > 0)
            {
                selectedIndex = 0;
            }
            handlerList.SelectedIndex = selectedIndex;
            handlerList.EndUpdate();
            updatingHandlersUi = false;
            BuildHandlerProperties();
        }

        private void BuildHandlerProperties()
        {
            if (handlerPropertiesPanel == null)
            {
                return;
            }

            handlerPropertiesPanel.SuspendLayout();
            handlerPropertiesPanel.Controls.Clear();
            if (handlerList?.SelectedItem is not HandlerListItem item)
            {
                handlerPropertiesPanel.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    Text = "Select a visible handler to edit its state.",
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Themer.CurrentThemeColors.ContrastSoft,
                });
                handlerPropertiesPanel.ResumeLayout(false);
                ResizeHandlersGroup(56);
                return;
            }

            activeWidget = item.Widget;
            if (widgetsByElementId.TryGetValue(item.Widget.ElementId, out var evaluatedWidgets))
            {
                var sceneNode = evaluatedWidgets
                    .Where(entry => entry.Widget.GetType() == item.Widget.GetType())
                    .Skip(item.Occurrence)
                    .FirstOrDefault()
                    .Node;
                SelectedNodeRenderer?.SelectNode(sceneNode);
            }
            var table = CreateHandlerPropertiesTable();
            AddHandlerTextRow(table, "Type", item.Widget switch
            {
                SmartPropPickOneHandleWidget => "PickOne",
                SmartPropLocatorWidget => "Locator",
                SmartPropRotatorWidget => "Rotator",
                SmartPropSizerWidget => "Sizer",
                _ => "Handler",
            });

            switch (item.Widget)
            {
                case SmartPropPickOneHandleWidget pickOne:
                    AddPickOneEditor(table, pickOne);
                    break;
                case SmartPropLocatorWidget locator:
                    AddLocatorEditor(table, locator);
                    break;
                case SmartPropRotatorWidget rotator:
                    AddRotatorEditor(table, rotator);
                    break;
                case SmartPropSizerWidget sizer:
                    AddSizerEditor(table, sizer);
                    break;
            }

            handlerPropertiesPanel.Controls.Add(table);
            handlerPropertiesPanel.ResumeLayout(false);
            ResizeHandlersGroup(table.PreferredSize.Height);
        }

        private bool SelectHandlerInList(SmartPropWidget widget)
        {
            if (handlerList == null)
            {
                return false;
            }

            if (handlerList.InvokeRequired)
            {
                if (!handlerList.IsDisposed && handlerList.IsHandleCreated)
                {
                    handlerList.BeginInvoke(() => SelectHandlerInList(widget));
                }

                return true;
            }

            for (var i = 0; i < handlerList.Items.Count; i++)
            {
                if (handlerList.Items[i] is HandlerListItem item && ReferenceEquals(item.Widget, widget))
                {
                    handlerList.SelectedIndex = i;
                    return true;
                }
            }

            return false;
        }

        private SmartPropWidget? FindWidget(SceneNode sceneNode)
        {
            foreach (var widgetList in widgetsByElementId.Values)
            {
                foreach (var (widget, node) in widgetList)
                {
                    if (ReferenceEquals(node, sceneNode))
                    {
                        return widget;
                    }
                }
            }

            return null;
        }

        private void ResizeHandlersGroup(int propertiesHeight)
        {
            if (handlersGroup == null || handlerList == null)
            {
                return;
            }

            handlersGroup.Height = Math.Clamp(
                handlerList.Height + propertiesHeight + handlersGroup.Padding.Vertical + 12,
                160,
                420);
        }

        private static TableLayoutPanel CreateHandlerPropertiesTable()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(6),
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            return table;
        }

        private static void AddHandlerTextRow(TableLayoutPanel table, string name, string value)
        {
            var row = table.RowCount++;
            table.Controls.Add(CreateHandlerLabel(name), 0, row);
            table.Controls.Add(new Label
            {
                Text = value,
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 4),
                ForeColor = Themer.CurrentThemeColors.ContrastSoft,
            }, 1, row);
        }

        private static Label CreateHandlerLabel(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 7, 10, 4),
            ForeColor = Themer.CurrentThemeColors.Contrast,
        };

        private static NumericUpDown AddHandlerNumber(
            TableLayoutPanel table,
            string name,
            float value,
            Action<float> changed,
            decimal minimum = -1000000m,
            decimal maximum = 1000000m,
            int decimals = 2)
        {
            var row = table.RowCount++;
            var number = new NumericUpDown
            {
                DecimalPlaces = decimals,
                Increment = decimals == 0 ? 1m : 0.1m,
                Minimum = minimum,
                Maximum = maximum,
                Value = Math.Clamp((decimal)value, minimum, maximum),
                Dock = DockStyle.Top,
                Margin = new Padding(0, 3, 0, 3),
            };
            number.ValueChanged += (_, _) => changed((float)number.Value);
            table.Controls.Add(CreateHandlerLabel(name), 0, row);
            table.Controls.Add(number, 1, row);
            return number;
        }

        private void AddPickOneEditor(TableLayoutPanel table, SmartPropPickOneHandleWidget pickOne)
        {
            if (!elementsByElementId.TryGetValue(pickOne.ElementId, out var element)
                || !element.TryGetValue("m_Children", out var children)
                || !children.IsArray)
            {
                AddHandlerTextRow(table, "Choice", "No children");
                return;
            }

            var childSpan = children.AsArraySpan();
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 3, 0, 3),
            };
            for (var i = 0; i < childSpan.Length; i++)
            {
                var label = GetString(childSpan[i], "m_sLabel");
                combo.Items.Add(label.Length > 0 ? $"{i}: {label}" : $"{i}: Child {i + 1}");
            }

            element.TryGetValue("m_SpecificChildIndex", out var authoredSelection);
            var initial = childSpan.Length == 0
                ? -1
                : Math.Clamp((int)new SmartPropEvaluationContext(activeVariables).ResolveScalar(authoredSelection), 0, childSpan.Length - 1);
            combo.SelectedIndex = childSpan.Length == 0
                ? -1
                : Math.Clamp(pickOneSelections.GetValueOrDefault(pickOne.ElementId, initial), 0, childSpan.Length - 1);
            combo.SelectedIndexChanged += (_, _) =>
            {
                if (combo.SelectedIndex < 0)
                {
                    return;
                }

                pickOneSelections[pickOne.ElementId] = combo.SelectedIndex;
                if (pickOne.Name.Length > 0)
                {
                    activeVariables[pickOne.Name] = combo.SelectedIndex;
                }
                ReevaluateSmartProp();
            };

            var row = table.RowCount++;
            table.Controls.Add(CreateHandlerLabel("Choice"), 0, row);
            table.Controls.Add(combo, 1, row);
        }

        private void AddLocatorEditor(TableLayoutPanel table, SmartPropLocatorWidget locator)
        {
            var edit = locatorEdits.GetValueOrDefault(locator.ElementId, new(locator.Position, locator.PitchYawRoll, locator.DisplayScale));
            AddHandlerNumber(table, "Position X", edit.Position.X, value => UpdateLocatorEditor(locator, state => state with { Position = state.Position with { X = value } }));
            AddHandlerNumber(table, "Position Y", edit.Position.Y, value => UpdateLocatorEditor(locator, state => state with { Position = state.Position with { Y = value } }));
            AddHandlerNumber(table, "Position Z", edit.Position.Z, value => UpdateLocatorEditor(locator, state => state with { Position = state.Position with { Z = value } }));
            AddHandlerNumber(table, "Rotation X", edit.Angles.X, value => UpdateLocatorEditor(locator, state => state with { Angles = state.Angles with { X = value } }));
            AddHandlerNumber(table, "Rotation Y", edit.Angles.Y, value => UpdateLocatorEditor(locator, state => state with { Angles = state.Angles with { Y = value } }));
            AddHandlerNumber(table, "Rotation Z", edit.Angles.Z, value => UpdateLocatorEditor(locator, state => state with { Angles = state.Angles with { Z = value } }));
            AddHandlerNumber(table, "Scale", edit.Scale, value => UpdateLocatorEditor(locator, state => state with { Scale = value }), 0.01m, 10000m);
        }

        private void UpdateLocatorEditor(SmartPropLocatorWidget locator, Func<LocatorEditState, LocatorEditState> update)
        {
            var state = locatorEdits.GetValueOrDefault(locator.ElementId, new(locator.Position, locator.PitchYawRoll, locator.DisplayScale));
            state = update(state);
            locatorEdits[locator.ElementId] = state;
            if (locator.Name.Length > 0)
            {
                activeVariables[locator.Name] = (float[])[state.Position.X, state.Position.Y, state.Position.Z];
            }
            ReevaluateSmartProp(false);
        }

        private void AddRotatorEditor(TableLayoutPanel table, SmartPropRotatorWidget rotator)
        {
            var minimum = rotator.MinAngle ?? -180f;
            var maximum = rotator.MaxAngle ?? 180f;
            if (minimum > maximum)
            {
                (minimum, maximum) = (maximum, minimum);
            }
            var angle = rotatorEdits.GetValueOrDefault(rotator.ElementId, rotator.Angle);
            AddHandlerNumber(table, "Angle", angle, value =>
            {
                if (rotator.SnappingIncrement > 0f)
                {
                    value = MathF.Round(value / rotator.SnappingIncrement) * rotator.SnappingIncrement;
                }
                value = Math.Clamp(value, minimum, maximum);
                rotatorEdits[rotator.ElementId] = value;
                if (rotator.OutputVariable.Length > 0)
                {
                    activeVariables[rotator.OutputVariable] = value;
                    widgetOutputValues[rotator.OutputVariable] = value;
                }
                ReevaluateSmartProp(false);
            }, (decimal)minimum, (decimal)maximum);
            AddHandlerTextRow(table, "Axis", FormatVector(rotator.Axis));
            AddHandlerTextRow(table, "Radius", rotator.Radius.ToString("0.##"));
        }

        private void AddSizerEditor(TableLayoutPanel table, SmartPropSizerWidget sizer)
        {
            var edit = sizerEdits.GetValueOrDefault(sizer.ElementId, new(sizer.MinBounds, sizer.MaxBounds));
            var editableCount = 0;
            AddBound(sizer.MinXVariable, "Minimum X", edit.MinBounds.X, sizer.Constraints.MinX, sizer.Constraints.MaxX,
                value => UpdateSizerEditor(sizer, state => state with { MinBounds = state.MinBounds with { X = value } }));
            AddBound(sizer.MaxXVariable, "Maximum X", edit.MaxBounds.X, sizer.Constraints.MinX, sizer.Constraints.MaxX,
                value => UpdateSizerEditor(sizer, state => state with { MaxBounds = state.MaxBounds with { X = value } }));
            AddBound(sizer.MinYVariable, "Minimum Y", edit.MinBounds.Y, sizer.Constraints.MinY, sizer.Constraints.MaxY,
                value => UpdateSizerEditor(sizer, state => state with { MinBounds = state.MinBounds with { Y = value } }));
            AddBound(sizer.MaxYVariable, "Maximum Y", edit.MaxBounds.Y, sizer.Constraints.MinY, sizer.Constraints.MaxY,
                value => UpdateSizerEditor(sizer, state => state with { MaxBounds = state.MaxBounds with { Y = value } }));
            AddBound(sizer.MinZVariable, "Minimum Z", edit.MinBounds.Z, sizer.Constraints.MinZ, sizer.Constraints.MaxZ,
                value => UpdateSizerEditor(sizer, state => state with { MinBounds = state.MinBounds with { Z = value } }));
            AddBound(sizer.MaxZVariable, "Maximum Z", edit.MaxBounds.Z, sizer.Constraints.MinZ, sizer.Constraints.MaxZ,
                value => UpdateSizerEditor(sizer, state => state with { MaxBounds = state.MaxBounds with { Z = value } }));

            if (editableCount == 0)
            {
                AddHandlerTextRow(table, "Bounds", "No output axes");
            }

            void AddBound(string variable, string label, float value, float? constraintMin, float? constraintMax, Action<float> changed)
            {
                if (variable.Length == 0)
                {
                    return;
                }

                editableCount++;
                var minimum = (decimal)(constraintMin ?? -1000000f);
                var maximum = (decimal)(constraintMax ?? 1000000f);
                if (minimum > maximum)
                {
                    (minimum, maximum) = (maximum, minimum);
                }
                AddHandlerNumber(table, label, value, changed, minimum, maximum);
            }
        }

        private void UpdateSizerEditor(SmartPropSizerWidget sizer, Func<SizerEditState, SizerEditState> update)
        {
            var state = sizerEdits.GetValueOrDefault(sizer.ElementId, new(sizer.MinBounds, sizer.MaxBounds));
            state = update(state);
            sizerEdits[sizer.ElementId] = state;
            SetSizerOutput(sizer.MinXVariable, state.MinBounds.X);
            SetSizerOutput(sizer.MinYVariable, state.MinBounds.Y);
            SetSizerOutput(sizer.MinZVariable, state.MinBounds.Z);
            SetSizerOutput(sizer.MaxXVariable, state.MaxBounds.X);
            SetSizerOutput(sizer.MaxYVariable, state.MaxBounds.Y);
            SetSizerOutput(sizer.MaxZVariable, state.MaxBounds.Z);
            ReevaluateSmartProp(false);
        }

        private void SetSizerOutput(string variable, float value)
        {
            if (variable.Length == 0)
            {
                return;
            }

            widgetOutputValues[variable] = value;
            SetActiveVariableValue(variable, value);
        }

        private void ReevaluateSmartProp(bool rebuildVariablesUi = true)
        {
            if (reevaluatingSmartProp)
            {
                reevaluationPending = true;
                pendingVariablesUiRebuild |= rebuildVariablesUi;
                return;
            }

            reevaluatingSmartProp = true;
            try
            {
                do
                {
                    reevaluationPending = false;
                    pendingVariablesUiRebuild = false;

                    using var lockedGl = MakeCurrent();

                    SelectedNodeRenderer?.SelectNode(null);

                    ClearSmartPropScene();

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
                    inspectorSelection = null;
                    rawSelectionTextBox?.Clear();

                    EvaluateScene();
                    RefreshLightingBindings();

                    PopulateHierarchyTree(filterTextBox?.Text.Trim() ?? string.Empty);
                    BuildHandlersUi();
                    if (rebuildVariablesUi)
                    {
                        updatingVariablesUi = true;
                        try
                        {
                            BuildVariablesUi();
                        }
                        finally
                        {
                            updatingVariablesUi = false;
                        }
                    }
                    PopulateChoiceOverrides();
                    PopulateVariablesView();

                    NotifyVisible();
                    rebuildVariablesUi = pendingVariablesUiRebuild;
                }
                while (reevaluationPending);
            }
            finally
            {
                reevaluatingSmartProp = false;
            }
        }

        private void RefreshLightingBindings()
        {
            // Environment-map and light-probe assignments are normally calculated once during scene initialization.
            // Handler edits replace every SmartProp node, so the replacements need those assignments before rendering.
            Scene.UpdateOctrees();
            Scene.CalculateLightProbeBindings();
            Scene.CalculateEnvironmentMaps();

            // Let the regular scene update rebuild node IDs and instance buffers with the refreshed assignments.
            Scene.StaticOctree.Dirty = true;
        }

        private void ClearSmartPropScene()
        {
            foreach (var node in Scene.AllNodes.ToArray())
            {
                node.Delete();
                Scene.Remove(node, dynamic: false);
            }
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
            elementsByElementId.Clear();

            PopulateElementHierarchy(
                hierarchyTree.Nodes,
                smartProp.Data.Root,
                modelsByElementId,
                pathsByElementId,
                widgetsByElementId,
                filter);

            hierarchyTree.ExpandAll();
            hierarchyTree.EndUpdate();
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

        private ModelSceneNode? CreateLocatorSceneNode(SmartPropLocatorWidget locator)
        {
            var resource = GuiContext.LoadFileCompiled(LocatorModelName);
            if (resource?.DataBlock is not Model model)
            {
                resource?.Dispose();
                return null;
            }

            loadedResources.Add(resource);

            var modelSceneNode = new ModelSceneNode(Scene, model)
            {
                Name = locator.Name,
                Transform = Matrix4x4.CreateScale(locator.DisplayScale) * locator.WorldMatrix,
            };

            Scene.Add(modelSceneNode, false);
            return modelSceneNode;
        }

        private SceneNode? CreateWidgetSceneNode(SmartPropWidget widget)
        {
            var sceneNode = widget switch
            {
                SmartPropLocatorWidget locator => CreateLocatorSceneNode(locator) is { } node ? Track(node, locatorNodes) : null,
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

        private SmartPropWidget ApplyWidgetEdit(SmartPropWidget widget)
        {
            if (widget is SmartPropRotatorWidget rotator && rotatorEdits.TryGetValue(rotator.ElementId, out var angle))
            {
                return rotator with { Angle = angle };
            }

            if (widget is SmartPropSizerWidget sizer && sizerEdits.TryGetValue(sizer.ElementId, out var sizerEdit))
            {
                return sizer with { MinBounds = sizerEdit.MinBounds, MaxBounds = sizerEdit.MaxBounds };
            }

            if (widget is SmartPropLocatorWidget locator && locatorEdits.TryGetValue(locator.ElementId, out var locatorEdit))
            {
                var matrix = EntityTransformHelper.EulerAnglesToRotationMatrix(locatorEdit.Angles);
                matrix.M11 *= locatorEdit.Scale;
                matrix.M12 *= locatorEdit.Scale;
                matrix.M13 *= locatorEdit.Scale;
                matrix.M21 *= locatorEdit.Scale;
                matrix.M22 *= locatorEdit.Scale;
                matrix.M23 *= locatorEdit.Scale;
                matrix.M31 *= locatorEdit.Scale;
                matrix.M32 *= locatorEdit.Scale;
                matrix.M33 *= locatorEdit.Scale;
                matrix.Translation = locatorEdit.Position;

                return locator with
                {
                    WorldMatrix = matrix,
                    Position = locatorEdit.Position,
                    PitchYawRoll = locatorEdit.Angles,
                    DisplayScale = locatorEdit.Scale,
                };
            }

            return widget;
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

            var editorVariables = variableDefinitions.Where(variable => variable.ExposeAsParameter).ToList();
            if (editorVariables.Count == 0)
            {
                var emptyLabel = new Label
                {
                    Dock = DockStyle.Fill,
                    Text = "No editor-visible parameters in this smart prop.",
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

            for (var i = 0; i < editorVariables.Count; i++)
            {
                var variable = editorVariables[i];
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
                        if (IsWidgetOutputVariable(variable.Name) && min == max)
                        {
                            min = -1000000;
                            max = 1000000;
                        }
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
                                SetWidgetOutputValue(variable.Name, (float)num.Value);
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
                        if (IsWidgetOutputVariable(variable.Name) && min == max)
                        {
                            min = -1000000;
                            max = 1000000;
                        }
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
                                SetWidgetOutputValue(variable.Name, (float)num.Value);
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

        private void PopulateVariablesView()
        {
            if (variablesGrid == null)
            {
                return;
            }

            variablesGrid.Rows.Clear();
            foreach (var variable in variableDefinitions)
            {
                var name = variable.DisplayName ?? variable.Name;
                var value = activeVariables.GetValueOrDefault(variable.Name, variable.DefaultValue);
                variablesGrid.Rows.Add(name, FormatObjectValue(value));
            }
        }

        private bool IsWidgetOutputVariable(string name)
        {
            foreach (var widgetList in widgetsByElementId.Values)
            {
                foreach (var (widget, _) in widgetList)
                {
                    if (widget is SmartPropRotatorWidget { OutputVariable.Length: > 0 } rotator
                        && string.Equals(rotator.OutputVariable, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (widget is SmartPropPickOneHandleWidget { Name.Length: > 0 } pickOne
                        && string.Equals(pickOne.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (widget is SmartPropSizerWidget sizer
                        && (string.Equals(sizer.MinXVariable, name, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(sizer.MaxXVariable, name, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(sizer.MinYVariable, name, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(sizer.MaxYVariable, name, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(sizer.MinZVariable, name, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(sizer.MaxZVariable, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void SetWidgetOutputValue(string name, float value)
        {
            if (IsWidgetOutputVariable(name))
            {
                widgetOutputValues[name] = value;
            }
        }

        private void SetActiveVariableValue(string name, float value)
        {
            var definition = variableDefinitions.FirstOrDefault(variable => string.Equals(variable.Name, name, StringComparison.OrdinalIgnoreCase));
            activeVariables[name] = definition?.Type == "Int" ? (int)MathF.Round(value) : value;
        }

        private SizerDragState? FindSizerHandle(SmartPropSizerWidget sizer, Point location)
        {
            var center = (sizer.MinBounds + sizer.MaxBounds) * 0.5f;
            var bestDistance = float.MaxValue;
            SizerDragState? best = null;

            TryHandle(sizer.MinXVariable, new Vector3(sizer.MinBounds.X, center.Y, center.Z), Vector3.UnitX, sizer.MinBounds.X);
            TryHandle(sizer.MaxXVariable, new Vector3(sizer.MaxBounds.X, center.Y, center.Z), Vector3.UnitX, sizer.MaxBounds.X);
            TryHandle(sizer.MinYVariable, new Vector3(center.X, sizer.MinBounds.Y, center.Z), Vector3.UnitY, sizer.MinBounds.Y);
            TryHandle(sizer.MaxYVariable, new Vector3(center.X, sizer.MaxBounds.Y, center.Z), Vector3.UnitY, sizer.MaxBounds.Y);
            TryHandle(sizer.MinZVariable, new Vector3(center.X, center.Y, sizer.MinBounds.Z), Vector3.UnitZ, sizer.MinBounds.Z);
            TryHandle(sizer.MaxZVariable, new Vector3(center.X, center.Y, sizer.MaxBounds.Z), Vector3.UnitZ, sizer.MaxBounds.Z);
            return bestDistance <= 18f ? best : null;

            void TryHandle(string variable, Vector3 localPosition, Vector3 localAxis, float initialValue)
            {
                if (variable.Length == 0)
                {
                    return;
                }

                var worldPosition = SmartPropTransform.TransformPoint(sizer.WorldMatrix, localPosition);
                var worldAxis = Vector3.TransformNormal(localAxis, sizer.WorldMatrix);
                if (worldAxis.LengthSquared() < 1e-8f)
                {
                    return;
                }

                worldAxis = Vector3.Normalize(worldAxis);
                if (!TryProject(worldPosition, out var screenPosition)
                    || !TryProject(worldPosition + worldAxis, out var axisScreenPosition))
                {
                    return;
                }

                var screenAxis = axisScreenPosition - screenPosition;
                var pixelsPerUnit = screenAxis.Length();
                if (pixelsPerUnit < 0.01f)
                {
                    return;
                }

                var distance = Vector2.Distance(screenPosition, new Vector2(location.X, location.Y));
                if (distance >= bestDistance)
                {
                    return;
                }

                bestDistance = distance;
                best = new SizerDragState(variable, initialValue, screenAxis / pixelsPerUnit, pixelsPerUnit, location);
            }
        }

        private bool TryProject(Vector3 worldPosition, out Vector2 screenPosition)
        {
            var clip = Vector4.Transform(new Vector4(worldPosition, 1f), Input.Camera.ViewProjectionMatrix);
            if (clip.W <= 0.0001f || GLControl == null)
            {
                screenPosition = default;
                return false;
            }

            var ndc = new Vector2(clip.X, clip.Y) / clip.W;
            screenPosition = new Vector2(
                (ndc.X + 1f) * GLControl.ClientSize.Width * 0.5f,
                (1f - ndc.Y) * GLControl.ClientSize.Height * 0.5f);
            return true;
        }

        private SmartPropWidget? FindOverlayWidget(Point location)
        {
            var mouse = new Vector2(location.X, location.Y);
            SmartPropWidget? best = null;
            var bestDistance = float.MaxValue;

            for (var i = overlayWidgets.Count - 1; i >= 0; i--)
            {
                var widget = overlayWidgets[i];
                if (!IsOverlayWidgetVisible(widget) || !TryProject(widget.Position, out var center))
                {
                    continue;
                }

                var distance = widget switch
                {
                    SmartPropPickOneHandleWidget => Vector2.Distance(mouse, center),
                    SmartPropLocatorWidget => Vector2.Distance(mouse, center),
                    SmartPropRotatorWidget rotator => DistanceToRotator(mouse, center, rotator),
                    SmartPropSizerWidget sizer => FindSizerHandle(sizer, location) != null ? 0f : float.MaxValue,
                    _ => float.MaxValue,
                };

                var tolerance = widget switch
                {
                    SmartPropPickOneHandleWidget => 22f,
                    SmartPropLocatorWidget => 32f,
                    SmartPropRotatorWidget => 14f,
                    SmartPropSizerWidget => 1f,
                    _ => 0f,
                };

                if (distance <= tolerance && distance < bestDistance)
                {
                    best = widget;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private bool IsOverlayWidgetVisible(SmartPropWidget widget)
            => widget switch
            {
                SmartPropLocatorWidget => locatorNodes.Count > 0 && locatorNodes[0].LayerEnabled,
                SmartPropRotatorWidget => rotatorNodes.Count > 0 && rotatorNodes[0].LayerEnabled,
                SmartPropSizerWidget => sizerNodes.Count > 0 && sizerNodes[0].LayerEnabled,
                SmartPropPickOneHandleWidget => pickOneNodes.Count > 0 && pickOneNodes[0].LayerEnabled,
                _ => false,
            };

        private float DistanceToRotator(Vector2 mouse, Vector2 center, SmartPropRotatorWidget rotator)
        {
            var axis = Vector3.Normalize(rotator.Axis);
            var radial = Vector3.Cross(axis, Input.Camera.Forward);
            if (radial.LengthSquared() < 1e-8f)
            {
                radial = Vector3.Cross(axis, Input.Camera.Up);
            }
            if (radial.LengthSquared() < 1e-8f
                || !TryProject(rotator.Position + (Vector3.Normalize(radial) * rotator.Radius), out var rim))
            {
                return float.MaxValue;
            }

            var radius = Vector2.Distance(center, rim);
            return MathF.Abs(Vector2.Distance(mouse, center) - radius);
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
            Dictionary<int, List<(SmartPropPathInfo Path, SceneNode? Node)>> pathsByElementId,
            Dictionary<int, List<(SmartPropWidget Widget, SceneNode? Node)>> widgetsByElementId,
            string filter)
        {
            var className = SmartPropModifierEvaluator.GetClassName(element);
            var elementId = GetInt32(element, "m_nElementID");
            if (elementId > 0)
            {
                elementsByElementId[elementId] = element;
            }

            // If this is the root container (CSmartPropRoot or unnamed root with m_Children), process its children directly
            if ((className is "Root" or "CSmartPropRoot" or "" || elementId == 0) && element.TryGetValue("m_Children", out var rootChildren) && rootChildren.IsArray)
            {
                var anyRootMatch = false;

                foreach (var child in rootChildren.AsArraySpan())
                {
                    if (child.ValueType == KVValueType.Collection)
                    {
                        anyRootMatch |= PopulateElementHierarchy(parentNodes, child, modelsByElementId, pathsByElementId, widgetsByElementId, filter);
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

            modelsByElementId.TryGetValue(elementId, out var modelList);
            pathsByElementId.TryGetValue(elementId, out var pathList);

            if (modelList != null && modelList.Count > 0)
            {
                primarySceneNode = modelList[0].Node;
            }
            else if (pathList != null && pathList.Count > 0)
            {
                primarySceneNode = pathList[0].Node;
            }

            var nodeData = new HierarchyNodeData(
                primarySceneNode,
                element,
                element,
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

            // Recursively populate child elements
            if (className != "SmartProp" && element.TryGetValue("m_Children", out var children) && children.IsArray)
            {
                foreach (var child in children.AsArraySpan())
                {
                    if (child.ValueType == KVValueType.Collection)
                    {
                        hasMatchingDescendant |= PopulateElementHierarchy(elementNode.Nodes, child, modelsByElementId, pathsByElementId, widgetsByElementId, filter);
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
                    if (node != null)
                    {
                        treeNodesBySceneNode[node] = elementNode;
                    }
                }
            }

            if (pathList != null)
            {
                foreach (var (_, node) in pathList)
                {
                    if (node != null)
                    {
                        treeNodesBySceneNode[node] = elementNode;
                    }
                }
            }

            if (widgetsByElementId.TryGetValue(elementId, out var widgetList))
            {
                foreach (var (_, node) in widgetList)
                {
                    if (node != null)
                    {
                        treeNodesBySceneNode[node] = elementNode;
                    }
                }
            }

            parentNodes.Add(elementNode);
            return true;
        }

        private void ToggleNodes(List<SceneNode> nodes, bool visible)
        {
            foreach (var node in nodes)
            {
                node.LayerEnabled = visible;
            }

            BuildHandlersUi();
        }

        private void OnHierarchyNodeSelected(object? sender, TreeViewEventArgs e)
        {
            if (selectingFromViewport || e.Node?.Tag is not HierarchyNodeData nodeData)
            {
                return;
            }

            UpdateActivePickOne(nodeData);
            activeWidget = nodeData.Payload as SmartPropWidget;
            SelectedNodeRenderer?.SelectNode(nodeData.Node);
            FillInspector(nodeData);
        }

        protected override void OnMouseWheel(int delta, Point location)
        {
            var hoveredWidget = FindOverlayWidget(location);
            if (hoveredWidget is SmartPropPickOneHandleWidget pickOne && delta != 0)
            {
                CyclePickOne(pickOne, delta);
                return;
            }

            if (hoveredWidget is SmartPropRotatorWidget { OutputVariable.Length: > 0 } rotator && delta != 0)
            {
                var increment = rotator.SnappingIncrement > 0f ? rotator.SnappingIncrement : 5f;
                var angle = rotator.Angle + (delta > 0 ? increment : -increment);
                if (rotator.MinAngle.HasValue)
                {
                    angle = MathF.Max(angle, rotator.MinAngle.Value);
                }
                if (rotator.MaxAngle.HasValue)
                {
                    angle = MathF.Min(angle, rotator.MaxAngle.Value);
                }

                activeVariables[rotator.OutputVariable] = angle;
                ReevaluateSmartProp();
                return;
            }

            base.OnMouseWheel(delta, location);
        }

        protected override void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                activeWidget = FindOverlayWidget(e.Location);
                if (activeWidget != null)
                {
                    SelectHandlerInList(activeWidget);
                    if (BeginWidgetDrag(e.Location))
                    {
                        base.OnMouseDown(sender, e);
                        draggingWidget = true;
                        widgetDragPoint = e.Location;
                        return;
                    }
                }
            }

            base.OnMouseDown(sender, e);
        }

        private void CyclePickOne(SmartPropPickOneHandleWidget pickOne, int delta)
        {
            if (!elementsByElementId.TryGetValue(pickOne.ElementId, out var element)
                || !element.TryGetValue("m_Children", out var children)
                || !children.IsArray
                || children.AsArraySpan().Length == 0)
            {
                return;
            }

            var childCount = children.AsArraySpan().Length;
            element.TryGetValue("m_SpecificChildIndex", out var authoredSelection);
            var initial = Math.Clamp((int)new SmartPropEvaluationContext(activeVariables).ResolveScalar(authoredSelection), 0, childCount - 1);
            var current = pickOneSelections.GetValueOrDefault(pickOne.ElementId, initial);
            var direction = delta > 0 ? -1 : 1;
            var selection = (current + direction + childCount) % childCount;
            pickOneSelections[pickOne.ElementId] = selection;
            if (pickOne.Name.Length > 0)
            {
                activeVariables[pickOne.Name] = selection;
            }
            ReevaluateSmartProp();
        }

        protected override void OnMouseMove(int x, int y)
        {
            if (!draggingWidget)
            {
                base.OnMouseMove(x, y);
                return;
            }

            var point = new Point(x, y);
            var delta = new Point(point.X - widgetDragPoint.X, point.Y - widgetDragPoint.Y);
            if (delta == Point.Empty)
            {
                return;
            }

            MouseDragged = true;
            UpdateWidgetDrag(point, delta);
            widgetDragPoint = point;
        }

        protected override void OnMouseUp(object? sender, MouseEventArgs e)
        {
            var wasDraggingWidget = draggingWidget;
            draggingWidget = false;
            locatorDragInitialState = null;
            sizerDragState = null;

            base.OnMouseUp(sender, e);

            if (wasDraggingWidget)
            {
                updatingVariablesUi = true;
                BuildVariablesUi();
                updatingVariablesUi = false;
            }
        }

        private bool BeginWidgetDrag(Point location)
        {
            switch (activeWidget)
            {
                case SmartPropRotatorWidget { OutputVariable.Length: > 0 } rotator:
                    widgetDragInitialAngle = rotator.Angle;
                    return true;

                case SmartPropLocatorWidget locator:
                    locatorDragInitialState = locatorEdits.GetValueOrDefault(
                        locator.ElementId,
                        new LocatorEditState(locator.Position, locator.PitchYawRoll, locator.DisplayScale));
                    return true;

                case SmartPropSizerWidget sizer:
                    sizerDragState = FindSizerHandle(sizer, location);
                    return sizerDragState != null;

                default:
                    return false;
            }
        }

        private void UpdateWidgetDrag(Point point, Point delta)
        {
            switch (activeWidget)
            {
                case SmartPropRotatorWidget { OutputVariable.Length: > 0 } rotator:
                {
                    var angle = widgetDragInitialAngle + (point.X - InitialMousePosition.X);
                    if (rotator.SnappingIncrement > 0f)
                    {
                        angle = MathF.Round(angle / rotator.SnappingIncrement) * rotator.SnappingIncrement;
                    }
                    if (rotator.MinAngle.HasValue)
                    {
                        angle = MathF.Max(angle, rotator.MinAngle.Value);
                    }
                    if (rotator.MaxAngle.HasValue)
                    {
                        angle = MathF.Min(angle, rotator.MaxAngle.Value);
                    }

                    activeVariables[rotator.OutputVariable] = angle;
                    ReevaluateSmartProp(false);
                    break;
                }

                case SmartPropLocatorWidget locator when locatorDragInitialState != null:
                    UpdateLocatorDrag(locator, delta);
                    break;

                case SmartPropSizerWidget when sizerDragState != null:
                {
                    var offset = new Vector2(point.X - sizerDragState.Start.X, point.Y - sizerDragState.Start.Y);
                    var value = sizerDragState.InitialValue + (Vector2.Dot(offset, sizerDragState.ScreenDirection) / sizerDragState.PixelsPerUnit);
                    widgetOutputValues[sizerDragState.Variable] = value;
                    SetActiveVariableValue(sizerDragState.Variable, value);
                    ReevaluateSmartProp(false);
                    break;
                }
            }
        }

        private void UpdateLocatorDrag(SmartPropLocatorWidget locator, Point delta)
        {
            Debug.Assert(locatorDragInitialState != null);
            var edit = locatorEdits.GetValueOrDefault(locator.ElementId, locatorDragInitialState);

            if (Control.ModifierKeys.HasFlag(Keys.Shift))
            {
                edit = edit with { Angles = edit.Angles + new Vector3(-delta.Y, delta.X, 0f) };
            }
            else if (Control.ModifierKeys.HasFlag(Keys.Control))
            {
                edit = edit with { Scale = MathF.Max(0.01f, edit.Scale * MathF.Exp(delta.X * 0.01f)) };
            }
            else
            {
                var distance = MathF.Max(Vector3.Distance(Input.Camera.Location, edit.Position), 1f);
                var unitsPerPixel = distance * 0.002f;
                edit = edit with
                {
                    Position = edit.Position + (Input.Camera.Right * delta.X * unitsPerPixel) - (Input.Camera.Up * delta.Y * unitsPerPixel),
                };
            }

            locatorEdits[locator.ElementId] = edit;
            if (locator.Name.Length > 0)
            {
                activeVariables[locator.Name] = (float[])[edit.Position.X, edit.Position.Y, edit.Position.Z];
            }
            ReevaluateSmartProp(false);
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

            if (UiControl?.IsDisposed != false)
            {
                return;
            }

            var widget = sceneNode != null ? FindWidget(sceneNode) : null;
            UiControl.BeginInvoke(() => UpdatePickedUi(sceneNode, widget));
        }

        private void UpdatePickedUi(SceneNode? sceneNode, SmartPropWidget? widget)
        {
            if (widget != null)
            {
                SelectHandlerInList(widget);
            }

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
                        UpdateActivePickOne(nodeData);
                        activeWidget = nodeData.Payload as SmartPropWidget;
                        FillInspector(nodeData);
                    }
                }
                finally
                {
                    selectingFromViewport = false;
                }
            }
        }

        private void UpdateActivePickOne(HierarchyNodeData nodeData)
        {
            if (nodeData.Element == null || nodeData.ClassName != "PickOne")
            {
                activePickOneElementId = null;
                activePickOneChildCount = 0;
                return;
            }

            if (!nodeData.Element.TryGetValue("m_Children", out var children) || !children.IsArray)
            {
                activePickOneElementId = null;
                activePickOneChildCount = 0;
                return;
            }

            activePickOneElementId = nodeData.ElementId;
            activePickOneChildCount = children.AsArraySpan().Length;
        }

        private int GetInitialPickOneSelection()
        {
            if (!activePickOneElementId.HasValue || hierarchyTree?.SelectedNode?.Tag is not HierarchyNodeData { Element: { } element })
            {
                return 0;
            }

            element.TryGetValue("m_SpecificChildIndex", out var selection);
            return Math.Clamp((int)new SmartPropEvaluationContext(activeVariables).ResolveScalar(selection), 0, activePickOneChildCount - 1);
        }

        private void FillInspector(HierarchyNodeData selection)
        {
            if (inspectorGrid == null)
            {
                return;
            }

            inspectorSelection = selection;
            RefreshInspector();
        }

        private void RefreshInspector()
        {
            if (inspectorGrid == null || inspectorSelection == null)
            {
                return;
            }

            var selection = inspectorSelection;
            var showRaw = showRawSelectionCheckBox?.Checked == true;
            inspectorGrid.Visible = !showRaw;
            if (rawSelectionTextBox != null)
            {
                rawSelectionTextBox.Visible = showRaw;
                rawSelectionTextBox.Text = showRaw ? GetRawSelectionText(selection) : string.Empty;
            }

            if (showRaw)
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
                    if (rotator.OutputVariable.Length > 0)
                    {
                        inspectorGrid.Rows.Add("Output Variable", rotator.OutputVariable);
                    }
                    inspectorGrid.Rows.Add("Color", FormatVector(rotator.Color));
                    break;

                case SmartPropSizerWidget sizer:
                    inspectorGrid.Rows.Add("Position", FormatVector(sizer.Position));
                    inspectorGrid.Rows.Add("Rotation", FormatVector(sizer.PitchYawRoll));
                    inspectorGrid.Rows.Add("Min Bounds", FormatVector(sizer.MinBounds));
                    inspectorGrid.Rows.Add("Max Bounds", FormatVector(sizer.MaxBounds));
                    inspectorGrid.Rows.Add("Active Axes", $"X: {sizer.ActiveAxes.X}, Y: {sizer.ActiveAxes.Y}, Z: {sizer.ActiveAxes.Z}");
                    if (sizer.MinXVariable.Length > 0 || sizer.MaxXVariable.Length > 0 || sizer.MinYVariable.Length > 0 || sizer.MaxYVariable.Length > 0 || sizer.MinZVariable.Length > 0 || sizer.MaxZVariable.Length > 0)
                    {
                        inspectorGrid.Rows.Add("Output Variables", $"Min: {sizer.MinXVariable}, {sizer.MinYVariable}, {sizer.MinZVariable}; Max: {sizer.MaxXVariable}, {sizer.MaxYVariable}, {sizer.MaxZVariable}");
                    }
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

        private static string GetRawSelectionText(HierarchyNodeData selection)
        {
            if (selection.Element != null)
            {
                var builder = new StringBuilder();
                AppendFormattedValue(builder, selection.Element, 0);
                return builder.ToString();
            }

            return $"Selection: {selection.Title}";
        }

        private static void AppendFormattedValue(StringBuilder builder, KVObject value, int indent)
        {
            if (value.ValueType == KVValueType.Collection)
            {
                builder.AppendLine("{");
                foreach (var key in value.Keys)
                {
                    if (!value.TryGetValue(key, out var child))
                    {
                        continue;
                    }

                    builder.Append(' ', (indent + 1) * 4);
                    builder.Append('"').Append(key.Replace("\"", "\\\"", StringComparison.Ordinal)).Append("\" = ");
                    AppendFormattedValue(builder, child, indent + 1);
                    builder.AppendLine();
                }

                builder.Append(' ', indent * 4).Append('}');
                return;
            }

            if (value.ValueType == KVValueType.Array)
            {
                var items = value.AsArraySpan();
                if (items.Length == 0)
                {
                    builder.Append("[ ]");
                    return;
                }

                builder.AppendLine("[");
                for (var i = 0; i < items.Length; i++)
                {
                    builder.Append(' ', (indent + 1) * 4);
                    AppendFormattedValue(builder, items[i], indent + 1);
                    if (i < items.Length - 1)
                    {
                        builder.Append(',');
                    }

                    builder.AppendLine();
                }

                builder.Append(' ', indent * 4).Append(']');
                return;
            }

            builder.Append(FormatRawPrimitive(value));
        }

        private static string FormatRawPrimitive(KVObject value) => value.ValueType switch
        {
            KVValueType.String => $"\"{((string)value).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}\"",
            KVValueType.Boolean => (bool)value ? "true" : "false",
            _ => value.ToString() ?? string.Empty,
        };

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

            filterTextBox?.Dispose();
            hierarchyHeader?.Dispose();
            hierarchyTree?.Dispose();
            inspectorGrid?.Dispose();
            rawSelectionTextBox?.Dispose();
            showRawSelectionCheckBox?.Dispose();
            variablesPanel?.Dispose();
            variablesGrid?.Dispose();
            choicesPanel?.Dispose();
            choiceOverridesGrid?.Dispose();
            parametersGroup?.Dispose();
            handlersGroup?.Dispose();
            handlerList?.Dispose();
            handlerPropertiesPanel?.Dispose();
            StructureControl?.Dispose();
            VariablesControl?.Dispose();

            foreach (var resource in loadedResources)
            {
                resource.Dispose();
            }

            loadedResources.Clear();
            nestedSmartProps.Clear();
        }
    }
}
