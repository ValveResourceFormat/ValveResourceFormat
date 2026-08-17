using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Particles;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// Renders a single <see cref="ParticleSystem"/> via a <see cref="ParticleSceneNode"/>, with UI controls for playback and an operator/renderer tree.
    /// </summary>
    class GLParticleViewer : GLSceneViewer
    {
        private readonly ParticleSystem particleSystem;
        private readonly ParticleSnapshot? particleSnapshot;
        private ParticleSceneNode? particleSceneNode;
        private GLViewerSliderControl? slowmodeTrackBar;
        private ThemedButton? restartButton;
        private ThemedButton? endCapButton;
        private float screenSize = SnapshotParticleSystem.DefaultScreenSize;
        private bool ShowRenderBounds { get; set; }

        public GLParticleViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, ParticleSystem particleSystem, ParticleSnapshot? particleSnapshot = null)
            : base(vrfGuiContext, rendererContext, Frustum.CreateEmpty())
        {
            this.particleSystem = particleSystem;
            this.particleSnapshot = particleSnapshot;
        }

        public override void Dispose()
        {
            base.Dispose();

            slowmodeTrackBar?.Dispose();
            restartButton?.Dispose();
            endCapButton?.Dispose();
        }

        protected override void LoadScene()
        {
            InitializeSoundPlayer();
            LoadDefaultLighting();
            Scene.LightingInfo.UseSceneBoundsForSunLightFrustum = false;

            particleSceneNode = new ParticleSceneNode(Scene, particleSystem, particleSnapshot, true)
            {
                Transform = Matrix4x4.Identity
            };

            if (particleSnapshot != null)
            {
                particleSceneNode.SetTextureOverride(Scene.RendererContext.MaterialLoader.GetDefaultColor());
            }

            Scene.Add(particleSceneNode, true);
        }

        protected override void OnGLLoad()
        {
            base.OnGLLoad();

            if (particleSnapshot != null)
            {
                var bounds = SnapshotParticleSystem.GetBounds(particleSnapshot);
                var size = bounds.Size;

                Input.Camera.FrameObject(bounds.Center, size.X, size.Y, size.Z);
                Input.OrbitTargetProvider = () => bounds.Center;

                ApplyScreenSize();
                return;
            }

            Input.Camera.SetLocation(new Vector3(200, 200, 200));
            Input.Camera.LookAt(Vector3.Zero);
        }

        private void ApplyScreenSize()
        {
            if (particleSceneNode != null && particleSnapshot != null && SnapshotParticleSystem.UsesConstantScreenSize(particleSnapshot))
            {
                SnapshotParticleSystem.SetScreenSize(particleSceneNode, screenSize, Input.Camera.GetFOV());
            }
        }

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);
            Debug.Assert(SelectedNodeRenderer != null);

            AddRenderModeSelectionControl();

            var detailLevelComboBox = UiControl.AddSelection("Detail Level", (_, i) =>
            {
                if (i < 0)
                {
                    return;
                }

                using var lockedGl = MakeCurrent();
                particleSceneNode?.SetDetailLevel((ParticleDetailLevel)i);
                particleSceneNode?.Restart();
            }, horizontal: true, fill: true);
            detailLevelComboBox.Items.AddRange(["Low", "Medium", "High", "Ultra"]);
            detailLevelComboBox.SelectedIndex = (int)ParticleDetailLevel.PARTICLEDETAIL_ULTRA;

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

            endCapButton = new ThemedButton
            {
                Text = "Play Endcap",
                AutoSize = true,
            };
            endCapButton.Click += (_, _) =>
            {
                using var lockedGl = MakeCurrent();
                particleSceneNode?.PlayEndCap();
            };

            using (UiControl.BeginGroup("Playback"))
            {
                UiControl.AddControl(restartButton);
                UiControl.AddControl(endCapButton);

                slowmodeTrackBar = UiControl.AddTrackBar(value =>
                {
                    particleSceneNode?.FrametimeMultiplier = value;
                }, particleSceneNode?.FrametimeMultiplier ?? 1f);
            }

            using (UiControl.BeginGroup("Display"))
            {
                UiControl.AddCheckBox("Show Render Bounds", ShowRenderBounds, value => SelectedNodeRenderer.SelectNode(value ? particleSceneNode : null));

                // Only when the snapshot stores no radius, in which case the preview invents a size.
                if (particleSnapshot != null && SnapshotParticleSystem.UsesConstantScreenSize(particleSnapshot))
                {
                    UiControl.AddControl(RendererControl.CreateFloatInput("Point Size", value =>
                    {
                        screenSize = value / 100f;
                        ApplyScreenSize();
                    }, SnapshotParticleSystem.DefaultScreenSize * 100f, 0.05f, 5f));
                }
            }

            AddOperatorTree();

            base.AddUiControls();
        }

        private void AddOperatorTree()
        {
            Debug.Assert(UiControl != null);

            var unsupportedColor = Color.FromArgb(224, 80, 80);

            // Order matches the CS2 particle editor (PET): pre-emission first, then emit/init/operate,
            // forces, constraints, and renderers last.
            AddFunctionGroup("Pre-Emission Operators", particleSystem.GetPreEmissionOperators(), ParticleSupportInfo.IsPreEmissionOperatorSupported, unsupportedColor);
            AddFunctionGroup("Emitters", particleSystem.GetEmitters(), ParticleSupportInfo.IsEmitterSupported, unsupportedColor);
            AddFunctionGroup("Initializers", particleSystem.GetInitializers(), ParticleSupportInfo.IsInitializerSupported, unsupportedColor);
            AddFunctionGroup("Operators", particleSystem.GetOperators(), ParticleSupportInfo.IsOperatorSupported, unsupportedColor);
            AddFunctionGroup("Force Generators", particleSystem.GetForceGenerators(), ParticleSupportInfo.IsForceGeneratorSupported, unsupportedColor);
            AddFunctionGroup("Constraints", particleSystem.GetConstraints(), ParticleSupportInfo.IsConstraintSupported, unsupportedColor);
            AddFunctionGroup("Renderers", particleSystem.GetRenderers(), ParticleSupportInfo.IsRendererSupported, unsupportedColor);
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
