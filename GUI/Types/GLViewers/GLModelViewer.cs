using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.GLViewers
{
    class GLModelViewer : GLSingleNodeViewer
    {
        protected Model? model { get; init; }
        private readonly bool ModelViewerWithAnimGraphSupport;
        private PhysAggregateData? phys;

        private readonly List<AnimationListEntry> animationIndexMap = [];

        /// <summary>What an animation dropdown entry plays: a clip by name, a graph by index, or neither.</summary>
        private readonly record struct AnimationListEntry(string? Animation, int GraphIndex = -1);

        public ComboBox? animationComboBox { get; protected set; }
        protected CheckBox? animationPlayPause;
        private CheckBox? rootMotionCheckBox;
        private CheckBox? additiveCheckBox;
        private CheckBox? showSkeletonCheckbox;
        private CheckBox? showAttachmentsCheckbox;
        private CheckBox? showParticlesCheckbox;
        private ComboBox? hitboxComboBox;
        private Label? animationTimeLabel;
        private GLViewerSliderControl? animationTrackBar;
        private GLViewerSliderControl? slowmodeTrackBar;
        private GLViewerMultiSelectionControl? attachmentList;
        public CheckedListBox? meshGroupListBox { get; private set; }
        public ComboBox? materialGroupListBox { get; private set; }
        private ComboBox? lodComboBox;
        private bool hasSelectableLods;
        private bool modelStatsDirty;
        private bool modelStatsPosted;
        private int statsLod = -1;
        private ModelSceneNode? modelSceneNode;
        protected AnimationController? animationController;
        private AnimationGraph? animGraph;
        private GraphSession[] graphSessions = [];
        private GraphSession? activeGraphSession;
        private Panel? graphControlsHost;
        protected SkeletonSceneNode? skeletonSceneNode;
        private HitboxSetSceneNode? hitboxSetSceneNode;
        private List<ParticleSceneNode> modelParticleNodes = [];
        private CheckedListBox? physicsGroupsComboBox;
        private int animationComboBoxCurrentIndex = -1;

        public GLModelViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext) : base(vrfGuiContext, rendererContext)
        {
            //
        }

        public GLModelViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, Model model) : base(vrfGuiContext, rendererContext)
        {
            this.model = model;
            ModelViewerWithAnimGraphSupport = true;
        }

        public GLModelViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, PhysAggregateData phys) : base(vrfGuiContext, rendererContext)
        {
            this.phys = phys;
        }

        public override void Dispose()
        {
            // Delete GL resources before the base disposes the GL context
            graphGizmos?.Delete();
            graphGizmos = null;

            base.Dispose();

            animationComboBox?.Dispose();
            animationPlayPause?.Dispose();
            animationTimeLabel?.Dispose();
            animationTrackBar?.Dispose();
            slowmodeTrackBar?.Dispose();
            attachmentList?.Dispose();
            meshGroupListBox?.Dispose();
            materialGroupListBox?.Dispose();
            lodComboBox?.Dispose();
            physicsGroupsComboBox?.Dispose();
            rootMotionCheckBox?.Dispose();
            additiveCheckBox?.Dispose();
            showSkeletonCheckbox?.Dispose();
            showAttachmentsCheckbox?.Dispose();
            showParticlesCheckbox?.Dispose();
            hitboxComboBox?.Dispose();
            graphControlsHost?.Dispose();
        }

        private void AddAnimationListComboBox()
        {
            Debug.Assert(UiControl != null);
            Debug.Assert(animationController != null);

            animationComboBox = UiControl.AddSelection("Animation", (animation, i) =>
            {
                // Initialize on first call
                if (animationComboBoxCurrentIndex < -1)
                {
                    animationComboBoxCurrentIndex = i;
                    return;
                }

                if (i < 0)
                {
                    return;
                }

                if (animationComboBox!.Items[i] is ThemedComboBoxItem item && item.IsHeader)
                {
                    // Skip header selection and jump to adjacent non-header item
                    animationComboBox.SelectedIndex = animationComboBoxCurrentIndex > i ? i - 1 : i + 1;
                    return;
                }

                animationComboBoxCurrentIndex = i;
                Debug.Assert(modelSceneNode != null);

                var entry = animationIndexMap.Count > i ? animationIndexMap[i] : default;
                var session = entry.GraphIndex >= 0 ? graphSessions[entry.GraphIndex] : null;
                var graph = session != null ? LoadGraph(session) : null;

                if (session != null && graph != null)
                {
                    EnsureGraphControls(session);
                }

                using (var lockedGL = MakeCurrent())
                {
                    modelSceneNode.SetAnimationGraph(graph);

                    if (graph == null)
                    {
                        if (entry.Animation is string animationId)
                        {
                            modelSceneNode.SetAnimationByName(animationId);
                        }
                        else
                        {
                            modelSceneNode.SetAnimation(null);
                        }
                    }

                    gizmoDragging = false;
                    activeGraphSession = graph != null ? session : null;
                    animGraph = graph;
                    graphGizmoBindings = activeGraphSession?.GizmoBindings ?? [];
                }

                ShowGraphControls(activeGraphSession);
                SyncAnimationToggles();
            });
        }

        protected void AddAnimationControls(bool includeAnimationList = true)
        {
            Debug.Assert(UiControl != null);
            Debug.Assert(animationController != null);

            using var _ = UiControl.BeginGroup("Animation");

            if (includeAnimationList)
            {
                AddAnimationListComboBox();
            }

            animationTimeLabel = new Label()
            {
                AutoSize = true,
            };
            UiControl.AddControl(animationTimeLabel);

            animationPlayPause = UiControl.AddCheckBox("Autoplay", true, isChecked =>
            {
                if (animationController != null)
                {
                    animationController.IsPaused = !isChecked;
                }
            });
            animationTrackBar = UiControl.AddTrackBar(frame =>
            {
                if (animationController?.ActiveAnimation is { CycleFrames: > 0 } animation)
                {
                    animationController.Frame = (int)MathF.Round(frame * animation.CycleFrames);
                }
            });

            slowmodeTrackBar = UiControl.AddTrackBar(value =>
            {
                animationController.FrametimeMultiplier = value;
            }, animationController.FrametimeMultiplier);

            animationPlayPause.Enabled = false;
            animationTrackBar.Enabled = false;
            slowmodeTrackBar.Enabled = false;
            slowmodeTrackBar.Slider.Value = animationController.FrametimeMultiplier;

            var previousPaused = false;
            animationTrackBar.Slider.MouseDown += (_, __) =>
            {
                previousPaused = animationController.IsPaused;
                animationController.IsPaused = true;
            };
            animationTrackBar.Slider.MouseUp += (_, __) =>
            {
                animationController.IsPaused = previousPaused;
            };

            rootMotionCheckBox = UiControl.AddCheckBox("Show Root Motion", enableRootMotion, (isChecked) =>
            {
                enableRootMotion = isChecked;
                rootMotionResetPending = true;
            });

            rootMotionCheckBox.Checked = false;
            rootMotionCheckBox.Enabled = false;

            additiveCheckBox = UiControl.AddCheckBox("Additive (over bind pose)", false, isChecked =>
            {
                animationController.ApplyAdditive = isChecked;
            });

            additiveCheckBox.Enabled = false;

            if (graphSessions.Length > 0)
            {
                // Graph controls sit under the playback controls and replace the clip ones while a graph plays.
                // Each graph's panel stays visible and laid out in this clipping host, which is sized to the
                // front one, because showing a hidden panel of this many controls relayouts every row.
                var host = new Panel
                {
                    Height = 0,
                };

                host.SizeChanged += (_, _) =>
                {
                    foreach (Control panel in host.Controls)
                    {
                        panel.Width = host.ClientSize.Width;
                    }
                };

                graphControlsHost = host;
                UiControl.AddControl(host);
            }
        }

        /// <summary>
        /// Syncs the root motion and additive toggles to the active animation: root motion defaults
        /// on for animations that carry it, the additive checkbox mirrors the player state.
        /// </summary>
        protected void SyncAnimationToggles()
        {
            Debug.Assert(animationController != null);

            var activeAnimation = animationController.ActiveAnimation;
            var hasRootMotion = activeAnimation?.HasMovementData() ?? false;
            rootMotionCheckBox!.Enabled = hasRootMotion;
            rootMotionCheckBox.Checked = hasRootMotion;
            enableRootMotion = hasRootMotion;

            rootMotionResetPending = true;

            additiveCheckBox!.Enabled = activeAnimation is not null
                && (activeAnimation is not ClipAnimation || activeAnimation.IsAdditive);
            additiveCheckBox.Checked = animationController.ApplyAdditive;
        }

        protected override void LoadScene()
        {
            base.LoadScene();

            InitializeSoundPlayer();

            if (model != null)
            {
                modelSceneNode = new ModelSceneNode(Scene, model);

                if (ModelViewerWithAnimGraphSupport)
                {
                    graphSessions = [.. model.AnimGraph2References
                        .Where(static reference => !string.IsNullOrEmpty(reference.GraphPath))
                        .Select(static reference => new GraphSession(reference.Identifier, reference.GraphPath))];

                    // Play the first graph, the model's default
                    if (graphSessions.Length > 0 && LoadGraph(graphSessions[0]) is { } graph)
                    {
                        activeGraphSession = graphSessions[0];
                        animGraph = graph;
                        modelSceneNode.SetAnimationGraph(graph);
                    }
                }

                animationController = modelSceneNode.AnimationController;
                Scene.Add(modelSceneNode, true);

                if (modelSceneNode.RenderableMeshes.Count == 1)
                {
                    var mesh = modelSceneNode.RenderableMeshes[0];

                    // check if this is a static overlay world model
                    if (mesh.DrawCallsOverlay.Count > 0
                        && mesh.DrawCallsOpaque.Count == 0
                        && mesh.DrawCallsBlended.Count == 0)
                    {
                        foreach (var drawCall in mesh.DrawCallsOverlay)
                        {
                            drawCall.Material.IsOverlay = false; // render without trying to overlay on empty space
                        }
                    }
                }

                skeletonSceneNode = new SkeletonSceneNode(Scene, animationController.Pose, model.Skeleton, model.Attachments);
                Scene.Add(skeletonSceneNode, true);

                if (model.HitboxSets != null && model.HitboxSets.Count > 0)
                {
                    hitboxSetSceneNode = new HitboxSetSceneNode(Scene, animationController, model.HitboxSets);
                    Scene.Add(hitboxSetSceneNode, true);
                }

                modelParticleNodes = ParticleSceneNode.CreateModelParticles(Scene, model, modelSceneNode);
                foreach (var particleNode in modelParticleNodes)
                {
                    Scene.Add(particleNode, true);
                }

                phys = model.GetEmbeddedPhys();
                if (phys == null)
                {
                    var refPhysicsPaths = model.GetReferencedPhysNames().ToArray();
                    if (refPhysicsPaths.Length != 0)
                    {
                        //TODO are there any models with more than one vphys?
                        if (refPhysicsPaths.Length != 1)
                        {
                            Log.Debug(nameof(GLModelViewer), $"Model has more than 1 vphys ({refPhysicsPaths.Length})." +
                                " Please report this on https://github.com/ValveResourceFormat/ValveResourceFormat and provide the file that caused this.");
                        }

                        var newResource = Scene.RendererContext.FileLoader.LoadFileCompiled(refPhysicsPaths.First());
                        if (newResource != null && newResource.DataBlock is PhysAggregateData newPhys)
                        {
                            phys = newPhys;
                        }
                    }
                }
            }
            else
            {
                Picker?.OnPicked -= OnPicked;
            }

            if (phys != null)
            {
                if (phys.Parts.Length > 0)
                {
                    Renderer.EntitySystem.PhysicsWorld = new Rubikon(phys);

                    var isMapPhysics = Path.GetFileNameWithoutExtension(GuiContext.FileName)
                        .Equals("world_physics", StringComparison.OrdinalIgnoreCase);

                    Input.PlayerMovement.GridPlaneCollisionEnabled = !isMapPhysics;
                }

                var physSceneNodes = PhysSceneNode.CreatePhysSceneNodes(Scene, phys, null).ToList();

                // Physics are not shown by default unless the model has no meshes
                var enabledAllPhysByDefault = modelSceneNode == null || modelSceneNode.RenderableMeshes.Count == 0;

                foreach (var physSceneNode in physSceneNodes)
                {
                    physSceneNode.Enabled = enabledAllPhysByDefault;
                    physSceneNode.IsTranslucentRenderMode = false;
                    Scene.Add(physSceneNode, false);
                }
            }

            var post = new ScenePostProcessVolume(Scene)
            {
                HasBloom = true,
                IsMaster = true,
            };

            Scene.PostProcessInfo.AddPostProcessVolume(post);
        }

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);

            if (model != null)
            {
                Debug.Assert(modelSceneNode != null);

                Input.OrbitTargetProvider = () => modelSceneNode.BoundingBox.Center;

                var animations = modelSceneNode.Animations.Keys.ToArray();

                if (animations.Length > 0 || graphSessions.Length > 0)
                {
                    AddAnimationControls();
                    SetAvailableAnimations(animations);
                    SetAnimationControllerUpdateHandler();
                }

                if (graphSessions.Length > 0)
                {
                    if (activeGraphSession != null)
                    {
                        EnsureGraphControls(activeGraphSession);
                        graphGizmoBindings = activeGraphSession.GizmoBindings;
                    }

                    ShowGraphControls(activeGraphSession);
                }

                if (model.Skeleton.Bones.Length > 0)
                {
                    using var _ = UiControl.BeginGroup("Model");

                    showSkeletonCheckbox = UiControl.AddCheckBox("Show skeleton", false, isChecked =>
                    {
                        using var lockedGl = MakeCurrent();
                        skeletonSceneNode?.ShowBones = isChecked;
                    });
                }

                if (model.Attachments.Count > 0)
                {
                    using var _ = UiControl.BeginGroup("Model");

                    showAttachmentsCheckbox = UiControl.AddCheckBox("Show attachments", false, isChecked =>
                    {
                        attachmentList?.Visible = isChecked;

                        using var lockedGl = MakeCurrent();

                        skeletonSceneNode?.ShowAttachments = isChecked;
                    });

                    attachmentList = UiControl.AddMultiSelectionControl("Attachments", listBox =>
                    {
                        listBox.Items.AddRange([.. model.Attachments.Keys]);
                        for (var i = 0; i < listBox.Items.Count; i++)
                        {
                            listBox.SetItemChecked(i, true);
                        }

                        skeletonSceneNode?.SelectedAttachments.UnionWith(model.Attachments.Keys);
                    }, selectedAttachments =>
                    {
                        using var lockedGl = MakeCurrent();

                        if (skeletonSceneNode != null)
                        {
                            skeletonSceneNode.SelectedAttachments.Clear();
                            skeletonSceneNode.SelectedAttachments.UnionWith(selectedAttachments);
                        }
                    });
                    attachmentList.Visible = false;
                }

                if (modelParticleNodes.Count > 0)
                {
                    using var _ = UiControl.BeginGroup("Model");

                    showParticlesCheckbox = UiControl.AddCheckBox("Show particles", true, isChecked =>
                    {
                        using var lockedGl = MakeCurrent();

                        foreach (var particleNode in modelParticleNodes)
                        {
                            particleNode.LayerEnabled = isChecked;
                        }
                    });
                }

                if (model.HitboxSets != null && model.HitboxSets.Count > 0)
                {
                    Debug.Assert(hitboxSetSceneNode != null);

                    using var _ = UiControl.BeginGroup("Model");

                    var hitboxSets = model.HitboxSets;
                    hitboxComboBox = UiControl.AddSelection("Hitbox Set", (hitboxSet, i) =>
                    {
                        if (i == 0)
                        {
                            hitboxSetSceneNode.SetHitboxSet(null);
                        }
                        else
                        {
                            hitboxSetSceneNode.SetHitboxSet(hitboxSet);
                        }
                    });
                    hitboxComboBox.Items.Add("");
                    hitboxComboBox.Items.AddRange([.. hitboxSets.Keys]);
                }

                var lodInfo = model.LodInfo;
                var lodCount = lodInfo.LevelCount;

                if (lodInfo.HasDistinctLevels)
                {
                    hasSelectableLods = true;

                    using var _ = UiControl.BeginGroup("Model");

                    lodComboBox = UiControl.AddSelection("Level of Detail", (_, i) =>
                    {
                        if (i < 0)
                        {
                            return;
                        }

                        using var lockedGl = MakeCurrent();
                        // Index 0 is Auto; everything below it maps straight to a LoD level.
                        modelSceneNode?.SetOverrideLod(i == 0 ? null : i - 1);
                    });

                    lodComboBox.Items.Add("Auto");

                    for (var level = 0; level < lodCount; level++)
                    {
                        lodComboBox.Items.Add(FormatLodEntry(lodInfo, level));
                    }

                    lodComboBox.SelectedIndex = 0;
                }

                var meshGroups = modelSceneNode.GetMeshGroups().ToArray<object>();

                if (meshGroups.Length > 1)
                {
                    using var _ = UiControl.BeginGroup("Model");

                    meshGroupListBox = UiControl.AddMultiSelection("Mesh Group", listBox =>
                    {
                        listBox.Items.AddRange(meshGroups);

                        foreach (var group in modelSceneNode.GetActiveMeshGroups())
                        {
                            listBox.SetItemChecked(listBox.FindStringExact(group), true);
                        }
                    }, groups =>
                    {
                        using var lockedGl = MakeCurrent();
                        modelSceneNode.SetActiveMeshGroups(groups);
                        modelStatsDirty = true;
                    });
                }

                var materialGroupNames = model.GetMaterialGroups().Select(group => group.Name).ToArray<object>();

                if (materialGroupNames.Length > 1)
                {
                    using var _ = UiControl.BeginGroup("Model");

                    materialGroupListBox = UiControl.AddSelection("Material Group", (selectedGroup, _) =>
                    {
                        using var lockedGl = MakeCurrent();
                        modelSceneNode?.SetMaterialGroup(selectedGroup);
                        modelStatsDirty = true;
                    });

                    materialGroupListBox.Items.AddRange(materialGroupNames);
                    materialGroupListBox.SelectedIndex = 0;
                }
            }

            if (phys != null)
            {
                var physSceneNodes = Scene.AllNodes.OfType<PhysSceneNode>().ToList();

                // Physics are not shown by default unless the model has no meshes
                var enabledAllPhysByDefault = modelSceneNode == null || modelSceneNode.RenderableMeshes.Count == 0;

                var physicsGroups = physSceneNodes
                    .Select(r => r.PhysGroupName)
                    .Distinct()
                    .OrderByDescending(static s => s.StartsWith('-'))
                    .ThenBy(static s => s)
                    .ToArray();

                if (physicsGroups.Length > 0)
                {
                    physicsGroupsComboBox = UiControl.AddMultiSelection("Physics Groups", (listBox) =>
                    {
                        if (!enabledAllPhysByDefault)
                        {
                            listBox.Items.AddRange(physicsGroups);
                            return;
                        }

                        listBox.BeginUpdate();

                        foreach (var physGroup in physicsGroups)
                        {
                            listBox.Items.Add(physGroup, true);
                        }

                        listBox.EndUpdate();
                    }, (enabledPhysicsGroups) =>
                    {
                        SetEnabledPhysicsGroups(enabledPhysicsGroups.ToHashSet());
                    });
                }
            }

            base.AddUiControls();
        }

        /// <summary>One of the model's animation graphs, loaded and given controls the first time it is picked.</summary>
        private sealed class GraphSession(string identifier, string path)
        {
            public string DisplayName { get; } = string.IsNullOrEmpty(identifier) ? System.IO.Path.GetFileNameWithoutExtension(path) : identifier;
            public string Path { get; } = path;
            public AnimationGraph? Graph { get; set; }
            public bool LoadFailed { get; set; }
            public Panel? Controls { get; set; }
            public List<GraphGizmoBinding> GizmoBindings { get; } = [];
        }

        private AnimationGraph? LoadGraph(GraphSession session)
        {
            if (session.Graph != null || session.LoadFailed)
            {
                return session.Graph;
            }

            if (Scene.RendererContext.FileLoader.LoadFileCompiled(session.Path)?.DataBlock is NmGraphDefinition graphDefinition)
            {
                session.Graph = new AnimationGraph(graphDefinition, Scene.RendererContext.FileLoader);
            }
            else
            {
                session.LoadFailed = true;
                Log.Warn(nameof(GLModelViewer), $"Failed to load animation graph {session.Path}");
            }

            return session.Graph;
        }

        private void EnsureGraphControls(GraphSession session)
        {
            Debug.Assert(UiControl != null);
            Debug.Assert(session.Graph != null);

            if (session.Controls != null)
            {
                return;
            }

            Debug.Assert(graphControlsHost != null);

            var controls = CreateAnimGraphControls(session.Graph, session.GizmoBindings);
            controls.Width = graphControlsHost.ClientSize.Width;
            controls.Height = controls.PreferredSize.Height;
            controls.AutoSize = false;

            graphControlsHost.Controls.Add(controls);
            Themer.ThemeControl(controls);
            session.Controls = controls;
        }

        private void ShowGraphControls(GraphSession? session)
        {
            Debug.Assert(UiControl != null);

            // Every visibility change below relayouts the sidebar, so they are applied in one pass
            var sidebar = graphControlsHost?.Parent;
            sidebar?.SuspendLayout();

            var showGraph = session?.Controls != null;

            if (graphControlsHost != null)
            {
                session?.Controls?.BringToFront();
                graphControlsHost.Height = session?.Controls?.Height ?? 0;
            }

            // The frame based controls only apply to clips, pausing and speed apply to graphs too
            animationTimeLabel?.Visible = !showGraph;
            animationTrackBar?.Visible = !showGraph;
            rootMotionCheckBox?.Parent?.Visible = !showGraph;
            additiveCheckBox?.Parent?.Visible = !showGraph;

            if (showGraph)
            {
                animationPlayPause?.Enabled = true;
                slowmodeTrackBar?.Enabled = true;
            }

            sidebar?.ResumeLayout();
        }

        private const string UnsetIdText = "(none)";

        private static (Control Editor, Control[] Trailing) CreateFloatParameterEditor(AnimationGraph graph, string paramName)
        {
            var value = graph.FloatParameters[paramName];
            void SetValue(float newValue) => graph.FloatParameters[paramName] = newValue;

            var range = graph.GetFloatParameterRange(paramName) ?? GuessFloatParameterRange(paramName);

            if (range is not { } knownRange)
            {
                return (RendererControl.CreateFloatField(value, 3, SetValue), []);
            }

            if (knownRange.IsDiscrete)
            {
                var options = Enumerable.Range((int)knownRange.Min, (int)(knownRange.Max - knownRange.Min) + 1)
                    .Select(static option => option.ToString(CultureInfo.InvariantCulture));

                return (RendererControl.CreateRowComboBox(options, ((int)value).ToString(CultureInfo.InvariantCulture),
                    option => SetValue(float.Parse(option, CultureInfo.InvariantCulture))), []);
            }

            var (slider, field) = RendererControl.CreateRangedFloatEditor(value, knownRange.Min, knownRange.Max, knownRange.IsWholeNumber, SetValue);
            return (slider, [field]);
        }

        // For parameters only read by code the graph does not describe, such as aim nodes
        private static FloatParameterRange? GuessFloatParameterRange(string paramName)
        {
            if (paramName.Contains("angle", StringComparison.OrdinalIgnoreCase))
            {
                return new FloatParameterRange(-180f, 180f, IsWholeNumber: false, IsDiscrete: false);
            }

            if (paramName.Contains("amount", StringComparison.OrdinalIgnoreCase))
            {
                return new FloatParameterRange(0f, 1f, IsWholeNumber: false, IsDiscrete: false);
            }

            return null;
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Each row adds its editors to its Controls, which dispose them")]
        private static Panel CreateAnimGraphControls(AnimationGraph graph, List<GraphGizmoBinding> gizmoBindings)
        {
            var panel = new Panel
            {
                AutoSize = true,
                Margin = Padding.Empty,
            };

            panel.SuspendLayout();

            void Add(Control control)
            {
                control.Dock = DockStyle.Top;
                panel.Controls.Add(control);
                control.BringToFront();
            }

            void AddSection(string title, int count)
            {
                if (count > 0)
                {
                    Add(RendererControl.CreateSectionHeader(title));
                }
            }

            static IOrderedEnumerable<string> Sorted<T>(Dictionary<string, T> parameters)
                => parameters.Keys.Order(StringComparer.OrdinalIgnoreCase);

            AddSection("Flags", graph.BoolParameters.Count);

            foreach (var paramName in Sorted(graph.BoolParameters))
            {
                Add(RendererControl.CreateCheckBoxRow(
                    RendererControl.CreateRowCheckBox(graph.BoolParameters[paramName], isChecked => graph.BoolParameters[paramName] = isChecked, paramName),
                    RendererControl.CreateRowButton("Signal", () => graph.SignalBoolParameter(paramName))));
            }

            AddSection("Values", graph.FloatParameters.Count);

            foreach (var paramName in Sorted(graph.FloatParameters))
            {
                var (editor, trailing) = CreateFloatParameterEditor(graph, paramName);
                Add(RendererControl.CreatePropertyRow(paramName, editor, trailing));
            }

            AddSection("Identifiers", graph.IdParameters.Count);

            foreach (var paramName in Sorted(graph.IdParameters))
            {
                // An empty ID is unset, which is how the game starts every ID parameter
                var options = graph.GetParameterIdOptions(paramName).Order(StringComparer.Ordinal).Prepend(UnsetIdText);
                var value = graph.IdParameters[paramName];

                Add(RendererControl.CreatePropertyRow(paramName,
                    RendererControl.CreateRowComboBox(options, value.Length == 0 ? UnsetIdText : value, id =>
                    {
                        graph.IdParameters[paramName] = id == UnsetIdText ? string.Empty : id;
                    })));
            }

            AddSection("Vectors", graph.VectorParameters.Count);

            foreach (var paramName in Sorted(graph.VectorParameters))
            {
                var binding = new GraphGizmoBinding(paramName, isTarget: false);
                var value = graph.VectorParameters[paramName];

                // Only look-at targets report a hint, so only they show a gizmo
                Add(RendererControl.CreatePropertyRow(paramName,
                    RendererControl.CreateRowCheckBox(binding.Gizmo.Visible, visible => binding.Gizmo.Visible = visible, "Gizmo"),
                    RendererControl.CreateRowButton("Reset", () => binding.ResetPending = true)));

                var (row, fields) = RendererControl.CreateVectorRow(new Vector3(value.X, value.Y, value.Z), vector =>
                {
                    graph.VectorParameters[paramName] = new Vector4(vector, 0f);
                    binding.Initialized = true;
                });

                binding.Fields = fields;
                Add(row);
                gizmoBindings.Add(binding);
            }

            AddSection("Targets", graph.TargetParameters.Count);

            foreach (var paramName in Sorted(graph.TargetParameters))
            {
                var binding = new GraphGizmoBinding(paramName, isTarget: true);

                // An unset target leaves the IK off and the gizmo on the animated bone
                Add(RendererControl.CreatePropertyRow(paramName,
                    RendererControl.CreateRowCheckBox(binding.Gizmo.Visible, visible => binding.Gizmo.Visible = visible, "Gizmo"),
                    RendererControl.CreateRowButton("Reset", () => graph.TargetParameters[paramName] = null)));

                gizmoBindings.Add(binding);
            }

            panel.ResumeLayout(false);
            return panel;
        }

        private sealed class GraphGizmoBinding(string parameterName, bool isTarget)
        {
            public string ParameterName { get; } = parameterName;
            public bool IsTarget { get; } = isTarget;
            public TransformGizmos.Gizmo Gizmo { get; } = new(parameterName, allowRotation: isTarget);
            public ThemedFloatNumeric[]? Fields { get; set; }
            public bool IsWorldSpace { get; set; }
            public volatile bool Initialized;
            public volatile bool ResetPending;
        }

        // Swapped with the active graph under the GL lock, synced into the gizmos on the render thread
        private List<GraphGizmoBinding> graphGizmoBindings = [];
        private List<GraphGizmoBinding>? shownGizmoBindings;
        private TransformGizmos? graphGizmos;

        // Written on the UI thread, read by the render loop
        private long gizmoMousePosition;
        private volatile bool gizmoPressPending;
        private volatile bool gizmoDragging;

        protected override void OnMouseMove(int x, int y)
        {
            Interlocked.Exchange(ref gizmoMousePosition, ((long)x << 32) | (uint)y);
            base.OnMouseMove(x, y);
        }

        protected override void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && graphGizmos?.HoveredGizmo != null)
            {
                Interlocked.Exchange(ref gizmoMousePosition, ((long)e.X << 32) | (uint)e.Y);
                gizmoPressPending = true;
                gizmoDragging = true;
                GLControl?.Focus();
                return;
            }

            base.OnMouseDown(sender, e);
        }

        protected override void OnMouseUp(object? sender, MouseEventArgs e)
        {
            if (gizmoDragging && e.Button == MouseButtons.Left)
            {
                gizmoDragging = false;
                return;
            }

            base.OnMouseUp(sender, e);
        }

        protected override void RenderOverlayLines(Scene.RenderContext renderContext)
        {
            if (animGraph == null || modelSceneNode == null || graphGizmoBindings.Count == 0)
            {
                return;
            }

            graphGizmos ??= new TransformGizmos(Scene.RendererContext);

            if (shownGizmoBindings != graphGizmoBindings)
            {
                shownGizmoBindings = graphGizmoBindings;
                graphGizmos.EndDrag();
                graphGizmos.Gizmos.Clear();
                graphGizmos.Gizmos.AddRange(graphGizmoBindings.Select(static binding => binding.Gizmo));
            }

            var camera = renderContext.Camera;
            var packedMouse = Interlocked.Read(ref gizmoMousePosition);
            var mouse = new Vector2((int)(packedMouse >> 32), (int)(packedMouse & 0xFFFFFFFF));

            var nodeTransform = modelSceneNode.Transform;
            if (!Matrix4x4.Invert(nodeTransform, out var nodeInverse))
            {
                return;
            }

            foreach (var binding in graphGizmoBindings)
            {
                if (binding.Gizmo != graphGizmos.ActiveGizmo)
                {
                    ReadGizmoFromParameter(binding, nodeTransform);
                }
            }

            if (gizmoPressPending)
            {
                gizmoPressPending = false;
                graphGizmos.BeginDrag(camera, mouse);
            }

            if (gizmoDragging && graphGizmos.ActiveGizmo is { } activeGizmo)
            {
                graphGizmos.Drag(camera, mouse);

                var binding = graphGizmoBindings.First(binding => binding.Gizmo == activeGizmo);
                WriteParameterFromGizmo(binding, nodeInverse);
            }
            else
            {
                if (graphGizmos.ActiveGizmo is { } releasedGizmo)
                {
                    graphGizmos.EndDrag();
                    RefreshVectorFields(graphGizmoBindings.First(binding => binding.Gizmo == releasedGizmo));
                }

                var cameraDragging = (CurrentlyPressedKeys & TrackedKeys.MouseLeftOrRight) != 0;
                if (MouseOverRenderArea && !cameraDragging)
                {
                    graphGizmos.UpdateHover(camera, mouse);
                }
                else
                {
                    graphGizmos.UpdateHover(camera, new Vector2(float.MinValue));
                }
            }

            graphGizmos.Render(camera);
        }

        private void ReadGizmoFromParameter(GraphGizmoBinding binding, Matrix4x4 nodeTransform)
        {
            Debug.Assert(animGraph != null);

            var gizmo = binding.Gizmo;
            var hasHint = animGraph.ParameterHints.TryGetValue(binding.ParameterName, out var hint);
            binding.IsWorldSpace = hasHint && hint.IsWorldSpace;

            FrameBone parameterValue;

            if (binding.IsTarget)
            {
                if (animGraph.TargetParameters.GetValueOrDefault(binding.ParameterName) is { } target)
                {
                    parameterValue = target;
                }
                else if (hasHint)
                {
                    parameterValue = hint.Transform;
                }
                else
                {
                    gizmo.HasValue = false;
                    return;
                }
            }
            else
            {
                if (!hasHint)
                {
                    gizmo.HasValue = false;
                    return;
                }

                // Look-at targets start ahead of the head rather than at the origin
                if (binding.ResetPending || !binding.Initialized)
                {
                    binding.ResetPending = false;
                    binding.Initialized = true;
                    animGraph.VectorParameters[binding.ParameterName] = new Vector4(hint.Transform.Position, 0f);
                    RefreshVectorFields(binding);
                }

                var vector = animGraph.VectorParameters[binding.ParameterName];
                parameterValue = new FrameBone(new Vector3(vector.X, vector.Y, vector.Z), 1f, Quaternion.Identity);
            }

            var characterValue = binding.IsWorldSpace ? parameterValue * animGraph.WorldTransform.Inverse() : parameterValue;

            if (!Matrix4x4.Decompose(characterValue.ToMatrix() * nodeTransform, out _, out var rotation, out var position))
            {
                gizmo.HasValue = false;
                return;
            }

            gizmo.Position = position;
            gizmo.Rotation = rotation;
            gizmo.HasValue = true;
        }

        private void WriteParameterFromGizmo(GraphGizmoBinding binding, Matrix4x4 nodeInverse)
        {
            Debug.Assert(animGraph != null);

            var gizmo = binding.Gizmo;
            var scene = Matrix4x4.CreateFromQuaternion(gizmo.Rotation) * Matrix4x4.CreateTranslation(gizmo.Position);

            if (!Matrix4x4.Decompose(scene * nodeInverse, out _, out var rotation, out var position))
            {
                return;
            }

            var characterValue = new FrameBone(position, 1f, rotation);
            var parameterValue = binding.IsWorldSpace ? characterValue * animGraph.WorldTransform : characterValue;

            if (binding.IsTarget)
            {
                animGraph.TargetParameters[binding.ParameterName] = parameterValue;
            }
            else
            {
                animGraph.VectorParameters[binding.ParameterName] = new Vector4(parameterValue.Position, 0f);
            }
        }

        private void RefreshVectorFields(GraphGizmoBinding binding)
        {
            if (animGraph == null || binding.Fields is not { } fields || binding.IsTarget)
            {
                return;
            }

            var vector = animGraph.VectorParameters[binding.ParameterName];
            fields[0].BeginInvoke(() =>
            {
                fields[0].Value = vector.X;
                fields[1].Value = vector.Y;
                fields[2].Value = vector.Z;
            });
        }

        protected void SetAnimationControllerUpdateHandler()
        {
            Debug.Assert(animationController != null);
            Debug.Assert(animationTrackBar != null);
            Debug.Assert(animationPlayPause != null);
            Debug.Assert(slowmodeTrackBar != null);
            Debug.Assert(animationTimeLabel != null);

            void UiAnimationHandler(Animation? animation, int frame)
            {
                if (frame == -1)
                {
                    var maximum = animation == null ? 1 : animation.FrameCount - 1;
                    if (maximum < 0)
                    {
                        maximum = 0;
                    }

                    var playing = animation != null || animGraph != null;
                    animationTrackBar.Enabled = animation != null;
                    animationPlayPause.Enabled = playing;
                    slowmodeTrackBar.Enabled = playing;

                    frame = 0;
                }
                else if (animation is { CycleFrames: > 0 } && animationPlayPause.Checked
                    && (int)MathF.Round(animationTrackBar.Slider.Value * animation.CycleFrames) != frame)
                {
                    animationTrackBar.Slider.Value = (float)frame / animation.CycleFrames;
                }

                if (animationController.ActiveAnimation == null)
                {
                    animationTimeLabel.Text = string.Empty;
                    return;
                }

                var activeAnimation = animationController.ActiveAnimation;
                var frameCount = activeAnimation.FrameCount;
                var fps = activeAnimation.Fps;
                var totalTime = activeAnimation.Duration;
                var (cycle, _, _) = activeAnimation.GetCyclePosition(animationController.Time);
                var time = animationController.Time - cycle * totalTime;
                var frameNumber = animationController.Frame + 1;

                animationTimeLabel.Text = $"Frame: {frameNumber,4} / {frameCount}\n" +
                    $"Time: {time:F2} / {totalTime:F2}\n" +
                    $"FPS: {fps:F2}\n";
            }

            void UpdateUiAnimationState(Animation? animation, int frame)
            {
                if (animationTrackBar.InvokeRequired)
                {
                    animationTrackBar.BeginInvoke(() => UiAnimationHandler(animation, frame));
                }
                else
                {
                    UiAnimationHandler(animation, frame);
                }
            }
            animationController.RegisterUpdateHandler(UpdateUiAnimationState);
        }

        private string GetModelStatsText()
        {
            Debug.Assert(modelSceneNode != null);

            var sb = new System.Text.StringBuilder();

            if (hasSelectableLods)
            {
                sb.AppendLine(GetActiveLodText());
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"Mesh Count: {modelSceneNode.RenderableMeshes.Count}");

            foreach (var mesh in modelSceneNode.RenderableMeshes)
            {
                var meshName = mesh.Name.Split(":")[^1];
                var size = mesh.BoundingBox.Max - mesh.BoundingBox.Min;

                var vertexTotal = 0;
                var triangleTotal = 0;
                var vertexBufferSize = 0;
                var indexBufferSize = 0;

                var coloredMaterialNames = new List<string>();

                void AddColoredMaterialName(DrawCall call)
                {
                    var tintHex = Color32.FromVector4(call.TintColor).HexCode;
                    coloredMaterialNames.Add($"\\{tintHex}{Path.GetFileNameWithoutExtension(call.Material.Material.Name)}");
                }

                foreach (var draw in mesh.DrawCalls)
                {
                    AddColoredMaterialName(draw);
                    vertexTotal += (int)draw.VertexCount;
                    triangleTotal += draw.IndexCount / 3;
                    vertexBufferSize += (int)(draw.VertexCount * draw.VertexBuffers.Sum(vb => vb.ElementSizeInBytes));
                    indexBufferSize += draw.IndexCount * draw.IndexSizeInBytes;
                }

                var moreThanSixEllipsis = coloredMaterialNames.Count > 6 ? "..." : string.Empty;
                var allColoredMaterials = string.Join("\\#FFFFFFFF, ", coloredMaterialNames.Take(6)) + "\\#FFFFFFFF" + moreThanSixEllipsis;

                sb.Append(CultureInfo.InvariantCulture,
                    $"""

                    Mesh '{meshName}':
                        Vertices  : {vertexTotal:N0} | {HumanReadableByteSizeFormatter.Format(vertexBufferSize)}
                        Triangles : {triangleTotal:N0} | {HumanReadableByteSizeFormatter.Format(indexBufferSize)}

                    """
                );

                if (mesh.Meshlets.Count > 0)
                {
                    var trianglesPerMeshlet = mesh.Meshlets[0].TriangleCount == 0
                        ? (uint)triangleTotal / mesh.Meshlets.Count
                        : mesh.Meshlets[0].TriangleCount;
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    Meshlets  : {mesh.Meshlets.Count:N0} | {trianglesPerMeshlet:N0} triangles each");
                }

                if (mesh.MeshBoneCount > 0)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    Skinning  : {mesh.MeshBoneCount} bones, {mesh.BoneWeightCount} per vertex");
                }

                sb.AppendLine(CultureInfo.InvariantCulture, $"    Drawcalls : {coloredMaterialNames.Count} ({allColoredMaterials})");
                sb.AppendLine(CultureInfo.InvariantCulture, $"    Size      : X: {size.X:0.##} | Y: {size.Y:0.##} | Z: {size.Z:0.##}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private void SetEnabledPhysicsGroups(HashSet<string> physicsGroups)
        {
            foreach (var physNode in Scene.AllNodes.OfType<PhysSceneNode>())
            {
                physNode.Enabled = physicsGroups.Contains(physNode.PhysGroupName);
            }

            using var lockedGl = MakeCurrent();

            foreach (var scene in Renderer.Scenes)
            {
                scene.UpdateOctrees();
            }
        }

        private Matrix4x4 rootMotionBase = Matrix4x4.Identity;
        private Matrix4x4 rootMotionTotal = Matrix4x4.Identity;

        private Vector3 rootMotionCameraOffset;

        private bool rootMotionLatched;
        private bool rootMotionResetPending;
        private bool enableRootMotion;

        /// <summary>
        /// Builds a dropdown label for one LoD level: "LOD n (Empty)" if the level has no meshes,
        /// otherwise "LOD n" plus the range it's active over, like "LOD 2 (10-15)" or "LOD 4 (20+)".
        /// The range is omitted when the model has no switch data.
        /// </summary>
        private static string FormatLodEntry(ModelLodInfo lodInfo, int level)
        {
            if (!lodInfo.AvailableLevels.Contains(level))
            {
                return $"LOD {level} (Empty)";
            }

            if (lodInfo.SwitchDistances.Count <= 1 || level >= lodInfo.SwitchDistances.Count)
            {
                return $"LOD {level}";
            }

            var (min, max) = lodInfo.GetMetricRange(level);
            var minText = min.ToString("0.#", CultureInfo.InvariantCulture);

            return max is float upper
                ? $"LOD {level} ({minText}-{upper.ToString("0.#", CultureInfo.InvariantCulture)})"
                : $"LOD {level} ({minText}+)";
        }

        /// <summary>
        /// Walks the model and the camera along the root motion the player advanced through since the last
        /// frame. The player unrolls looping, and reports no motion when playback did not advance.
        /// </summary>
        private void UpdateRootMotion()
        {
            if (!enableRootMotion || animationController == null)
            {
                return;
            }

            var delta = animationController.ConsumeRootMotionDelta();

            if (delta.IsIdentity)
            {
                return;
            }

            if (!rootMotionLatched)
            {
                rootMotionBase = modelSceneNode?.Transform ?? skeletonSceneNode?.Transform ?? Matrix4x4.Identity;
                rootMotionTotal = Matrix4x4.Identity;
                rootMotionCameraOffset = Vector3.Zero;
                rootMotionLatched = true;
            }

            var previousTranslation = (rootMotionTotal * rootMotionBase).Translation;

            rootMotionTotal *= delta;

            var transform = rootMotionTotal * rootMotionBase;

            MoveRootMotionCamera(transform.Translation - previousTranslation);
            SetRootMotionTransform(transform);
        }

        /// <summary>
        /// Returns the model and the camera to where root motion picked them up.
        /// </summary>
        private void ResetRootMotion()
        {
            if (rootMotionLatched)
            {
                SetRootMotionTransform(rootMotionBase);
                MoveRootMotionCamera(-rootMotionCameraOffset);
            }

            // Discard motion banked up while nothing was consuming it.
            animationController?.ConsumeRootMotionDelta();

            rootMotionTotal = Matrix4x4.Identity;
            rootMotionCameraOffset = Vector3.Zero;
            rootMotionLatched = false;
        }

        /// <summary>
        /// Places the animated nodes at <paramref name="transform"/>, keeping the dynamic octree in step.
        /// </summary>
        private void SetRootMotionTransform(Matrix4x4 transform)
        {
            static void Move(SceneNode? node, Matrix4x4 transform)
            {
                if (node == null)
                {
                    return;
                }

                node.Transform = transform;
                node.Scene.DynamicOctree.Update(node);
            }

            Move(modelSceneNode, transform);
            Move(skeletonSceneNode, transform);
        }

        /// <summary>
        /// Carries the camera along with the model, keeping any orbit anchor pinned to it.
        /// </summary>
        private void MoveRootMotionCamera(Vector3 delta)
        {
            Input.Camera.Location += delta;

            if (Input.OrbitTarget is Vector3 orbitTarget)
            {
                Input.OrbitTarget = orbitTarget + delta;
            }

            // The input tick is skipped while the cursor is over the side panel, and it is what commits the camera.
            Input.ForceUpdate = true;

            rootMotionCameraOffset += delta;
        }

        /// <summary>
        /// Placed ahead of the input tick, which commits the camera. Any later and the model would move this
        /// frame but the camera only on the next one.
        /// </summary>
        protected override void OnUpdate(float frameTime)
        {
            if (rootMotionResetPending)
            {
                rootMotionResetPending = false;
                ResetRootMotion();
            }

            UpdateRootMotion();

            base.OnUpdate(frameTime);
        }

        protected override void OnPaint(float frameTime)
        {
            // The stats overlay reflects whatever meshes are currently drawn, so it only needs rebuilding
            // when that set changes (a LoD switch, or a mesh/material group change), not every frame.
            if (modelSceneNode != null && SelectedNodeRenderer != null)
            {
                if (!SelectedNodeRenderer.HasSelectedNodes)
                {
                    if (modelStatsPosted)
                    {
                        SelectedNodeRenderer.ScreenDebugText = string.Empty;
                        modelStatsPosted = false;
                        modelStatsDirty = true;
                    }
                }
                else
                {
                    if (modelSceneNode.ActiveLod != statsLod)
                    {
                        statsLod = modelSceneNode.ActiveLod;
                        modelStatsDirty = true;
                    }

                    if (modelStatsDirty)
                    {
                        SelectedNodeRenderer.ScreenDebugText = GetModelStatsText();
                        modelStatsDirty = false;
                        modelStatsPosted = true;
                    }
                }
            }

            // Always show the active level in the corner. Skip it while paused, where the corner is
            // taken over by the "Paused" text.
            if (hasSelectableLods && modelSceneNode != null && !Paused)
            {
                DrawLowerCornerText(GetActiveLodText(), Color32.White, lineFromBottom: 1);
            }

            base.OnPaint(frameTime);
        }

        /// <summary>Active level as overlay text: "LOD: Auto (2)" while auto-selecting, "LOD: 2" when forced.</summary>
        private string GetActiveLodText()
        {
            Debug.Assert(modelSceneNode != null);

            return modelSceneNode.IsAutoLod
                ? $"LOD: Auto ({modelSceneNode.ActiveLod})"
                : $"LOD: {modelSceneNode.ActiveLod}";
        }

        protected override void OnPicked(object? sender, PickingTexture.PickingResponse pickingResponse)
        {
            if (modelSceneNode == null)
            {
                return;
            }

            Debug.Assert(SelectedNodeRenderer != null);

            // Void
            if (pickingResponse.PixelInfo.ObjectId == 0)
            {
                SelectedNodeRenderer.SelectNode(null);
                return;
            }

            if (pickingResponse.Intent == PickingTexture.PickingIntent.Select)
            {
                var sceneNode = Scene.Find(pickingResponse.PixelInfo.ObjectId);
                SelectedNodeRenderer.SelectNode(sceneNode);
                modelStatsDirty = true;
                return;
            }

            if (pickingResponse.Intent == PickingTexture.PickingIntent.Open)
            {
                var refMesh = modelSceneNode.GetReferenceMeshes().FirstOrDefault(x => x.MeshIndex == pickingResponse.PixelInfo.MeshId);
                if (refMesh.MeshName != null)
                {
                    var foundFile = GuiContext.FindFileWithContext(refMesh.MeshName + GameFileLoader.CompiledFileSuffix);
                    if (foundFile.Context != null)
                    {
                        foundFile.Context.GLPostLoadAction = (viewerControl) =>
                        {
                            if (viewerControl is GLSceneViewer sceneViewer)
                            {
                                sceneViewer.Input.Camera.CopyFrom(Renderer.Camera);
                            }
                        };

                        Program.MainForm.OpenFile(foundFile.Context, foundFile.PackageEntry);
                    }
                }
            }
        }

        private void SetAvailableAnimations(string[] animations)
        {
            Debug.Assert(animationComboBox != null);

            animationIndexMap.Clear();

            animationComboBox.BeginUpdate();
            animationComboBox.Items.Clear();

            if (animations.Length > 0 || graphSessions.Length > 0)
            {
                animationComboBox.Enabled = true;
                animationComboBox.Items.Add(animations.Length > 0 ? $"({animations.Length} animations available)" : "(bind pose)");
                animationIndexMap.Add(default);

                var selectedIndex = 0;

                if (graphSessions.Length > 0)
                {
                    animationComboBox.Items.Add(new ThemedComboBoxItem
                    {
                        Text = "Animation Graphs",
                        IsHeader = true
                    });
                    animationIndexMap.Add(default);

                    for (var i = 0; i < graphSessions.Length; i++)
                    {
                        if (graphSessions[i] == activeGraphSession)
                        {
                            selectedIndex = animationComboBox.Items.Count;
                        }

                        animationComboBox.Items.Add(new ThemedComboBoxItem
                        {
                            Text = graphSessions[i].DisplayName,
                            IsHeader = false
                        });
                        animationIndexMap.Add(new AnimationListEntry(null, i));
                    }
                }

                var animationToFolder = model?.SequenceGroup.GetFaceposerFolders() ?? [];

                // Add ag2 folders
                foreach (var anim in animations)
                {
                    if (!animationToFolder.ContainsKey(anim))
                    {
                        animationToFolder[anim] = (Path.GetDirectoryName(anim) ?? string.Empty).Replace('\\', '/');
                    }
                }

                if (animationToFolder.Count > 0)
                {
                    var folderGroups = animations
                        .GroupBy(anim => animationToFolder.GetValueOrDefault(anim, string.Empty))
                        .ToList();

                    var groupedFolders = folderGroups
                        .Where(g => !string.IsNullOrEmpty(g.Key))
                        .OrderBy(g => g.Key);

                    var ungroupedAnimations = folderGroups
                        .Where(g => string.IsNullOrEmpty(g.Key))
                        .SelectMany(g => g)
                        .OrderBy(a => a)
                        .ToList();

                    foreach (var folderGroup in groupedFolders)
                    {
                        animationComboBox.Items.Add(new ThemedComboBoxItem
                        {
                            Text = folderGroup.Key,
                            IsHeader = true
                        });
                        animationIndexMap.Add(default);

                        foreach (var anim in folderGroup.OrderBy(a => a))
                        {
                            var displayName = Path.GetFileNameWithoutExtension(anim);
                            animationComboBox.Items.Add(new ThemedComboBoxItem
                            {
                                Text = displayName,
                                IsHeader = false
                            });
                            animationIndexMap.Add(new AnimationListEntry(anim));
                        }
                    }

                    if (ungroupedAnimations.Count > 0)
                    {
                        animationComboBox.Items.Add(new ThemedComboBoxItem
                        {
                            Text = "Ungrouped",
                            IsHeader = true
                        });
                        animationIndexMap.Add(default);

                        foreach (var anim in ungroupedAnimations)
                        {
                            var displayName = Path.GetFileNameWithoutExtension(anim);
                            animationComboBox.Items.Add(new ThemedComboBoxItem
                            {
                                Text = displayName,
                                IsHeader = false
                            });
                            animationIndexMap.Add(new AnimationListEntry(anim));
                        }
                    }
                }
                else
                {
                    animationComboBox.Items.AddRange(animations);
                    animationIndexMap.AddRange(animations.Select(static anim => new AnimationListEntry(anim)));
                }

                animationComboBoxCurrentIndex = -10;
                animationComboBox.SelectedIndex = selectedIndex;
            }
            else
            {
                animationComboBox.Items.Add("(no animations available)");
                animationComboBox.SelectedIndex = 0;
                animationComboBox.Enabled = false;
            }

            animationComboBox.EndUpdate();
        }
    }
}
