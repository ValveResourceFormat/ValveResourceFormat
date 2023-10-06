using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with model controls (render mode, animation panels).
    /// </summary>
    class GLModelViewer : GLSceneViewer
    {
        private readonly Model model;
        private readonly Mesh mesh;
        private readonly SmartProp smartProp;
        private PhysAggregateData phys;
        public ComboBox animationComboBox { get; private set; }
        private CheckBox animationPlayPause;
        private CheckBox showSkeletonCheckbox;
        private GLViewerTrackBarControl animationTrackBar;
        private GLViewerTrackBarControl slowmodeTrackBar;
        public CheckedListBox meshGroupListBox { get; private set; }
        public ComboBox materialGroupListBox { get; private set; }
        private ModelSceneNode modelSceneNode;
        private MeshSceneNode meshSceneNode;
        private SkeletonSceneNode skeletonSceneNode;
        private CheckedListBox physicsGroupsComboBox;

        public GLModelViewer(VrfGuiContext guiContext, Model model)
            : base(guiContext, Frustum.CreateEmpty())
        {
            this.model = model;
        }

        public GLModelViewer(VrfGuiContext guiContext, Mesh mesh)
           : base(guiContext, Frustum.CreateEmpty())
        {
            this.mesh = mesh;
        }

        public GLModelViewer(VrfGuiContext guiContext, PhysAggregateData phys)
           : base(guiContext, Frustum.CreateEmpty())
        {
            this.phys = phys;
        }

        public GLModelViewer(VrfGuiContext guiContext, SmartProp smartProp)
            : base(guiContext, Frustum.CreateEmpty())
        {
            this.smartProp = smartProp;
        }

        public GLModelViewer(VrfGuiContext guiContext, AnimGraph animGraph)
            : base(guiContext, Frustum.CreateEmpty())
        {
            var animGraphAssociatedModel = animGraph.Data.GetProperty<string>("m_modelName");
            this.model = (Model)guiContext.LoadFileCompiled(animGraphAssociatedModel).DataBlock;
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
            }

            base.Dispose(disposing);
        }

        protected override void InitializeControl()
        {
            AddRenderModeSelectionControl();
            AddBaseGridControl();

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

            showSkeletonCheckbox = AddCheckBox("Show skeleton", false, isChecked =>
            {
                if (skeletonSceneNode != null)
                {
                    skeletonSceneNode.Enabled = isChecked;
                }
            });
        }

        protected override void LoadScene()
        {
            LoadDefaultEnviromentMap();

            if (model != null)
            {
                modelSceneNode = new ModelSceneNode(Scene, model);
                SetAvailableAnimations(modelSceneNode.GetSupportedAnimationNames());
                Scene.Add(modelSceneNode, true);

                skeletonSceneNode = new SkeletonSceneNode(Scene, modelSceneNode.AnimationController, model.Skeleton);
                Scene.Add(skeletonSceneNode, true);

                phys = model.GetEmbeddedPhys();
                if (phys == null)
                {
                    var refPhysicsPaths = model.GetReferencedPhysNames().ToArray();
                    if (refPhysicsPaths.Any())
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
                    }, selectedGroups =>
                    {
                        modelSceneNode.SetActiveMeshGroups(selectedGroups);
                    });
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
                SetAvailableAnimations(Enumerable.Empty<string>());
                Camera.Picker.OnPicked -= OnPicked;
            }

            if (mesh != null)
            {
                meshSceneNode = new MeshSceneNode(Scene, mesh, 0);
                Scene.Add(meshSceneNode, false);
            }

            if (phys != null)
            {
                var physSceneNodes = PhysSceneNode.CreatePhysSceneNodes(Scene, phys, null);

                foreach (var physSceneNode in physSceneNodes)
                {
                    Scene.Add(physSceneNode, false);
                }

                // Physics are not shown by default unless the model has no meshes
                var enabledAllPhysByDefault = modelSceneNode == null || !modelSceneNode.RenderableMeshes.Any();

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

                        SetEnabledPhysicsGroups(physicsGroups.ToHashSet());
                    }, (enabledPhysicsGroups) =>
                    {
                        SetEnabledPhysicsGroups(enabledPhysicsGroups.ToHashSet());
                    });
                }
            }

            if (smartProp != null)
            {
                var children = smartProp.Data.GetArray("m_Children");

                foreach (var child in children)
                {
                    var className = child.GetStringProperty("_class");

                    switch (className)
                    {
                        case "CSmartPropElement_Model":
                            {
                                using var resource = GuiContext.LoadFileCompiled(child.GetStringProperty("m_sModelName"));
                                var model = (Model)resource.DataBlock;

                                modelSceneNode = new ModelSceneNode(Scene, model);
                                Scene.Add(modelSceneNode, true);

                                break;
                            }
                        case "CSmartPropElement_SmartProp":
                            {
                                // TODO: m_sSmartProp - create SmartPropSceneNode?
                                break;
                            }
                        case "CSmartPropElement_Group":
                        case "CSmartPropElement_PickOne":
                            {
                                var pickOneChildren = child.GetArray("m_Children");

                                // TODO: This probably should recurse into parent smartprop loader
                                foreach (var pickOneChild in pickOneChildren)
                                {
                                    var pickOneClass = pickOneChild.GetStringProperty("_class");

                                    if (pickOneClass != "CSmartPropElement_Model")
                                    {
                                        Log.Warn(nameof(GLModelViewer), $"Unhandled smart prop class {className}");
                                        continue;
                                    }

                                    using var resource = GuiContext.LoadFileCompiled(pickOneChild.GetStringProperty("m_sModelName"));
                                    var model = (Model)resource.DataBlock;

                                    modelSceneNode = new ModelSceneNode(Scene, model);
                                    Scene.Add(modelSceneNode, true);
                                }

                                break;
                            }
                        default:
                            Log.Warn(nameof(GLModelViewer), $"Unhandled smart prop class {className}");
                            break;
                    }
                }
            }

            Scene.CalculateEnvironmentMaps();
        }

        private void LoadDefaultEnviromentMap()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream($"GUI.Utils.inspect_agents_custom_cubemap.vtex_c");

            using var resource = new Resource()
            {
                FileName = "vrf_default_cubemap.vtex_c"
            };
            resource.Read(stream);

            var texture = Scene.GuiContext.MaterialLoader.LoadTexture(resource);
            var envMap = new SceneEnvMap(Scene, new AABB(new Vector3(float.MinValue), new Vector3(float.MaxValue)))
            {
                Transform = Matrix4x4.Identity,
                EdgeFadeDists = Vector3.Zero,
                HandShake = 0,
                ProjectionMode = 0,
                EnvMapTexture = texture,
            };

            Scene.LightingInfo.Lightmaps.TryAdd("g_tEnvironmentMap", texture);
            Scene.LightingInfo.EnvMaps.Add(0, envMap);
            Scene.RenderAttributes["SCENE_ENVIRONMENT_TYPE"] = 2;
        }

        private void SetEnabledPhysicsGroups(HashSet<string> physicsGroups)
        {
            foreach (var physNode in Scene.AllNodes.OfType<PhysSceneNode>())
            {
                physNode.Enabled = physicsGroups.Contains(physNode.PhysGroupName);
            }
        }

        protected override void OnPaint(object sender, RenderEventArgs e)
        {
            Scene.LightingInfo.LightingData.SunLightPosition = Camera.ViewProjectionMatrix;
            Scene.LightingInfo.LightingData.SunLightColor = Vector4.One;

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
                    var foundFile = GuiContext.FileLoader.FindFileWithContext(refMesh.MeshName + "_c");
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
                                    glViewer.GLPostLoad = (viewerControl) => viewerControl.Camera.CopyFrom(Scene.MainCamera);
                                }
                            },
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnRanToCompletion,
                        TaskScheduler.Default);
                    }
                }
            }
        }

        private void SetAvailableAnimations(IEnumerable<string> animations)
        {
            animationComboBox.BeginUpdate();
            animationComboBox.Items.Clear();

            var animationsArray = animations.ToArray();

            if (animationsArray.Length > 0)
            {
                animationComboBox.Enabled = true;
                animationComboBox.Items.Add($"({animationsArray.Length} animations available)");
                animationComboBox.Items.AddRange(animationsArray);
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
