using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
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
        private Label drawCallsLabel;
        private ComboBox animationComboBox;
        private CheckedListBox meshGroupListBox;
        private ModelSceneNode modelSceneNode;
        private MeshSceneNode meshSceneNode;

        public GLModelViewer(VrfGuiContext guiContext, Model model)
            : base(guiContext)
        {
            this.model = model;
        }

        public GLModelViewer(VrfGuiContext guiContext, Mesh mesh)
           : base(guiContext)
        {
            this.mesh = mesh;
        }

        protected override void InitializeControl()
        {
            drawCallsLabel = ViewerControl.AddLabel("Drawcalls: 0");

            AddRenderModeSelectionControl();

            animationComboBox = ViewerControl.AddSelection("Animation", (animation, _) =>
            {
                if (modelSceneNode != null)
                {
                    modelSceneNode.SetAnimation(animation);
                }
            });
        }

        protected override void LoadScene()
        {
            if (model != null)
            {
                modelSceneNode = new ModelSceneNode(Scene, model, null, false);
                SetAvailableAnimations(modelSceneNode.GetSupportedAnimationNames());
                Scene.Add(modelSceneNode, false);

                var meshGroups = modelSceneNode.GetMeshGroups();

                if (meshGroups.Count() > 1)
                {
                    meshGroupListBox = ViewerControl.AddMultiSelection("Mesh Group", selectedGroups =>
                    {
                        modelSceneNode.SetActiveMeshGroups(selectedGroups);
                    });

                    meshGroupListBox.Items.AddRange(modelSceneNode.GetMeshGroups().ToArray());
                    foreach (var group in modelSceneNode.GetActiveMeshGroups())
                    {
                        meshGroupListBox.SetItemChecked(meshGroupListBox.FindStringExact(group), true);
                    }
                }
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
            animationComboBox.Items.Clear();
            if (animations.Any())
            {
                animationComboBox.Enabled = true;
                animationComboBox.Items.AddRange(animations.ToArray());
                animationComboBox.SelectedIndex = 0;
            }
            else
            {
                animationComboBox.Items.Add("(no animations available)");
                animationComboBox.SelectedIndex = 0;
                animationComboBox.Enabled = false;
            }
        }
    }
}
