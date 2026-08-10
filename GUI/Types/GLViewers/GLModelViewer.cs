using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace GUI.Types.GLViewers
{
    class GLModelViewer : GLSingleNodeViewer
    {
        protected Model? model { get; init; }
        private PhysAggregateData? phys;

        private readonly List<string?> animationIndexMap = [];

        public ComboBox? animationComboBox { get; protected set; }
        protected CheckBox? animationPlayPause;
        private CheckBox? rootMotionCheckBox;
        private CheckBox? additiveCheckBox;
        private CheckBox? showSkeletonCheckbox;
        private CheckBox? showParticlesCheckbox;
        private ComboBox? hitboxComboBox;
        private Label? animationTimeLabel;
        private GLViewerSliderControl? animationTrackBar;
        private GLViewerSliderControl? slowmodeTrackBar;
        public CheckedListBox? meshGroupListBox { get; private set; }
        public ComboBox? materialGroupListBox { get; private set; }
        private ComboBox? lodComboBox;
        private bool hasSelectableLods;
        private bool modelStatsShown;
        private bool modelStatsDirty;
        private int statsLod = -1;
        private ModelSceneNode? modelSceneNode;
        protected AnimationController? animationController;
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
        }

        public GLModelViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, PhysAggregateData phys) : base(vrfGuiContext, rendererContext)
        {
            this.phys = phys;
        }

        public override void Dispose()
        {
            base.Dispose();

            animationComboBox?.Dispose();
            animationPlayPause?.Dispose();
            animationTimeLabel?.Dispose();
            animationTrackBar?.Dispose();
            slowmodeTrackBar?.Dispose();
            meshGroupListBox?.Dispose();
            materialGroupListBox?.Dispose();
            lodComboBox?.Dispose();
            physicsGroupsComboBox?.Dispose();
            rootMotionCheckBox?.Dispose();
            additiveCheckBox?.Dispose();
            showSkeletonCheckbox?.Dispose();
            showParticlesCheckbox?.Dispose();
            hitboxComboBox?.Dispose();
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

                // Check if this is a header item (ThemedComboBoxItem with IsHeader = true)
                if (animationComboBox!.Items[i] is ThemedComboBoxItem item && item.IsHeader)
                {
                    // Skip header selection and jump to adjacent non-header item
                    animationComboBox.SelectedIndex = animationComboBoxCurrentIndex > i ? i - 1 : i + 1;
                    return;
                }

                animationComboBoxCurrentIndex = i;
                Debug.Assert(modelSceneNode != null);
                using (var lockedGL = MakeCurrent())
                {
                    if (animationIndexMap.Count > i &&
                        animationIndexMap[i] is string animationId)
                    {
                        modelSceneNode.SetAnimationByName(animationId);
                    }
                    else
                    {
                        modelSceneNode.SetAnimation(null);
                    }
                }

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

                skeletonSceneNode = new SkeletonSceneNode(Scene, animationController.Pose, model.Skeleton);
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
                    Scene.PhysicsWorld = new Rubikon(phys);

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

                if (animations.Length > 0)
                {
                    AddAnimationControls();
                    SetAvailableAnimations(animations);
                    SetAnimationControllerUpdateHandler();
                }

                if (model.Skeleton.Bones.Length > 0)
                {
                    using var _ = UiControl.BeginGroup("Model");

                    showSkeletonCheckbox = UiControl.AddCheckBox("Show skeleton", false, isChecked =>
                    {
                        using var lockedGl = MakeCurrent();
                        skeletonSceneNode?.Enabled = isChecked;
                    });
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

                    animationTrackBar.Enabled = animation != null;
                    animationPlayPause.Enabled = animation != null;
                    slowmodeTrackBar.Enabled = animation != null;

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

            Scene.UpdateOctrees();
            SkyboxScene?.UpdateOctrees();
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

                var oldBounds = node.BoundingBox;
                node.Transform = transform;

                if (node.LayerEnabled)
                {
                    node.Scene.DynamicOctree.Update(node, oldBounds);
                }
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
            if (modelStatsShown && modelSceneNode != null && SelectedNodeRenderer != null)
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
                SelectedNodeRenderer.ScreenDebugText = string.Empty;
                modelStatsShown = false;
                return;
            }

            if (pickingResponse.Intent == PickingTexture.PickingIntent.Select)
            {
                var sceneNode = Scene.Find(pickingResponse.PixelInfo.ObjectId);
                SelectedNodeRenderer.SelectNode(sceneNode);
                modelStatsShown = true;
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

            if (animations.Length > 0)
            {
                animationComboBox.Enabled = true;
                animationComboBox.Items.Add($"({animations.Length} animations available)");
                animationIndexMap.Add(null);

                var animationToFolder = model?.GetFaceposerFolders() ?? [];

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
                        animationIndexMap.Add(null);

                        foreach (var anim in folderGroup.OrderBy(a => a))
                        {
                            var displayName = Path.GetFileNameWithoutExtension(anim);
                            animationComboBox.Items.Add(new ThemedComboBoxItem
                            {
                                Text = displayName,
                                IsHeader = false
                            });
                            animationIndexMap.Add(anim);
                        }
                    }

                    if (ungroupedAnimations.Count > 0)
                    {
                        animationComboBox.Items.Add(new ThemedComboBoxItem
                        {
                            Text = "Ungrouped",
                            IsHeader = true
                        });
                        animationIndexMap.Add(null);

                        foreach (var anim in ungroupedAnimations)
                        {
                            var displayName = Path.GetFileNameWithoutExtension(anim);
                            animationComboBox.Items.Add(new ThemedComboBoxItem
                            {
                                Text = displayName,
                                IsHeader = false
                            });
                            animationIndexMap.Add(anim);
                        }
                    }
                }
                else
                {
                    animationComboBox.Items.AddRange(animations);
                    animationIndexMap.AddRange(animations);
                }

                animationComboBoxCurrentIndex = -10;
                animationComboBox.SelectedIndex = 0;
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
