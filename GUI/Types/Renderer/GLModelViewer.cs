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
        private ComboBox animationComboBox;
        private CheckBox animationPlayPause;
        private GLViewerTrackBarControl animationTrackBar;
        private CheckedListBox meshGroupListBox;
        private ComboBox materialGroupListBox;
        private ModelSceneNode modelSceneNode;
        private MeshSceneNode meshSceneNode;

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
            animationTrackBar = ViewerControl.AddTrackBar("Animation Frame", frame =>
            {
                if (modelSceneNode != null)
                {
                    modelSceneNode.AnimationController.Frame = frame;
                }
            });
            animationPlayPause.Enabled = false;
            animationTrackBar.Enabled = false;
        }

        protected override void LoadScene()
        {
            if (model != null)
            {
                modelSceneNode = new ModelSceneNode(Scene, model);
                SetAvailableAnimations(modelSceneNode.GetSupportedAnimationNames());
                Scene.Add(modelSceneNode, false);

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
                    materialGroupListBox = ViewerControl.AddSelection("Material Group", (selectedGroup,_) =>
                    {
                        modelSceneNode?.SetSkin(selectedGroup);
                    });

                    materialGroupListBox.Items.AddRange(materialGroups.ToArray<object>());
                    materialGroupListBox.SelectedIndex = 0;
                }

                modelSceneNode.AnimationController.RegisterUpdateHandler((animation, frame) =>
                {
                    if (animationTrackBar.TrackBar.Value != frame)
                    {
                        animationTrackBar.UpdateValueSilently(frame);
                    }
                    var maximum = animation == null ? 1 : animation.FrameCount - 1;
                    if (animationTrackBar.TrackBar.Maximum != maximum)
                    {
                        animationTrackBar.TrackBar.Maximum = maximum;
                    }
                    animationTrackBar.Enabled = animation != null;
                    animationPlayPause.Enabled = animation != null;
                });
            }
            else
            {
                SetAvailableAnimations(Enumerable.Empty<string>());
            }

            if (mesh != null)
            {
                meshSceneNode = new MeshSceneNode(Scene, mesh);
                Scene.Add(meshSceneNode, false);
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
