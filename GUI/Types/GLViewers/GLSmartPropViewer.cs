using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace GUI.Types.GLViewers
{
    class GLSmartPropViewer : GLSingleNodeViewer
    {
        private readonly SmartProp smartProp;
        private readonly List<ModelSceneNode> spawnedNodes = [];
        private readonly Dictionary<int, SmartPropValue> parameterOverrides = [];
        private readonly Dictionary<(string Path, string Name), SmartPropHandleEdit> handleEdits = [];
        private readonly List<(ModelSceneNode Node, float FadeEndSize)> detailNodes = [];
        private SmartPropOutput? lastOutput;
        private readonly List<Control> handleControls = [];
        private readonly List<Control> handleInputControls = [];
        private string? selectedHandleLabel;
        private string? handleSignature;
        private readonly HashSet<string> loggedWarnings = [];
        private readonly List<Control> createdControls = [];
        private readonly List<(SmartPropVariableDefinition Variable, Control Target)> parameterControls = [];
        private SmartPropDefinition? definition;
        private SmartPropEvaluator? evaluator;
        private Label? statusLabel;
        private Timer? rebuildTimer;
        private int seed;
        private int modelCount;
        private bool suppressUiEvents;
        private bool disposed;

        public GLSmartPropViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, SmartProp smartProp) : base(vrfGuiContext, rendererContext)
        {
            this.smartProp = smartProp;
        }

        public override void Dispose()
        {
            disposed = true;
            rebuildTimer?.Stop();
            rebuildTimer?.Dispose();

            base.Dispose();

            statusLabel?.Dispose();

            foreach (var control in createdControls)
            {
                control.Dispose();
            }

            createdControls.Clear();
            parameterControls.Clear();
        }

        protected override void LoadScene()
        {
            base.LoadScene();

            definition = SmartPropDefinition.Load(smartProp);
            evaluator = new SmartPropEvaluator(GuiContext);
            EvaluateAndSpawn();
        }

        private SmartPropEvaluationInput CreateInput() => new()
        {
            MasterSeed = seed,
            ParameterOverrides = parameterOverrides,
            ElementStates = CreateElementStates(),
        };

        private List<SmartPropElementState> CreateElementStates()
        {
            var states = new Dictionary<string, SmartPropElementState>();

            foreach (var ((path, name), edit) in handleEdits)
            {
                if (!states.TryGetValue(path, out var state))
                {
                    state = new SmartPropElementState { ElementPath = path.Length == 0 ? [] : [.. path.Split(',').Select(int.Parse)] };
                    states[path] = state;
                }

                state.HandleEdits[name] = edit;
            }

            return [.. states.Values];
        }

        private static string PathKey(int[] path) => string.Join(',', path);

        private void EvaluateAndSpawn()
        {
            Debug.Assert(definition != null && evaluator != null);

            var output = evaluator.Evaluate(definition, CreateInput());
            lastOutput = output;
            modelCount = 0;
            detailNodes.Clear();

            foreach (var instance in output.Models)
            {
                if (instance.ModelName.Length == 0)
                {
                    continue;
                }

                if (GuiContext.LoadFileCompiled(instance.ModelName)?.DataBlock is not Model model)
                {
                    if (loggedWarnings.Add(instance.ModelName))
                    {
                        Log.Warn(nameof(GLSmartPropViewer), $"Failed to load model \"{instance.ModelName}\"");
                    }

                    continue;
                }

                var vertexDeformation = output.GetVertexDeformation(instance);

                var node = vertexDeformation == null
                    ? new ModelSceneNode(Scene, model) { Transform = instance.GetMatrix() }
                    : new ModelSceneNode(Scene, model, replaceVertices: mesh => mesh.VBIB.TransformVertices(vertexDeformation));

                node.Tint = new Vector3(instance.Tint.R, instance.Tint.G, instance.Tint.B) / 255f;

                if (instance.LodLevel >= 0)
                {
                    node.SetOverrideLod(instance.LodLevel);
                }

                if (!instance.CastShadows)
                {
                    node.Flags |= ObjectTypeFlags.NoShadows;
                }

                if (instance.DetailObject)
                {
                    detailNodes.Add((node, instance.DetailFadeEndSize));
                }

                if (instance.MaterialGroup != null)
                {
                    node.SetMaterialGroup(instance.MaterialGroup);
                }

                if (instance.MaterialOverrideSetIndex >= 0)
                {
                    var overrides = output.MaterialOverrideSets[instance.MaterialOverrideSetIndex]
                        .Where(static pair => pair.Value.Length > 0)
                        .ToDictionary(StringComparer.OrdinalIgnoreCase);

                    foreach (var mesh in node.RenderableMeshes)
                    {
                        mesh.ReplaceMaterials(overrides);
                    }
                }

                Scene.Add(node, true);
                spawnedNodes.Add(node);
                modelCount++;
            }
        }

        protected override void OnUpdate(float frameTime)
        {
            base.OnUpdate(frameTime);

            if (detailNodes.Count == 0)
            {
                return;
            }

            var camera = Renderer.Camera;
            var tanHalfHorizontalFov = MathF.Tan(camera.GetFOV() * 0.5f) * camera.AspectRatio;

            foreach (var (node, fadeEndSize) in detailNodes)
            {
                var bounds = node.BoundingBox;
                var radius = (bounds.Max - bounds.Min).Length() * 0.5f;
                var distance = Vector3.Distance(Vector3.Clamp(camera.Location, bounds.Min, bounds.Max), camera.Location);
                var screenSize = distance < radius ? 1f : Math.Clamp(radius / (distance * tanHalfHorizontalFov), 0f, 1f);

                node.Visible = !(fadeEndSize > 1.01f * screenSize);
            }
        }

        private void RequestRebuild()
        {
            if (suppressUiEvents)
            {
                return;
            }

            if (rebuildTimer == null)
            {
                Rebuild();
                return;
            }

            rebuildTimer.Stop();
            rebuildTimer.Start();
        }

        private void Rebuild()
        {
            if (suppressUiEvents || disposed)
            {
                return;
            }

            try
            {
                RebuildCore();
            }
            catch (Exception e)
            {
                Log.Error(nameof(GLSmartPropViewer), $"Smart prop rebuild failed: {e}");

                if (statusLabel != null)
                {
                    statusLabel.Text = "Evaluation failed (see console)";
                }
            }
        }

        private void RebuildCore()
        {
            using (var lockedGl = MakeCurrent())
            {
                foreach (var node in spawnedNodes)
                {
                    Scene.Remove(node, true);
                    node.Delete();
                }

                spawnedNodes.Clear();

                EvaluateAndSpawn();

                foreach (var node in spawnedNodes)
                {
                    node.EnvMaps.Clear();
                    node.EnvMaps.AddRange(Scene.LightingInfo.EnvMaps);
                    node.ShaderEnvMapVisibility = node.ShaderEnvMapVisibility.Store(node.EnvMaps);
                }
            }

            UpdateParameterStates();
            RebuildHandleControls();
        }

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);
            Debug.Assert(definition != null);

            suppressUiEvents = true;

            try
            {
                rebuildTimer = new Timer { Interval = 120 };
                rebuildTimer.Tick += (_, __) =>
                {
                    rebuildTimer.Stop();
                    Rebuild();
                };

                using (UiControl.BeginGroup("Smart Prop"))
                {
                    AddSeedControls();

                    statusLabel = new Label
                    {
                        AutoSize = true,
                        Padding = new Padding(2),
                    };
                    UiControl.AddControl(statusLabel);
                    createdControls.Add(statusLabel);
                }

                var parameters = definition.Variables.Where(static v => v.ExposeAsParameter).ToList();

                if (definition.Choices.Count > 0 || parameters.Count > 0)
                {
                    using var _ = UiControl.BeginGroup("Variables");

                    foreach (var choice in definition.Choices)
                    {
                        AddChoiceControl(choice);
                    }

                    foreach (var parameter in parameters)
                    {
                        AddParameterControl(parameter);
                    }
                }

                RebuildHandleControls();
            }
            finally
            {
                suppressUiEvents = false;
            }

            UpdateParameterStates();

            base.AddUiControls();
        }

        private void UpdateParameterStates()
        {
            if (definition == null)
            {
                return;
            }

            var input = CreateInput();

            foreach (var (variable, control) in parameterControls)
            {
                control.Visible = !definition.EvaluateCondition(variable.HideExpression, input);
                control.Enabled = !definition.EvaluateCondition(variable.ReadOnlyExpression, input);
            }

            if (statusLabel != null)
            {
                statusLabel.Text = $"{modelCount} models";
            }
        }

        private void AddSeedControls()
        {
            Debug.Assert(UiControl != null);

            var seedPanel = AddNumberInput("Seed", 0f, 0f, 1_000_000f, value =>
            {
                var newSeed = (int)MathF.Round(value);

                if (newSeed != seed)
                {
                    seed = newSeed;
                    RequestRebuild();
                }
            });

            var rerollButton = new ThemedButton
            {
                Text = "Re-roll Seed",
                Dock = DockStyle.Top,
            };
            rerollButton.Click += (_, __) => seedPanel.Controls.OfType<ThemedFloatNumeric>().First().Value = Random.Shared.Next(0, 1_000_000);

            UiControl.AddControl(rerollButton);
            createdControls.Add(rerollButton);
        }

        private Panel AddNumberInput(string labelText, float startValue, float minValue, float maxValue, Action<float> onChanged)
        {
            Debug.Assert(UiControl != null);

            var input = RendererControl.CreateFloatInput(labelText, value =>
            {
                if (!suppressUiEvents)
                {
                    onChanged(value);
                }
            }, startValue, minValue, maxValue);

            UiControl.AddControl(input);
            createdControls.Add(input);
            return input;
        }

        private ComboBox AddDropdown(string labelText, string[] items, int defaultIndex, Action<int> onSelected)
        {
            Debug.Assert(UiControl != null);

            var comboBox = UiControl.AddSelection(labelText, (_, index) =>
            {
                if (suppressUiEvents || index < 0 || index >= items.Length)
                {
                    return;
                }

                onSelected(index);
            });

            createdControls.Add(comboBox);
            comboBox.Items.AddRange(items);
            comboBox.SelectedIndex = defaultIndex >= 0 && defaultIndex < items.Length ? defaultIndex : 0;

            return comboBox;
        }

        private static string Label(SmartPropVariableDefinition variable) => variable.DisplayName.Length > 0 ? variable.DisplayName : variable.Name;

        private void AddChoiceControl(SmartPropChoice choice)
        {
            if (choice.Options.Count == 0)
            {
                return;
            }

            var optionNames = choice.Options.Select(static o => o.Name).ToArray();

            AddDropdown(choice.Name.Length > 0 ? choice.Name : "Choice", optionNames, Array.IndexOf(optionNames, choice.DefaultOption), index =>
            {
                parameterOverrides[choice.ElementId] = SmartPropValue.FromString(optionNames[index]);
                RequestRebuild();
            });
        }

        private void SetOverride(SmartPropVariableDefinition variable, SmartPropValue value)
        {
            parameterOverrides[variable.ElementId] = value;
            RequestRebuild();
        }

        private void AddParameterControl(SmartPropVariableDefinition variable)
        {
            Debug.Assert(UiControl != null);

            var controls = new List<Control>();

            if (variable.EnumNames != null)
            {
                var names = variable.EnumNames.ToArray();
                controls.Add(AddDropdown(Label(variable), names, Array.IndexOf(names, variable.DefaultValue.GetString()), index => SetOverride(variable, SmartPropValue.FromString(names[index]))));
            }
            else if (variable.ClassName == "CSmartPropVariable_MaterialGroup")
            {
                controls.Add(AddMaterialGroupControl(variable));
            }
            else
            {
                switch (variable.Type)
                {
                    case SmartPropVariableType.Bool:
                    {
                        var checkBox = UiControl.AddCheckBox(Label(variable), variable.DefaultValue.GetBool(), isChecked =>
                        {
                            if (!suppressUiEvents)
                            {
                                SetOverride(variable, SmartPropValue.FromBool(isChecked));
                            }
                        });
                        createdControls.Add(checkBox);
                        controls.Add(checkBox);
                        break;
                    }

                    case SmartPropVariableType.Int:
                    {
                        var value = variable.DefaultValue.GetInt();
                        var min = (float)(variable.MinValue ?? Math.Min(value, 0));
                        var max = (float)(variable.MaxValue ?? Math.Max(value + 100, 100));
                        controls.Add(AddNumberInput(Label(variable), value, min, MathF.Max(min, max), newValue => SetOverride(variable, SmartPropValue.FromInt((int)MathF.Round(newValue)))));
                        break;
                    }

                    case SmartPropVariableType.Float:
                    {
                        var value = variable.DefaultValue.GetFloat();
                        var min = (float)(variable.MinValue ?? Math.Min(value, 0f));
                        var max = (float)(variable.MaxValue ?? Math.Max(value + 100f, 100f));
                        controls.Add(AddNumberInput(Label(variable), value, min, MathF.Max(min, max), newValue => SetOverride(variable, SmartPropValue.FromFloat(newValue))));
                        break;
                    }

                    case SmartPropVariableType.Color:
                        controls.Add(AddColorControl(variable));
                        break;

                    case SmartPropVariableType.Vector2:
                    case SmartPropVariableType.Vector3:
                    case SmartPropVariableType.Vector4:
                    case SmartPropVariableType.Angles:
                        controls.AddRange(AddVectorInput(variable));
                        break;

                    default:
                        controls.Add(AddTextInput(variable));
                        break;
                }
            }

            foreach (var control in controls)
            {
                parameterControls.Add((variable, control is CheckBox or ComboBox ? control.Parent ?? control : control));
            }
        }

        private Panel AddColorControl(SmartPropVariableDefinition variable)
        {
            Debug.Assert(UiControl != null);

            var color = variable.DefaultValue.GetColor();
            var panel = new Panel { Dock = DockStyle.Top };
            panel.Height = panel.AdjustForDPI(26);

            var button = new ThemedButton
            {
                Text = Label(variable),
                Dock = DockStyle.Fill,
                BackColor = System.Drawing.Color.FromArgb(color.R, color.G, color.B),
            };

            button.Click += (_, __) =>
            {
                using var dialog = new ColorDialog
                {
                    Color = button.BackColor,
                    FullOpen = true,
                };

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    button.BackColor = dialog.Color;
                    SetOverride(variable, SmartPropValue.FromColor(new Color32(dialog.Color.R, dialog.Color.G, dialog.Color.B)));
                }
            };

            panel.Controls.Add(button);
            UiControl.AddControl(panel);
            createdControls.Add(panel);
            return panel;
        }

        private SmartPropHandleEdit GetHandleEdit(SmartPropHandle handle)
            => handleEdits.GetValueOrDefault((PathKey(handle.ElementPath), handle.Name)) ?? new SmartPropHandleEdit();

        private void SetHandleEdit(SmartPropHandle handle, SmartPropHandleEdit edit)
        {
            handleEdits[(PathKey(handle.ElementPath), handle.Name)] = edit;
            RequestRebuild();
        }

        private void RebuildHandleControls()
        {
            if (UiControl == null || lastOutput == null)
            {
                return;
            }

            var signature = string.Join(';', lastOutput.Handles.Select(static h => HandleLabel(h)));

            if (signature == handleSignature)
            {
                return;
            }

            handleSignature = signature;
            RemoveControls(handleControls);

            if (lastOutput.Handles.Count == 0)
            {
                return;
            }

            var handles = lastOutput.Handles.ToList();
            var labels = handles.Select(HandleLabel).ToArray();
            var wasSuppressed = suppressUiEvents;
            suppressUiEvents = true;

            try
            {
                using var _ = UiControl.BeginGroup("Handles");
                var firstNew = createdControls.Count;
                var selected = Math.Max(0, Array.IndexOf(labels, selectedHandleLabel));

                AddDropdown("Handle", labels, selected, index => ShowHandleInputs(handles[index]));
                handleControls.AddRange(createdControls.Skip(firstNew));
                ShowHandleInputs(handles[selected]);
            }
            finally
            {
                suppressUiEvents = wasSuppressed;
            }
        }

        private static string HandleLabel(SmartPropHandle handle)
            => $"{handle.Kind}{(handle.Name.Length > 0 ? " " + handle.Name : string.Empty)} [{PathKey(handle.ElementPath)}]";

        private void RemoveControls(List<Control> controls)
        {
            foreach (var control in controls)
            {
                var target = control is ComboBox ? control.Parent ?? control : control;
                target.Parent?.Controls.Remove(target);
                createdControls.Remove(control);
                target.Dispose();
            }

            controls.Clear();
        }

        private void ShowHandleInputs(SmartPropHandle handle)
        {
            Debug.Assert(UiControl != null);

            RemoveControls(handleInputControls);
            selectedHandleLabel = HandleLabel(handle);

            var wasSuppressed = suppressUiEvents;
            suppressUiEvents = true;

            try
            {
                using var _ = UiControl.BeginGroup("Handles");
                var firstNew = createdControls.Count;
                AddHandleControl(handle);
                handleInputControls.AddRange(createdControls.Skip(firstNew));
            }
            finally
            {
                suppressUiEvents = wasSuppressed;
            }
        }

        private void AddHandleControl(SmartPropHandle handle)
        {
            switch (handle.Kind)
            {
                case SmartPropHandleKind.Rotator:
                {
                    var (min, max) = handle.AngleLimits is var (low, high) && low <= high
                        ? (low - handle.InitialAngle, high - handle.InitialAngle)
                        : (-180f, 180f);

                    AddNumberInput("Angle", GetHandleEdit(handle).DeltaValue, min, max, value => SetHandleEdit(handle, new SmartPropHandleEdit { DeltaValue = value }));
                    break;
                }

                case SmartPropHandleKind.Sizer:
                    AddVectorRow("Min", GetHandleEdit(handle).DeltaMin, value => SetHandleEdit(handle, new SmartPropHandleEdit { DeltaMin = value, DeltaMax = GetHandleEdit(handle).DeltaMax }));
                    AddVectorRow("Max", GetHandleEdit(handle).DeltaMax, value => SetHandleEdit(handle, new SmartPropHandleEdit { DeltaMin = GetHandleEdit(handle).DeltaMin, DeltaMax = value }));
                    break;

                case SmartPropHandleKind.Locator:
                {
                    var (translation, rotation, _) = handle.AllowedEdits;
                    var stored = GetHandleEdit(handle).DeltaTransform;
                    var offset = stored.Position;
                    var angles = EntityTransformHelper.ToEulerAngles(stored.Rotation);

                    void Update() => SetHandleEdit(handle, new SmartPropHandleEdit
                    {
                        DeltaTransform = new SmartPropTransform(offset, 1f, EntityTransformHelper.EulerAnglesToQuaternion(angles)),
                    });

                    if (translation)
                    {
                        AddVectorRow("Offset", offset, value =>
                        {
                            offset = value;
                            Update();
                        });
                    }

                    if (rotation)
                    {
                        AddVectorRow("Angles", angles, value =>
                        {
                            angles = value;
                            Update();
                        });
                    }

                    break;
                }

                default:
                    break;
            }
        }

        private void AddVectorRow(string labelText, Vector3 initial, Action<Vector3> onChanged)
        {
            var value = initial;
            var axes = "XYZ";

            for (var axis = 0; axis < 3; axis++)
            {
                var capturedAxis = axis;
                AddNumberInput($"{labelText} {axes[axis]}", value[axis], -100_000f, 100_000f, newValue =>
                {
                    value[capturedAxis] = newValue;
                    onChanged(value);
                });
            }
        }

        private Control AddMaterialGroupControl(SmartPropVariableDefinition variable)
        {
            var groups = string.IsNullOrEmpty(variable.ModelName) || GuiContext.LoadFileCompiled(variable.ModelName)?.DataBlock is not Model model
                ? []
                : model.GetMaterialGroups().Select(static g => g.Name).ToArray();

            if (groups.Length == 0)
            {
                return AddTextInput(variable);
            }

            var defaultGroup = variable.DefaultValue.GetString();
            var defaultIndex = defaultGroup.Length == 0 ? 0 : Array.IndexOf(groups, defaultGroup);

            return AddDropdown(Label(variable), groups, defaultIndex, index => SetOverride(variable, SmartPropValue.FromString(groups[index])));
        }

        private Panel AddTextInput(SmartPropVariableDefinition variable)
        {
            Debug.Assert(UiControl != null);

            var panel = new Panel { Dock = DockStyle.Top };
            panel.Height = panel.AdjustForDPI(44);
            var label = new Label { Text = Label(variable), Dock = DockStyle.Top, AutoSize = false };
            label.Height = label.AdjustForDPI(18);
            var textBox = new ThemedTextBox { Dock = DockStyle.Top, Text = variable.DefaultValue.GetString() };

            void Commit()
            {
                if (!suppressUiEvents)
                {
                    SetOverride(variable, SmartPropValue.FromString(textBox.Text.Trim()));
                }
            }

            textBox.LostFocus += (_, __) => Commit();
            textBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    Commit();
                }
            };

            panel.Controls.Add(textBox);
            panel.Controls.Add(label);
            UiControl.AddControl(panel);
            createdControls.Add(panel);
            return panel;
        }

        private List<Control> AddVectorInput(SmartPropVariableDefinition variable)
        {
            var axisCount = variable.DefaultValue.Count;
            var values = Enumerable.Range(0, axisCount).Select(i => variable.DefaultValue.GetItem(i)).ToArray();
            var axes = "XYZW";
            var panels = new List<Control>();

            for (var axis = 0; axis < axisCount; axis++)
            {
                var capturedAxis = axis;
                panels.Add(AddNumberInput($"{Label(variable)} {axes[axis]}", (float)values[axis], -100_000f, 100_000f, newValue =>
                {
                    values[capturedAxis] = newValue;
                    SetOverride(variable, SmartPropValue.FromArray([.. values]));
                }));
            }

            return panels;
        }
    }
}
