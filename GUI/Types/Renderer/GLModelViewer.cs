using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    class GLModelViewer : GLSingleNodeViewer
    {
        protected Model model { get; init; }
        private PhysAggregateData phys;
        public ComboBox animationComboBox { get; private set; }
        private CheckBox animationPlayPause;
        private CheckBox showSkeletonCheckbox;
        private ComboBox hitboxComboBox;
        private GLViewerTrackBarControl animationTrackBar;
        private GLViewerTrackBarControl slowmodeTrackBar;
        public CheckedListBox meshGroupListBox { get; private set; }
        public ComboBox materialGroupListBox { get; private set; }
        private ModelSceneNode modelSceneNode;
        private SkeletonSceneNode skeletonSceneNode;
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                animationComboBox?.Dispose();
                animationPlayPause?.Dispose();
                animationTrackBar?.Dispose();
                slowmodeTrackBar?.Dispose();
                meshGroupListBox?.Dispose();
                materialGroupListBox?.Dispose();
                physicsGroupsComboBox?.Dispose();
                showSkeletonCheckbox?.Dispose();
                hitboxComboBox?.Dispose();
            }

            base.Dispose(disposing);
        }

        private void AddAnimationControls()
        {
            animationComboBox = AddSelection("Animation", (animation, _) =>
            {
                modelSceneNode?.SetAnimation(animation);
            });
            animationPlayPause = AddCheckBox("Autoplay", true, isChecked =>
            {
                if (modelSceneNode != null)
                {
                    modelSceneNode.AnimationController.IsPaused = !isChecked;
                }
            });
            animationTrackBar = AddTrackBar(frame =>
            {
                if (modelSceneNode != null)
                {
                    modelSceneNode.AnimationController.Frame = frame;
                }
            });

            slowmodeTrackBar = AddTrackBar(value =>
            {
                modelSceneNode.AnimationController.FrametimeMultiplier = value / 100f;
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
                previousPaused = modelSceneNode.AnimationController.IsPaused;
                modelSceneNode.AnimationController.IsPaused = true;
            };
            animationTrackBar.TrackBar.MouseUp += (_, __) =>
            {
                modelSceneNode.AnimationController.IsPaused = previousPaused;
            };
        }

        protected override void LoadScene()
        {
            base.LoadScene();

            if (model != null)
            {
                modelSceneNode = new ModelSceneNode(Scene, model);
                Scene.Add(modelSceneNode, true);

                var animations = modelSceneNode.GetSupportedAnimationNames().ToArray();

                if (animations.Length > 0)
                {
                    AddAnimationControls();
                    SetAvailableAnimations(animations);
                }

                skeletonSceneNode = new SkeletonSceneNode(Scene, modelSceneNode.AnimationController, model.Skeleton);
                Scene.Add(skeletonSceneNode, true);

                if (model.Skeleton.Bones.Length > 0)
                {
                    showSkeletonCheckbox = AddCheckBox("Show skeleton", false, isChecked =>
                    {
                        if (skeletonSceneNode != null)
                        {
                            skeletonSceneNode.Enabled = isChecked;
                        }
                    });
                }

                var meshes = model.GetEmbeddedMeshes();
                var firstMesh = meshes.Select(m => m.Mesh)
                                      .FirstOrDefault((Mesh)null);
                if (firstMesh != null && firstMesh.HitboxSets.Count > 0)
                {
                    var hitboxSets = firstMesh.HitboxSets;
                    hitboxSetSceneNode = new HitboxSetSceneNode(Scene, modelSceneNode.AnimationController, model.Skeleton, hitboxSets);
                    Scene.Add(hitboxSetSceneNode, true);

                    hitboxComboBox = AddSelection("Hitbox Set", (hitboxSet, i) =>
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

                var meshGroups = modelSceneNode.GetMeshGroups().ToArray<object>();

                if (meshGroups.Length > 1)
                {
                    meshGroupListBox = AddMultiSelection("Mesh Group", listBox =>
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
                    materialGroupListBox = AddSelection("Material Group", (selectedGroup, _) =>
                    {
                        modelSceneNode?.SetMaterialGroup(selectedGroup);
                    });

                    materialGroupListBox.Items.AddRange(materialGroupNames);
                    materialGroupListBox.SelectedIndex = 0;
                }

                modelSceneNode.AnimationController.RegisterUpdateHandler((animation, frame) =>
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
                });
            }
            else
            {
                Picker.OnPicked -= OnPicked;
            }

            if (phys != null)
            {
                var physSceneNodes = PhysSceneNode.CreatePhysSceneNodes(Scene, phys, null);

                foreach (var physSceneNode in physSceneNodes)
                {
                    Scene.Add(physSceneNode, false);
                }

                // Physics are not shown by default unless the model has no meshes
                var enabledAllPhysByDefault = modelSceneNode == null || modelSceneNode.RenderableMeshes.Count == 0;

                var physicsGroups = Scene.AllNodes
                    .OfType<PhysSceneNode>()
                    .Select(r => r.PhysGroupName)
                    .Distinct()
                    .ToArray();

                if (physicsGroups.Length > 0)
                {
                    physicsGroupsComboBox = AddMultiSelection("Physics Groups", (listBox) =>
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

                        SetEnabledPhysicsGroups([.. physicsGroups]);
                    }, (enabledPhysicsGroups) =>
                    {
                        SetEnabledPhysicsGroups(enabledPhysicsGroups.ToHashSet());
                    });
                }
            }
        }

        private void SetEnabledPhysicsGroups(HashSet<string> physicsGroups)
        {
            foreach (var physNode in Scene.AllNodes.OfType<PhysSceneNode>())
            {
                physNode.Enabled = physicsGroups.Contains(physNode.PhysGroupName);
            }
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
                selectedNodeRenderer.SelectNode(null);
                return;
            }

            if (pickingResponse.Intent == PickingTexture.PickingIntent.Select)
            {
                Log.Info(nameof(GLModelViewer), $"Selected mesh {pickingResponse.PixelInfo.MeshId}, ({pickingResponse.PixelInfo.ObjectId}.");

                var sceneNode = Scene.Find(pickingResponse.PixelInfo.ObjectId);
                selectedNodeRenderer.SelectNode(sceneNode);

                return;
            }

            if (pickingResponse.Intent == PickingTexture.PickingIntent.Open)
            {
                var refMesh = modelSceneNode.GetLod1RefMeshes().FirstOrDefault(x => x.MeshIndex == pickingResponse.PixelInfo.MeshId);
                if (refMesh.MeshName != null)
                {
                    var foundFile = GuiContext.FileLoader.FindFileWithContext(refMesh.MeshName + GameFileLoader.CompiledFileSuffix);
                    if (foundFile.Context != null)
                    {
                        var task = Program.MainForm.OpenFile(foundFile.Context, foundFile.PackageEntry);
                        task.ContinueWith(
                            t =>
                            {
                                var glViewer = t.Result.Controls.OfType<TabControl>().FirstOrDefault()?
                                    .Controls.OfType<TabPage>().First(tab => tab.Controls.OfType<GLViewerControl>() is not null)?
                                    .Controls.OfType<GLViewerControl>().First();
                                if (glViewer is not null)
                                {
                                    glViewer.GLPostLoad = (viewerControl) => viewerControl.Camera.CopyFrom(Camera);
                                }
                            },
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnRanToCompletion,
                        TaskScheduler.Default);
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
