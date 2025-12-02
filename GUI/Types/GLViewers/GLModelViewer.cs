using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Renderer;
using GUI.Utils;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

#nullable disable

namespace GUI.Types.GLViewers
{
    class GLModelViewer : GLSingleNodeViewer
    {
        protected Model model { get; init; }
        private PhysAggregateData phys;
        public ComboBox animationComboBox { get; protected set; }
        protected CheckBox animationPlayPause;
        private CheckBox rootMotionCheckBox;
        private CheckBox showSkeletonCheckbox;
        private ComboBox hitboxComboBox;
        private Label animationTimeLabel;
        private GLViewerTrackBarControl animationTrackBar;
        private GLViewerTrackBarControl slowmodeTrackBar;
        public CheckedListBox meshGroupListBox { get; private set; }
        public ComboBox materialGroupListBox { get; private set; }
        private ModelSceneNode modelSceneNode;
        protected AnimationController animationController;
        protected SkeletonSceneNode skeletonSceneNode;
        private HitboxSetSceneNode hitboxSetSceneNode;
        private CheckedListBox physicsGroupsComboBox;

        public GLModelViewer(VrfGuiContext guiContext) : base(guiContext)
        {
            //
        }

        public GLModelViewer(VrfGuiContext guiContext, Model model) : base(guiContext)
        {
            this.model = model;
        }

        public GLModelViewer(VrfGuiContext guiContext, PhysAggregateData phys) : base(guiContext)
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
            physicsGroupsComboBox?.Dispose();
            rootMotionCheckBox?.Dispose();
            showSkeletonCheckbox?.Dispose();
            hitboxComboBox?.Dispose();
        }

        protected void AddAnimationControls()
        {
            if (modelSceneNode != null)
            {
                animationComboBox = UiControl.AddSelection("Animation", (animation, _) =>
                {
                    modelSceneNode.SetAnimation(animation);
                    rootMotionCheckBox.Enabled = animationController.ActiveAnimation?.HasMovementData() ?? false;
                    enableRootMotion = rootMotionCheckBox.Enabled && rootMotionCheckBox.Checked;
                });
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
                if (animationController != null)
                {
                    animationController.Frame = frame;
                }
            });

            slowmodeTrackBar = UiControl.AddTrackBar(value =>
            {
                animationController.FrametimeMultiplier = value / 100f;
            });
            slowmodeTrackBar.TrackBar.TickFrequency = 10;
            slowmodeTrackBar.TrackBar.Minimum = 0;
            slowmodeTrackBar.TrackBar.Maximum = 100;
            slowmodeTrackBar.TrackBar.Value = 100;

            animationPlayPause.Enabled = false;
            animationTrackBar.Enabled = false;
            slowmodeTrackBar.Enabled = false;

            var previousPaused = false;
            animationTrackBar.TrackBar.MouseDown += (_, __) =>
            {
                previousPaused = animationController.IsPaused;
                animationController.IsPaused = true;
            };
            animationTrackBar.TrackBar.MouseUp += (_, __) =>
            {
                animationController.IsPaused = previousPaused;
            };

            rootMotionCheckBox = UiControl.AddCheckBox("Show Root Motion", enableRootMotion, (isChecked) =>
            {
                enableRootMotion = isChecked;
                LastRootMotionPosition = modelSceneNode.Transform.Translation;
            });

            rootMotionCheckBox.Checked = false;
            rootMotionCheckBox.Enabled = false;
        }

        protected override void LoadScene()
        {
            base.LoadScene();

            if (model != null)
            {
                modelSceneNode = new ModelSceneNode(Scene, model);
                animationController = modelSceneNode.AnimationController;
                Scene.Add(modelSceneNode, true);

                skeletonSceneNode = new SkeletonSceneNode(Scene, animationController, model.Skeleton);
                Scene.Add(skeletonSceneNode, true);

                if (model.HitboxSets != null && model.HitboxSets.Count > 0)
                {
                    hitboxSetSceneNode = new HitboxSetSceneNode(Scene, animationController, model.HitboxSets);
                    Scene.Add(hitboxSetSceneNode, true);
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

                        var newResource = Scene.GuiContext.LoadFileCompiled(refPhysicsPaths.First());
                        if (newResource != null)
                        {
                            phys = (PhysAggregateData)newResource.DataBlock;
                        }
                    }
                }
            }
            else
            {
                Picker.OnPicked -= OnPicked;
            }

            if (phys != null)
            {
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
        }

        protected override void AddUiControls()
        {
            if (model != null)
            {
                var animations = modelSceneNode.GetSupportedAnimationNames().ToArray();

                if (animations.Length > 0)
                {
                    AddAnimationControls();
                    SetAvailableAnimations(animations);
                    SetAnimationControllerUpdateHandler();
                }

                if (model.Skeleton.Bones.Length > 0)
                {
                    showSkeletonCheckbox = UiControl.AddCheckBox("Show skeleton", false, isChecked =>
                    {
                        if (skeletonSceneNode != null)
                        {
                            skeletonSceneNode.Enabled = isChecked;
                        }
                    });
                }

                if (model.HitboxSets != null && model.HitboxSets.Count > 0)
                {
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

                var meshGroups = modelSceneNode.GetMeshGroups().ToArray<object>();

                if (meshGroups.Length > 1)
                {
                    meshGroupListBox = UiControl.AddMultiSelection("Mesh Group", listBox =>
                    {
                        listBox.Items.AddRange(meshGroups);

                        foreach (var group in modelSceneNode.GetActiveMeshGroups())
                        {
                            listBox.SetItemChecked(listBox.FindStringExact(group), true);
                        }
                    }, modelSceneNode.SetActiveMeshGroups);
                }

                var materialGroupNames = model.GetMaterialGroups().Select(group => group.Name).ToArray<object>();

                if (materialGroupNames.Length > 1)
                {
                    materialGroupListBox = UiControl.AddSelection("Material Group", (selectedGroup, _) =>
                    {
                        modelSceneNode?.SetMaterialGroup(selectedGroup);
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
            void UiAnimationHandler(Animation animation, int frame)
            {
                if (frame == -1)
                {
                    var maximum = animation == null ? 1 : animation.FrameCount - 1;
                    if (maximum < 0)
                    {
                        maximum = 0;
                    }
                    if (animationTrackBar.TrackBar.Maximum != maximum)
                    {
                        animationTrackBar.TrackBar.Maximum = maximum;
                        animationTrackBar.TrackBar.TickFrequency = maximum / 10;
                    }
                    animationTrackBar.Enabled = animation != null;
                    animationPlayPause.Enabled = animation != null;
                    slowmodeTrackBar.Enabled = animation != null;

                    frame = 0;
                }
                else if (animationTrackBar.TrackBar.Value != frame)
                {
                    animationTrackBar.TrackBar.Value = frame;

                }

                if (animationController.ActiveAnimation == null)
                {
                    animationTimeLabel.Text = string.Empty;
                    return;
                }

                var frameCount = animationController.ActiveAnimation.FrameCount;
                var fps = animationController.ActiveAnimation.Fps;
                var totalTime = frameCount / fps;
                var time = animationController.Time % totalTime;
                var frameNumber = animationController.Frame + 1;

                animationTimeLabel.Text = $"Frame: {frameNumber,4} / {frameCount}\n" +
                    $"Time: {time:F2} / {totalTime:F2}\n" +
                    $"FPS: {fps:F2}\n";
            }

            void UpdateUiAnimationState(Animation animation, int frame)
            {
                animationTrackBar.BeginInvoke(() => UiAnimationHandler(animation, frame));
            }
            animationController?.RegisterUpdateHandler(UpdateUiAnimationState);
        }

        private string GetModelStatsText()
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine(CultureInfo.InvariantCulture, $"Mesh Count: {modelSceneNode.RenderableMeshes.Count}");

            foreach (var mesh in modelSceneNode.RenderableMeshes)
            {
                var meshName = mesh.Name.Split(":")[^1];
                var size = mesh.BoundingBox.Max - mesh.BoundingBox.Min;

                var vertexTotal = 0;
                var triangleTotal = 0;
                var coloredMaterialNames = new List<string>();

                void AddColoredMaterialName(DrawCall call)
                {
                    var tintHex = Color32.FromVector4(call.TintColor).HexCode;
                    coloredMaterialNames.Add($"\\{tintHex}{Path.GetFileNameWithoutExtension(call.Material.Material.Name)}");
                }

                foreach (var opaqueDraw in mesh.DrawCallsOpaque)
                {
                    AddColoredMaterialName(opaqueDraw);
                    vertexTotal += (int)opaqueDraw.VertexCount;
                    triangleTotal += opaqueDraw.IndexCount / 3;
                }

                foreach (var blendedDraw in mesh.DrawCallsBlended)
                {
                    AddColoredMaterialName(blendedDraw);
                    vertexTotal += (int)blendedDraw.VertexCount;
                    triangleTotal += blendedDraw.IndexCount / 3;
                }

                foreach (var overlayDraw in mesh.DrawCallsOverlay)
                {
                    AddColoredMaterialName(overlayDraw);
                    vertexTotal += (int)overlayDraw.VertexCount;
                    triangleTotal += overlayDraw.IndexCount / 3;
                }

                var moreThanSixEllipsis = coloredMaterialNames.Count > 6 ? "..." : string.Empty;
                var allColoredMaterials = string.Join("\\#FFFFFFFF, ", coloredMaterialNames.Take(6)) + "\\#FFFFFFFF" + moreThanSixEllipsis;

                sb.Append(CultureInfo.InvariantCulture,
                    $"""

                    Mesh '{meshName}':
                        DrawCalls : {coloredMaterialNames.Count} ({allColoredMaterials})
                        Vertices  : {triangleTotal:N0}
                        Triangles : {vertexTotal:N0}
                        Size      : X: {size.X:0.##} | Y: {size.Y:0.##} | Z: {size.Z:0.##}

                    """
                );
            }

            return sb.ToString();
        }

        private void SetEnabledPhysicsGroups(HashSet<string> physicsGroups)
        {
            foreach (var physNode in Scene.AllNodes.OfType<PhysSceneNode>())
            {
                physNode.Enabled = physicsGroups.Contains(physNode.PhysGroupName);
            }

            Scene.UpdateOctrees();
            SkyboxScene?.UpdateOctrees();
        }

        private Vector3 LastRootMotionPosition;
        private bool enableRootMotion;

        protected override void OnPaint(object sender, RenderEventArgs e)
        {
            if (enableRootMotion && animationController.AnimationFrame is Frame animationFrame)
            {
                var rootMotionDelta = animationFrame.Movement.Position - LastRootMotionPosition;

                modelSceneNode.Transform = modelSceneNode.Transform with
                {
                    Translation = modelSceneNode.Transform.Translation + rootMotionDelta,
                };

                Camera.SetLocation(Camera.Location + rootMotionDelta);
                LastRootMotionPosition = animationFrame.Movement.Position;
            }

            base.OnPaint(sender, e);
        }

        protected override void OnPicked(object sender, PickingTexture.PickingResponse pickingResponse)
        {
            if (modelSceneNode == null)
            {
                return;
            }

            // Void
            if (pickingResponse.PixelInfo.ObjectId == 0)
            {
                SelectedNodeRenderer.SelectNode(null);
                SelectedNodeRenderer.ScreenDebugText = string.Empty;
                return;
            }

            if (pickingResponse.Intent == PickingTexture.PickingIntent.Select)
            {
                var sceneNode = Scene.Find(pickingResponse.PixelInfo.ObjectId);
                SelectedNodeRenderer.SelectNode(sceneNode);
                SelectedNodeRenderer.ScreenDebugText = GetModelStatsText();
                return;
            }

            if (pickingResponse.Intent == PickingTexture.PickingIntent.Open)
            {
                var refMesh = modelSceneNode.GetLod1RefMeshes().FirstOrDefault(x => x.MeshIndex == pickingResponse.PixelInfo.MeshId);
                if (refMesh.MeshName != null)
                {
                    var foundFile = GuiContext.FindFileWithContext(refMesh.MeshName + GameFileLoader.CompiledFileSuffix);
                    if (foundFile.Context != null)
                    {
                        foundFile.Context.GLPostLoadAction = (viewerControl) =>
                        {
                            viewerControl.Camera.CopyFrom(Camera);
                        };

                        Program.MainForm.OpenFile(foundFile.Context, foundFile.PackageEntry);
                    }
                }
            }
        }

        private void SetAvailableAnimations(string[] animations)
        {
            animationComboBox.BeginUpdate();
            animationComboBox.Items.Clear();

            if (animations.Length > 0)
            {
                animationComboBox.Enabled = true;
                animationComboBox.Items.Add($"({animations.Length} animations available)");
                animationComboBox.Items.AddRange(animations);
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
