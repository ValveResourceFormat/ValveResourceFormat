using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Particles;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// GL Render control with particle controls (control points? particle counts?).
    /// Renders a list of ParticleRenderers.
    /// </summary>
    class GLParticleViewer : GLSceneViewer
    {
        private readonly ParticleSystem particleSystem;
        private ParticleSceneNode? particleSceneNode;
        private GLViewerSliderControl? slowmodeTrackBar;
        private ThemedButton? restartButton;
        private bool ShowRenderBounds { get; set; }

        public GLParticleViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, ParticleSystem particleSystem) : base(vrfGuiContext, rendererContext, Frustum.CreateEmpty())
        {
            this.particleSystem = particleSystem;
        }

        public override void Dispose()
        {
            base.Dispose();

            slowmodeTrackBar?.Dispose();
            restartButton?.Dispose();
        }

        protected override void LoadScene()
        {
            particleSceneNode = new ParticleSceneNode(Scene, particleSystem, null, true)
            {
                Transform = Matrix4x4.Identity
            };
            Scene.Add(particleSceneNode, true);
        }

        protected override void OnGLLoad()
        {
            base.OnGLLoad();

            Input.Camera.SetLocation(new Vector3(200, 200, 200));
            Input.Camera.LookAt(Vector3.Zero);
        }

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);
            Debug.Assert(SelectedNodeRenderer != null);

            AddRenderModeSelectionControl();
            AddBaseGridControl();

            restartButton = new ThemedButton
            {
                Text = "Restart",
                AutoSize = true,
            };
            restartButton.Click += (_, _) =>
            {
                using var lockedGl = MakeCurrent();
                particleSceneNode?.Restart();
            };

            using (UiControl.BeginGroup("Playback"))
            {
                UiControl.AddControl(restartButton);

                slowmodeTrackBar = UiControl.AddTrackBar(value =>
                {
                    particleSceneNode?.FrametimeMultiplier = value;
                }, particleSceneNode?.FrametimeMultiplier ?? 1f);
            }

            using (UiControl.BeginGroup("Display"))
            {
                UiControl.AddCheckBox("Show Render Bounds", ShowRenderBounds, value => SelectedNodeRenderer.SelectNode(value ? particleSceneNode : null));
            }

            AddOperatorTree();

            base.AddUiControls();
        }

        private void AddOperatorTree()
        {
            Debug.Assert(UiControl != null);

            var unsupportedColor = Color.FromArgb(224, 80, 80);

            AddFunctionGroup("Emitters", particleSystem.GetEmitters(), ParticleSupportInfo.IsEmitterSupported, unsupportedColor);
            AddFunctionGroup("Initializers", particleSystem.GetInitializers(), ParticleSupportInfo.IsInitializerSupported, unsupportedColor);
            AddFunctionGroup("Operators", particleSystem.GetOperators(), ParticleSupportInfo.IsOperatorSupported, unsupportedColor);
            AddFunctionGroup("Force Generators", particleSystem.GetForceGenerators(), ParticleSupportInfo.IsForceGeneratorSupported, unsupportedColor);
            AddFunctionGroup("Renderers", particleSystem.GetRenderers(), ParticleSupportInfo.IsRendererSupported, unsupportedColor);
            AddFunctionGroup("Pre-Emission Operators", particleSystem.GetPreEmissionOperators(), ParticleSupportInfo.IsPreEmissionOperatorSupported, unsupportedColor);
        }

        private void AddFunctionGroup(string groupName, IEnumerable<KVObject> functions, Func<string, bool> isSupported, Color unsupportedColor)
        {
            Debug.Assert(UiControl != null);

            var functionList = functions.ToList();
            if (functionList.Count == 0)
            {
                return;
            }

            var listBox = new ListBox
            {
                Dock = DockStyle.Fill,
                DrawMode = DrawMode.OwnerDrawFixed,
                BorderStyle = BorderStyle.None,
                SelectionMode = SelectionMode.None,
                IntegralHeight = false,
            };

            foreach (var function in functionList)
            {
                var className = function.GetStringProperty("_class");
                var displayName = StripClassPrefix(className);
                listBox.Items.Add(new ParticleFunctionItem(displayName, isSupported(className)));
            }

            listBox.DrawItem += (_, e) =>
            {
                if (e.Index < 0)
                {
                    return;
                }

                using var brush = new SolidBrush(listBox.BackColor);
                e.Graphics.FillRectangle(brush, e.Bounds);

                var item = (ParticleFunctionItem)listBox.Items[e.Index];
                var color = item.IsSupported ? listBox.ForeColor : unsupportedColor;

                System.Windows.Forms.TextRenderer.DrawText(e.Graphics, item.ClassName, e.Font, e.Bounds, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            };

            Themer.ThemeControl(listBox);

            listBox.Height = listBox.ItemHeight * functionList.Count + 2;

            using (UiControl.BeginGroup(groupName))
            {
                UiControl.AddControl(listBox);
            }
        }

        private static string StripClassPrefix(string className)
        {
            if (className.StartsWith("C_OP_", StringComparison.Ordinal))
            {
                return className[5..];
            }

            if (className.StartsWith("C_INIT_", StringComparison.Ordinal))
            {
                return className[7..];
            }

            return className;
        }

        private sealed record ParticleFunctionItem(string ClassName, bool IsSupported);

        protected override void OnPicked(object? sender, PickingTexture.PickingResponse pixelInfo)
        {
            //
        }
    }
}
