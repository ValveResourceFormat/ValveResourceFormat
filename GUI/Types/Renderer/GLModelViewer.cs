using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with model controls (render mode, animation panels).
    /// </summary>
    internal class GLModelViewer : GLSceneViewer
    {
        private readonly Model model;
        private readonly Mesh mesh;
        private PhysAggregateData phys;
        private ComboBox animationComboBox;
        private CheckBox animationPlayPause;
        private GLViewerTrackBarControl animationTrackBar;
        private CheckedListBox meshGroupListBox;
        private ComboBox materialGroupListBox;
        private ModelSceneNode modelSceneNode;
        private MeshSceneNode meshSceneNode;
        private PhysSceneNode physSceneNode;

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

        protected override void InitializeControl()
        {
            AddRenderModeSelectionControl();

            animationComboBox = ViewerControl.AddSelection("Animation", (animation, _) =>
            {
                modelSceneNode?.SetAnimation(animation);
            });
            animationPlayPause = ViewerControl.AddCheckBox("Autoplay", true, isChecked =>
            {
                if (modelSceneNode != null)
                {
                    modelSceneNode.AnimationController.IsPaused = !isChecked;
                }
            });
            animationTrackBar = ViewerControl.AddTrackBar(frame =>
            {
                if (modelSceneNode != null)
                {
                    modelSceneNode.AnimationController.Frame = frame;
                }
            });
            animationPlayPause.Enabled = false;
            animationTrackBar.Enabled = false;

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
            if (model != null)
            {
                modelSceneNode = new ModelSceneNode(Scene, model);
                SetAvailableAnimations(modelSceneNode.GetSupportedAnimationNames());
                Scene.Add(modelSceneNode, true);

                phys = model.GetEmbeddedPhys();
                if (phys == null)
                {
                    var refPhysicsPaths = model.GetReferencedPhysNames();
                    if (refPhysicsPaths.Any())
                    {
                        //TODO are there any models with more than one vphys?
                        if (refPhysicsPaths.Count() != 1)
                        {
                            Console.WriteLine($"Model has more than 1 vphys ({refPhysicsPaths.Count()})." +
                                " Please report this on https://github.com/SteamDatabase/ValveResourceFormat and provide the file that caused this.");
                        }

                        var newResource = Scene.GuiContext.LoadFileByAnyMeansNecessary(refPhysicsPaths.First() + "_c");
                        if (newResource != null)
                        {
                            phys = (PhysAggregateData)newResource.DataBlock;
                        }
                    }
                }

                var meshGroups = modelSceneNode.GetMeshGroups();

                if (meshGroups.Count() > 1)
                {
                    meshGroupListBox = ViewerControl.AddMultiSelection("Mesh Group", selectedGroups =>
                    {
                        modelSceneNode.SetActiveMeshGroups(selectedGroups);
                    });

                    meshGroupListBox.Items.AddRange(modelSceneNode.GetMeshGroups().ToArray<object>());
                    foreach (var group in modelSceneNode.GetActiveMeshGroups())
                    {
                        meshGroupListBox.SetItemChecked(meshGroupListBox.FindStringExact(group), true);
                    }
                }

                var materialGroups = model.GetMaterialGroups();

                if (materialGroups.Count() > 1)
                {
                    materialGroupListBox = ViewerControl.AddSelection("Material Group", (selectedGroup, _) =>
                    {
                        modelSceneNode?.SetSkin(selectedGroup);
                    });

                    materialGroupListBox.Items.AddRange(materialGroups.ToArray<object>());
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
            }

            if (mesh != null)
            {
                meshSceneNode = new MeshSceneNode(Scene, mesh, 0);
                Scene.Add(meshSceneNode, false);
            }

            if (phys != null)
            {
                physSceneNode = new PhysSceneNode(Scene, phys);
                Scene.Add(physSceneNode, false);

                //disabled by default. Enable if viewing only phys or model without meshes
                physSceneNode.Enabled = modelSceneNode == null || !modelSceneNode.RenderableMeshes.Any();

                ViewerControl.AddCheckBox("Show Physics", physSceneNode.Enabled, (v) => { physSceneNode.Enabled = v; });
            }
        }

        private void SetAvailableAnimations(IEnumerable<string> animations)
        {
            animationComboBox.BeginUpdate();
            animationComboBox.Items.Clear();

            var count = animations.Count();

            if (count > 0)
            {
                animationComboBox.Enabled = true;
                animationComboBox.Items.Add($"({count} animations available)");
                animationComboBox.Items.AddRange(animations.ToArray());
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
