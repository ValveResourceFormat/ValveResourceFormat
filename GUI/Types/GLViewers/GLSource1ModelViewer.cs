using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Source1;
using ValveResourceFormat.Source1.FileSystem;
using ValveResourceFormat.Source1.Model;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// Scene of a Source 1 model. The renderer context reads materials and textures through <see cref="Source1FileSystem"/>.
    /// </summary>
    class GLSource1ModelViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, Source1FileSystem? fileSystem, Source1Model model)
        : GLSingleNodeViewer(vrfGuiContext, rendererContext)
    {
        public Source1FileSystem? FileSystem { get; } = fileSystem;

        public Source1Model Model { get; } = model;

        private Source1ModelSceneNode? modelNode;
        private CheckBox? autoplayCheckBox;
        private ComboBox? sequenceComboBox;

        public override void Dispose()
        {
            base.Dispose();

            autoplayCheckBox?.Dispose();
            sequenceComboBox?.Dispose();
        }

        protected override void LoadScene()
        {
            base.LoadScene();

            modelNode = new Source1ModelSceneNode(Scene, Model, animated: true, flexes: true);
            Scene.Add(modelNode, true);
        }

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);

            if (modelNode != null)
            {
                Input.OrbitTargetProvider = () => modelNode.BoundingBox.Center;

                AddModelControls(modelNode);

                if (modelNode.IsAnimated && modelNode.PlayableSequences.Count > 0)
                {
                    AddAnimationControls(modelNode);
                }

                if (modelNode.HasFlexes)
                {
                    AddFlexControllerControls(modelNode);
                }
            }

            base.AddUiControls();
        }

        private void AddModelControls(Source1ModelSceneNode node)
        {
            Debug.Assert(UiControl != null);

            using var _ = UiControl.BeginGroup("Model");

            if (node.SkinCount > 1)
            {
                var skinComboBox = UiControl.AddSelection("Skin", (_, index) =>
                {
                    if (index < 0)
                    {
                        return;
                    }

                    using var lockedGl = MakeCurrent();
                    node.SetSkin(index);
                });

                for (var i = 0; i < node.SkinCount; i++)
                {
                    skinComboBox.Items.Add(string.Create(CultureInfo.InvariantCulture, $"Skin {i}"));
                }

                skinComboBox.SelectedIndex = node.Skin;
            }

            for (var partIndex = 0; partIndex < Model.BodyParts.Length; partIndex++)
            {
                var part = Model.BodyParts[partIndex];

                if (part.Models.Length < 2)
                {
                    continue;
                }

                var capturedPartIndex = partIndex;

                var bodyGroupComboBox = UiControl.AddSelection(part.Name, (_, index) =>
                {
                    if (index < 0)
                    {
                        return;
                    }

                    using var lockedGl = MakeCurrent();
                    node.SetBodyGroup(capturedPartIndex, index);
                });

                for (var i = 0; i < part.Models.Length; i++)
                {
                    var subModelName = part.Models[i].Name;
                    bodyGroupComboBox.Items.Add(string.IsNullOrEmpty(subModelName) ? string.Create(CultureInfo.InvariantCulture, $"Model {i}") : subModelName);
                }

                bodyGroupComboBox.SelectedIndex = node.GetBodyGroup(partIndex);
            }
        }

        private void AddAnimationControls(Source1ModelSceneNode node)
        {
            Debug.Assert(UiControl != null);

            using (UiControl.BeginGroup("Animation"))
            {
                sequenceComboBox = UiControl.AddSelection("Sequence", (name, index) =>
                {
                    if (index < 0)
                    {
                        return;
                    }

                    node.PlaySequence(index == 0 ? null : name);
                });

                sequenceComboBox.Items.Add("(reference pose)");
                sequenceComboBox.Items.AddRange([.. node.PlayableSequences.Select(static sequence => sequence.Name).Order(StringComparer.OrdinalIgnoreCase)]);
                var defaultIndex = sequenceComboBox.Items.IndexOf("ref");

                if (defaultIndex < 0)
                {
                    defaultIndex = sequenceComboBox.Items.IndexOf("idle");
                }

                sequenceComboBox.SelectedIndex = Math.Max(defaultIndex, 0);

                autoplayCheckBox = UiControl.AddCheckBox("Autoplay", true, isChecked =>
                {
                    node.Paused = !isChecked;
                });

                UiControl.AddCheckBox("Loop", true, isChecked =>
                {
                    node.Loop = isChecked;
                });

                node.Loop = true;

                UiControl.AddCheckBox("Root Motion", false, isChecked =>
                {
                    node.RootMotion = isChecked;
                });

                UiControl.AddTrackBar("Animation Frame", cycle =>
                {
                    node.Paused = true;
                    node.Cycle = cycle;
                });

                UiControl.AddTrackBar("Animation Speed", value =>
                {
                    node.PlaybackSpeed = value * 2f;
                }, 0.5f);
            }

            AddPoseParameterControls(node);
        }

        private void AddFlexControllerControls(Source1ModelSceneNode node)
        {
            Debug.Assert(UiControl != null);

            using var _ = UiControl.BeginGroup("Flex Controllers");

            var controllers = Model.FlexControllers;

            for (var i = 0; i < controllers.Length; i++)
            {
                var controller = controllers[i];
                var range = controller.Max - controller.Min;

                if (range <= 0f)
                {
                    continue;
                }

                var index = i;
                var min = controller.Min;
                var normalizedDefault = (node.GetFlexController(index) - min) / range;

                UiControl.AddTrackBar(controller.Name, value =>
                {
                    node.SetFlexController(index, min + (value * range));
                }, normalizedDefault);
            }
        }

        private void AddPoseParameterControls(Source1ModelSceneNode node)
        {
            Debug.Assert(UiControl != null);

            var parameters = Model.PoseParameters;

            if (parameters.Length == 0)
            {
                return;
            }

            var defaults = Model.CreateDefaultPoseParameters();

            using var _ = UiControl.BeginGroup("Pose Parameters");

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var low = MathF.Min(parameter.Start, parameter.End);
                var high = MathF.Max(parameter.Start, parameter.End);
                var range = high - low;

                if (range <= 0f)
                {
                    continue;
                }

                var name = parameter.Name;
                var normalizedDefault = (defaults[i] - low) / range;

                UiControl.AddTrackBar(name, value =>
                {
                    node.SetPoseParameter(name, low + (value * range));
                }, normalizedDefault);
            }
        }
    }
}
