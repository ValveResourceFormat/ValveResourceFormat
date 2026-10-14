using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace GUI.Types.GLViewers
{
    class GLSmartPropViewer : GLSingleNodeViewer
    {
        private sealed class ElementPathComparer : IEqualityComparer<int[]>
        {
            public static readonly ElementPathComparer Instance = new();

            public bool Equals(int[]? x, int[]? y) => x.AsSpan().SequenceEqual(y);

            public int GetHashCode(int[] obj)
            {
                var hash = new HashCode();

                foreach (var value in obj)
                {
                    hash.Add(value);
                }

                return hash.ToHashCode();
            }
        }

        /// <summary>
        /// Identifies baked vertex streams: the model, its LoD level, and the linear map or deformation baked into them.
        /// </summary>
        private sealed record BakeKey(string ModelName, int? LodLevel, Matrix4x4 Matrix, SmartPropDeformer? Deformer)
        {
            public bool Equals(BakeKey? other)
                => other is not null
                && ModelName == other.ModelName
                && LodLevel == other.LodLevel
                && Matrix == other.Matrix
                && DeformerEquals(Deformer, other.Deformer);

            public override int GetHashCode() => HashCode.Combine(ModelName, LodLevel, Matrix, Deformer?.Frame, Deformer?.Size);

            private static bool DeformerEquals(SmartPropDeformer? a, SmartPropDeformer? b)
            {
                if (a is null || b is null)
                {
                    return a is null && b is null;
                }

                return a.Frame == b.Frame
                    && a.Size == b.Size
                    && a.Divisions == b.Divisions
                    && a.InterpolationMode == b.InterpolationMode
                    && a.Points.AsSpan().SequenceEqual(b.Points)
                    && a.Handles.AsSpan().SequenceEqual(b.Handles);
            }
        }

        private const float LocatorAxisLength = 16f;

        private static readonly Color32 SizerColor = new(255, 200, 80);

        private sealed record PlacedModel(Model Model, SmartPropModelInstance Instance, Matrix4x4 Transform, BakedVertexStreams? Streams);

        private sealed record Evaluation(SmartPropEvaluationInput Input, SmartPropOutput Output, List<PlacedModel> Models, HashSet<BakeKey> UsedBakes);

        private readonly SmartProp smartProp;
        private readonly List<ModelSceneNode> spawnedNodes = [];
        private readonly Dictionary<int, SmartPropValue> parameterOverrides = [];
        private readonly Dictionary<int[], Dictionary<string, SmartPropHandleEdit>> handleEdits = new(ElementPathComparer.Instance);
        private readonly Dictionary<int[], SmartPropElementState> storedStates = new(ElementPathComparer.Instance);
        private readonly Dictionary<BakeKey, BakedVertexStreams> bakedStreams = [];
        private List<(ModelSceneNode Node, float FadeEndSize)> detailNodes = [];
        private SmartPropOutput? lastOutput;
        private readonly List<Control> handleControls = [];
        private readonly List<Control> handleInputControls = [];
        private readonly List<SmartPropChoiceRequest> pendingChoiceRequests = [];
        private LineSceneNode? handleLines;
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
        private int stateGeneration;
        private int modelCount;
        private bool suppressUiEvents;
        private bool rebuildRunning;
        private bool rebuildPending;
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

            var evaluation = Evaluate(definition, evaluator, CreateInput());
            Spawn(evaluation);
            StoreEvaluation(evaluation, stateGeneration);
        }

        private SmartPropEvaluationInput CreateInput() => new()
        {
            MasterSeed = seed,
            ParameterOverrides = new Dictionary<int, SmartPropValue>(parameterOverrides),
            ElementStates = CreateElementStates(),
            ChoiceRequests = pendingChoiceRequests.Count > 0 ? [.. pendingChoiceRequests] : null,
        };

        private List<SmartPropElementState> CreateElementStates()
        {
            var states = new Dictionary<int[], SmartPropElementState>(ElementPathComparer.Instance);

            foreach (var (path, stored) in storedStates)
            {
                states[path] = new SmartPropElementState
                {
                    ElementPath = path,
                    RandomSeed = stored.RandomSeed,
                    ChoiceValue = stored.ChoiceValue,
                };
            }

            foreach (var (path, edits) in handleEdits)
            {
                if (!states.TryGetValue(path, out var state))
                {
                    state = new SmartPropElementState { ElementPath = path };
                    states[path] = state;
                }

                foreach (var (name, edit) in edits)
                {
                    state.HandleEdits[name] = edit;
                }
            }

            return [.. states.Values];
        }

        private static string PathKey(int[] path) => string.Join(',', path.Select(static index => index.ToString(CultureInfo.InvariantCulture)));

        /// <summary>
        /// Evaluates the smart prop and bakes the vertex streams its placements need. Touches no GL state, so it can run
        /// off the UI thread.
        /// </summary>
        private Evaluation Evaluate(SmartPropDefinition definition, SmartPropEvaluator evaluator, SmartPropEvaluationInput input)
        {
            var output = evaluator.Evaluate(definition, input);
            var evaluation = new Evaluation(input, output, new List<PlacedModel>(output.Models.Count), []);

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

                evaluation.Models.Add(Place(evaluation, instance, model));
            }

            return evaluation;
        }

        private PlacedModel Place(Evaluation evaluation, SmartPropModelInstance instance, Model model)
        {
            int? lodLevel = instance.LodLevel >= 0 ? instance.LodLevel : null;

            if (evaluation.Output.GetVertexDeformer(instance) is { } deformer)
            {
                var deformedKey = new BakeKey(instance.ModelName, lodLevel, instance.GetMatrix(), evaluation.Output.Deformers[instance.DeformerIndex]);
                var map = new VertexAttributeMap(deformer.DeformPosition, deformer.DeformNormal, deformer.DeformTangent);
                return new PlacedModel(model, instance, Matrix4x4.Identity, GetBakedStreams(evaluation, deformedKey, model, map));
            }

            var transform = instance.Transform;
            var bakedScale = transform.Scale < 0f ? -instance.ModelScale : instance.ModelScale;

            if (bakedScale.X > 0f && bakedScale.X == bakedScale.Y && bakedScale.Y == bakedScale.Z)
            {
                return new PlacedModel(model, instance, instance.GetMatrix(), null);
            }

            var linear = Matrix4x4.CreateScale(bakedScale);
            var key = new BakeKey(instance.ModelName, lodLevel, linear, null);
            var nodeTransform = (transform with { Scale = MathF.Abs(transform.Scale) }).ToMatrix();

            return new PlacedModel(model, instance, nodeTransform, GetBakedStreams(evaluation, key, model, VertexAttributeMap.FromMatrix(linear)));
        }

        private BakedVertexStreams GetBakedStreams(Evaluation evaluation, BakeKey key, Model model, VertexAttributeMap map)
        {
            if (!bakedStreams.TryGetValue(key, out var streams))
            {
                streams = BakedVertexStreams.Create(model, GuiContext, key.LodLevel, map);
                bakedStreams.Add(key, streams);
            }

            evaluation.UsedBakes.Add(key);
            return streams;
        }

        private void Spawn(Evaluation evaluation)
        {
            foreach (var node in spawnedNodes)
            {
                Scene.Remove(node, true);
                node.Delete();
            }

            spawnedNodes.Clear();

            foreach (var key in bakedStreams.Keys.Where(key => !evaluation.UsedBakes.Contains(key)).ToList())
            {
                bakedStreams[key].Delete();
                bakedStreams.Remove(key);
            }

            var output = evaluation.Output;
            var newDetailNodes = new List<(ModelSceneNode Node, float FadeEndSize)>();

            foreach (var (model, instance, transform, streams) in evaluation.Models)
            {
                var node = new ModelSceneNode(Scene, model, instance.MaterialGroup) { Transform = transform };

                if (streams != null)
                {
                    node.SetBakedVertexStreams(streams);
                }
                else if (instance.LodLevel >= 0)
                {
                    node.SetOverrideLod(instance.LodLevel);
                }

                node.Tint = new Vector3(instance.Tint.R, instance.Tint.G, instance.Tint.B) / 255f;

                if (!instance.CastShadows)
                {
                    node.Flags |= ObjectTypeFlags.NoShadows;
                }

                if (instance.DetailObject)
                {
                    newDetailNodes.Add((node, instance.DetailFadeEndSize));
                }

                ApplyMaterialSets(node, output, instance);

                Scene.Add(node, true);
                spawnedNodes.Add(node);
            }

            detailNodes = newDetailNodes;
            modelCount = spawnedNodes.Count;

            SpawnHandleLines(output);
        }

        /// <summary>
        /// Replaces the materials the instance's override set names, then tints the materials its tint set names. Both
        /// match the material a draw call draws after the material group, by normalized resource name.
        /// </summary>
        private static void ApplyMaterialSets(ModelSceneNode node, SmartPropOutput output, SmartPropModelInstance instance)
        {
            if (instance.MaterialOverrideSetIndex >= 0)
            {
                var overrides = output.MaterialOverrideSets[instance.MaterialOverrideSetIndex];

                foreach (var mesh in node.AllMeshes)
                {
                    foreach (var drawCall in mesh.DrawCalls.ToList())
                    {
                        if (overrides.TryGetValue(SmartPropOutput.NormalizeResourceName(drawCall.Material.Material.Name), out var replacement)
                            && replacement.EndsWith(".vmat", StringComparison.Ordinal))
                        {
                            mesh.ReplaceMaterial(drawCall, replacement);
                        }
                    }
                }
            }

            if (instance.MaterialTintSetIndex >= 0)
            {
                var tints = output.MaterialTintSets[instance.MaterialTintSetIndex];

                foreach (var drawCall in node.AllMeshes.SelectMany(static mesh => mesh.DrawCalls))
                {
                    if (tints.TryGetValue(SmartPropOutput.NormalizeResourceName(drawCall.Material.Material.Name), out var tint))
                    {
                        drawCall.TintColor *= new Vector4(ColorSpace.SrgbGammaToLinear(new Vector3(tint.R, tint.G, tint.B) / 255f), 1f);
                    }
                }
            }
        }

        private void SpawnHandleLines(SmartPropOutput output)
        {
            if (handleLines != null)
            {
                Scene.Remove(handleLines, true);
                handleLines.Delete();
                handleLines = null;
            }

            var vertices = new List<SimpleVertex>();

            foreach (var handle in output.Handles)
            {
                AddHandleLines(vertices, handle);
            }

            foreach (var handle in output.ChoiceHandles)
            {
                AddChoiceHandleLines(vertices, handle);
            }

            if (vertices.Count == 0)
            {
                return;
            }

            handleLines = new LineSceneNode(Scene, [.. vertices]);
            Scene.Add(handleLines, true);
        }

        private void AddHandleLines(List<SimpleVertex> vertices, SmartPropHandle handle)
        {
            var matrix = handle.Transform.ToMatrix();
            Vector3 Point(Vector3 local) => Vector3.Transform(local, matrix);

            switch (handle.Kind)
            {
                case SmartPropHandleKind.Locator when handle.Configurable:
                {
                    var length = LocatorAxisLength * handle.DisplaySize;
                    ShapeSceneNode.AddLine(vertices, Point(Vector3.Zero), Point(new Vector3(length, 0f, 0f)), new Color32(255, 140, 140));
                    ShapeSceneNode.AddLine(vertices, Point(Vector3.Zero), Point(new Vector3(0f, length, 0f)), new Color32(140, 255, 140));
                    ShapeSceneNode.AddLine(vertices, Point(Vector3.Zero), Point(new Vector3(0f, 0f, length)), new Color32(140, 140, 255));
                    break;
                }

                case SmartPropHandleKind.Rotator:
                    ShapeSceneNode.AddLine(vertices, Point(Vector3.Zero), Point(new Vector3(handle.DisplaySize, 0f, 0f)), handle.DisplayColor);
                    AddCircle(vertices, Point, handle.DisplaySize, handle.DisplayColor);
                    break;

                case SmartPropHandleKind.Sizer:
                {
                    var (min, max) = GetSizerExtents(handle);
                    var box = new List<SimpleVertex>();
                    ShapeSceneNode.AddBox(box, new AABB(Vector3.Min(min, max), Vector3.Max(min, max)), SizerColor);

                    foreach (var vertex in box)
                    {
                        vertices.Add(new SimpleVertex(Point(vertex.Position), SizerColor));
                    }

                    break;
                }

                default:
                    break;
            }
        }

        private static void AddChoiceHandleLines(List<SimpleVertex> vertices, SmartPropChoiceHandle handle)
        {
            var matrix = handle.Transform.ToMatrix();
            Vector3 Point(Vector3 local) => Vector3.Transform(local, matrix);
            var half = handle.Size * 0.5f;

            switch (handle.Shape)
            {
                case SmartPropHandleShape.Square:
                    AddPolygon(vertices, Point, [new(0f, -half, -half), new(0f, half, -half), new(0f, half, half), new(0f, -half, half)], handle.Color);
                    break;

                case SmartPropHandleShape.Diamond:
                    AddPolygon(vertices, Point, [new(0f, 0f, -half), new(0f, half, 0f), new(0f, 0f, half), new(0f, -half, 0f)], handle.Color);
                    break;

                case SmartPropHandleShape.Circle:
                    AddCircle(vertices, Point, half, handle.Color);
                    break;

                default:
                    break;
            }
        }

        /// <summary>Adds a circle around the local X axis.</summary>
        private static void AddCircle(List<SimpleVertex> vertices, Func<Vector3, Vector3> point, float radius, Color32 color)
        {
            const int Segments = 32;
            var corners = new Vector3[Segments];

            for (var i = 0; i < Segments; i++)
            {
                var (sin, cos) = MathF.SinCos(MathF.Tau * i / Segments);
                corners[i] = new Vector3(0f, cos * radius, sin * radius);
            }

            AddPolygon(vertices, point, corners, color);
        }

        private static void AddPolygon(List<SimpleVertex> vertices, Func<Vector3, Vector3> point, Vector3[] corners, Color32 color)
        {
            for (var i = 0; i < corners.Length; i++)
            {
                ShapeSceneNode.AddLine(vertices, point(corners[i]), point(corners[(i + 1) % corners.Length]), color);
            }
        }

        /// <summary>The box a sizer handle spans with its stored edit, clamped to its limits as the evaluation does.</summary>
        private (Vector3 Min, Vector3 Max) GetSizerExtents(SmartPropHandle handle)
        {
            var edit = GetHandleEdit(handle);
            var min = handle.InitialExtents.Min + edit.DeltaMin;
            var max = handle.InitialExtents.Max + edit.DeltaMax;
            var (limitMin, limitMax) = handle.ExtentLimits;

            for (var i = 0; i < 3; i++)
            {
                if (limitMax[i] > limitMin[i])
                {
                    min[i] = Math.Clamp(min[i], limitMin[i], limitMax[i]);
                    max[i] = Math.Clamp(max[i], limitMin[i], limitMax[i]);
                }
            }

            return (min, max);
        }

        /// <summary>
        /// Keeps the seeds and choices the evaluation used, so later edits re-evaluate the same variation, unless the seed
        /// changed since the evaluation started. The choice requests the evaluation applied are done.
        /// </summary>
        private void StoreEvaluation(Evaluation evaluation, int generation)
        {
            lastOutput = evaluation.Output;

            if (evaluation.Input.ChoiceRequests is { } applied)
            {
                pendingChoiceRequests.RemoveAll(applied.Contains);
            }

            if (generation != stateGeneration)
            {
                return;
            }

            storedStates.Clear();

            foreach (var state in evaluation.Output.ElementStates.Where(static state => state.Used))
            {
                storedStates[state.ElementPath] = state;
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

        private async void Rebuild()
        {
            if (suppressUiEvents || disposed || definition == null || evaluator == null)
            {
                return;
            }

            if (rebuildRunning)
            {
                rebuildPending = true;
                return;
            }

            rebuildRunning = true;

            try
            {
                var input = CreateInput();
                var generation = stateGeneration;
                var (currentDefinition, currentEvaluator) = (definition, evaluator);
                var evaluation = await Task.Run(() => Evaluate(currentDefinition, currentEvaluator, input)).ConfigureAwait(true);

                if (disposed)
                {
                    return;
                }

                using (var lockedGl = MakeCurrent())
                {
                    Spawn(evaluation);

                    foreach (var node in spawnedNodes)
                    {
                        node.EnvMaps.Clear();
                        node.EnvMaps.AddRange(Scene.LightingInfo.EnvMaps);
                        node.ShaderEnvMapVisibility = node.ShaderEnvMapVisibility.Store(node.EnvMaps);
                    }
                }

                StoreEvaluation(evaluation, generation);
                UpdateParameterStates();
                RebuildHandleControls();
            }
            catch (Exception e) when (!disposed)
            {
                Log.Error(nameof(GLSmartPropViewer), $"Smart prop rebuild failed: {e}");

                if (statusLabel != null)
                {
                    statusLabel.Text = "Evaluation failed (see console)";
                }
            }
            finally
            {
                rebuildRunning = false;

                if (rebuildPending && !disposed)
                {
                    rebuildPending = false;
                    Rebuild();
                }
            }
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
                statusLabel.Text = string.Create(CultureInfo.InvariantCulture, $"{modelCount} models");
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
                    stateGeneration++;
                    storedStates.Clear();
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

        private static string DisplayLabel(SmartPropVariableDefinition variable) => variable.DisplayName.Length > 0 ? variable.DisplayName : variable.Name;

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
                controls.Add(AddDropdown(DisplayLabel(variable), names, Array.IndexOf(names, variable.DefaultValue.GetString()), index => SetOverride(variable, SmartPropValue.FromString(names[index]))));
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
                        var checkBox = UiControl.AddCheckBox(DisplayLabel(variable), variable.DefaultValue.GetBool(), isChecked =>
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
                        controls.Add(AddNumberInput(DisplayLabel(variable), value, min, MathF.Max(min, max), newValue => SetOverride(variable, SmartPropValue.FromInt((int)MathF.Round(newValue)))));
                        break;
                    }

                    case SmartPropVariableType.Float:
                    {
                        var value = variable.DefaultValue.GetFloat();
                        var min = (float)(variable.MinValue ?? Math.Min(value, 0f));
                        var max = (float)(variable.MaxValue ?? Math.Max(value + 100f, 100f));
                        controls.Add(AddNumberInput(DisplayLabel(variable), value, min, MathF.Max(min, max), newValue => SetOverride(variable, SmartPropValue.FromFloat(newValue))));
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
                Text = DisplayLabel(variable),
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
            => handleEdits.GetValueOrDefault(handle.ElementPath)?.GetValueOrDefault(handle.Name) ?? new SmartPropHandleEdit();

        private void SetHandleEdit(SmartPropHandle handle, SmartPropHandleEdit edit)
        {
            if (!handleEdits.TryGetValue(handle.ElementPath, out var edits))
            {
                edits = new(StringComparer.OrdinalIgnoreCase);
                handleEdits[handle.ElementPath] = edits;
            }

            edits[handle.Name] = edit;
            RequestRebuild();
        }

        private void RebuildHandleControls()
        {
            if (UiControl == null || lastOutput == null)
            {
                return;
            }

            var entries = lastOutput.Handles
                .Where(static handle => handle.Kind != SmartPropHandleKind.Locator || handle.Configurable)
                .Select(handle => (Label: HandleLabel(handle), AddInputs: (Action)(() => AddHandleControl(handle))))
                .Concat(lastOutput.ChoiceHandles.Select(handle => (Label: ChoiceHandleLabel(handle), AddInputs: (Action)(() => AddChoiceHandleControl(handle)))))
                .ToList();

            var signature = string.Join(';', entries.Select(static entry => entry.Label));

            if (signature == handleSignature)
            {
                return;
            }

            handleSignature = signature;
            RemoveControls(handleControls);
            RemoveControls(handleInputControls);

            if (entries.Count == 0)
            {
                return;
            }

            var labels = entries.Select(static entry => entry.Label).ToArray();
            var wasSuppressed = suppressUiEvents;
            suppressUiEvents = true;

            try
            {
                using var _ = UiControl.BeginGroup("Handles");
                var firstNew = createdControls.Count;
                var selected = Math.Max(0, Array.IndexOf(labels, selectedHandleLabel));

                AddDropdown("Handle", labels, selected, index => ShowHandleInputs(entries[index].Label, entries[index].AddInputs));
                handleControls.AddRange(createdControls.Skip(firstNew));
                ShowHandleInputs(entries[selected].Label, entries[selected].AddInputs);
            }
            finally
            {
                suppressUiEvents = wasSuppressed;
            }
        }

        private static string HandleLabel(SmartPropHandle handle)
            => $"{handle.Kind}{(handle.Name.Length > 0 ? " " + handle.Name : string.Empty)} [{PathKey(handle.ElementPath)}]";

        private static string ChoiceHandleLabel(SmartPropChoiceHandle handle) => $"Pick One [{PathKey(handle.ElementPath)}]";

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

        private void ShowHandleInputs(string label, Action addInputs)
        {
            Debug.Assert(UiControl != null);

            RemoveControls(handleInputControls);
            selectedHandleLabel = label;

            var wasSuppressed = suppressUiEvents;
            suppressUiEvents = true;

            try
            {
                using var _ = UiControl.BeginGroup("Handles");
                var firstNew = createdControls.Count;
                addInputs();
                handleInputControls.AddRange(createdControls.Skip(firstNew));
            }
            finally
            {
                suppressUiEvents = wasSuppressed;
            }
        }

        private void AddChoiceHandleControl(SmartPropChoiceHandle handle)
        {
            Debug.Assert(UiControl != null);

            foreach (var (text, action) in (ReadOnlySpan<(string, SmartPropChoiceAction)>)
                [("Previous", SmartPropChoiceAction.Previous), ("Next", SmartPropChoiceAction.Next), ("None", SmartPropChoiceAction.ChooseNone), ("Re-roll", SmartPropChoiceAction.Randomize)])
            {
                var button = new ThemedButton
                {
                    Text = text,
                    Dock = DockStyle.Top,
                };

                button.Click += (_, __) =>
                {
                    pendingChoiceRequests.Add(new SmartPropChoiceRequest(handle.ElementPath, action));
                    RequestRebuild();
                };

                UiControl.AddControl(button);
                createdControls.Add(button);
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

                    AddNumberInput("Angle", GetHandleEdit(handle).DeltaValue, min, max, value =>
                    {
                        if (handle.SnappingIncrement > 0f)
                        {
                            value = MathF.Round((handle.InitialAngle + value) / handle.SnappingIncrement) * handle.SnappingIncrement - handle.InitialAngle;
                        }

                        SetHandleEdit(handle, new SmartPropHandleEdit { DeltaValue = value });
                    });
                    break;
                }

                case SmartPropHandleKind.Sizer:
                    AddSizerInputs(handle);
                    break;

                case SmartPropHandleKind.Locator:
                {
                    var (translation, rotation, scaling) = handle.AllowedEdits;
                    var stored = GetHandleEdit(handle).DeltaTransform;
                    var offset = stored.Position;
                    var angles = EntityTransformHelper.ToEulerAngles(stored.Rotation);
                    var scale = stored.Scale;

                    void Update() => SetHandleEdit(handle, new SmartPropHandleEdit
                    {
                        DeltaTransform = new SmartPropTransform(offset, scale, EntityTransformHelper.EulerAnglesToQuaternion(angles)),
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

                    if (scaling)
                    {
                        AddNumberInput("Scale", scale, 0.01f, 100f, value =>
                        {
                            scale = value;
                            Update();
                        });
                    }

                    break;
                }

                default:
                    break;
            }
        }

        /// <summary>
        /// Adds an input per sizer extent component that is written to a variable, limited to the constraints when the
        /// axis has them.
        /// </summary>
        private void AddSizerInputs(SmartPropHandle handle)
        {
            var axes = "XYZ";
            var (limitMin, limitMax) = handle.ExtentLimits;
            var (editableMinX, editableMinY, editableMinZ) = handle.EditableMin;
            var (editableMaxX, editableMaxY, editableMaxZ) = handle.EditableMax;
            ReadOnlySpan<bool> editableMin = [editableMinX, editableMinY, editableMinZ];
            ReadOnlySpan<bool> editableMax = [editableMaxX, editableMaxY, editableMaxZ];

            for (var axis = 0; axis < 3; axis++)
            {
                var bounded = limitMax[axis] > limitMin[axis];

                foreach (var isMax in (ReadOnlySpan<bool>)[false, true])
                {
                    if (!(isMax ? editableMax[axis] : editableMin[axis]))
                    {
                        continue;
                    }

                    var capturedAxis = axis;
                    var initial = isMax ? handle.InitialExtents.Max[axis] : handle.InitialExtents.Min[axis];
                    var edit = GetHandleEdit(handle);
                    var start = isMax ? edit.DeltaMax[axis] : edit.DeltaMin[axis];
                    var (low, high) = bounded ? (limitMin[axis] - initial, limitMax[axis] - initial) : (-100_000f, 100_000f);

                    AddNumberInput($"{(isMax ? "Max" : "Min")} {axes[axis]}", start, low, high, value =>
                    {
                        var current = GetHandleEdit(handle);
                        var (deltaMin, deltaMax) = (current.DeltaMin, current.DeltaMax);

                        if (isMax)
                        {
                            deltaMax[capturedAxis] = value;
                        }
                        else
                        {
                            deltaMin[capturedAxis] = value;
                        }

                        SetHandleEdit(handle, new SmartPropHandleEdit { DeltaMin = deltaMin, DeltaMax = deltaMax });
                    });
                }
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

            return AddDropdown(DisplayLabel(variable), groups, defaultIndex, index => SetOverride(variable, SmartPropValue.FromString(groups[index])));
        }

        private Panel AddTextInput(SmartPropVariableDefinition variable)
        {
            Debug.Assert(UiControl != null);

            var panel = new Panel { Dock = DockStyle.Top };
            panel.Height = panel.AdjustForDPI(44);
            var label = new Label { Text = DisplayLabel(variable), Dock = DockStyle.Top, AutoSize = false };
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
                panels.Add(AddNumberInput($"{DisplayLabel(variable)} {axes[axis]}", (float)values[axis], -100_000f, 100_000f, newValue =>
                {
                    values[capturedAxis] = newValue;
                    SetOverride(variable, SmartPropValue.FromArray([.. values]));
                }));
            }

            return panels;
        }
    }
}
