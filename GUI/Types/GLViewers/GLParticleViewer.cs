using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Particles;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ParticleUpgrade;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// Renders a single <see cref="ParticleSystem"/> via a <see cref="ParticleSceneNode"/>, with UI controls for playback and an operator/renderer tree.
    /// </summary>
    class GLParticleViewer : GLSceneViewer
    {
        private static readonly Color UnsupportedColor = Color.FromArgb(224, 80, 80);
        private static readonly Color RemovedColor = Color.FromArgb(140, 140, 140);
        private static readonly Color LinkColorDark = Color.FromArgb(99, 161, 255);
        private static readonly Color LinkColorLight = Color.FromArgb(0, 102, 204);
        private static readonly Color DisabledLinkColorDark = Color.FromArgb(122, 138, 160);
        private static readonly Color DisabledLinkColorLight = Color.FromArgb(112, 128, 148);

        // Order matches the CS2 particle editor (PET): pre-emission first, then emit/init/operate,
        // forces, constraints, and renderers last.
        private static readonly (string Title, string ListName, Func<string, bool> IsSupported)[] FunctionGroups =
        [
            ("Pre-Emission Operators", "m_PreEmissionOperators", ParticleSupportInfo.IsPreEmissionOperatorSupported),
            ("Emitters", "m_Emitters", ParticleSupportInfo.IsEmitterSupported),
            ("Initializers", "m_Initializers", ParticleSupportInfo.IsInitializerSupported),
            ("Operators", "m_Operators", ParticleSupportInfo.IsOperatorSupported),
            ("Force Generators", "m_ForceGenerators", ParticleSupportInfo.IsForceGeneratorSupported),
            ("Constraints", "m_Constraints", ParticleSupportInfo.IsConstraintSupported),
            ("Renderers", "m_Renderers", ParticleSupportInfo.IsRendererSupported),
        ];

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

            var functionLists = particleSystem.GetUpgradeTrace();

            foreach (var (title, listName, isSupported) in FunctionGroups)
            {
                AddFunctionGroup(title, functionLists[listName], isSupported);
            }

            AddChildList();
        }

        private void AddChildList()
        {
            Debug.Assert(UiControl != null);

            var upgradedData = particleSystem.GetUpgradedData();
            var children = new List<ChildSystemItem>();

            foreach (var childInfo in upgradedData.GetArray("m_Children") ?? [])
            {
                var childRef = childInfo.GetStringProperty("m_ChildRef");

                if (string.IsNullOrEmpty(childRef))
                {
                    continue;
                }

                var disabled = !particleSystem.IsChildEnabled(childInfo);
                var shortName = Path.GetFileNameWithoutExtension(childRef);
                children.Add(new ChildSystemItem(disabled ? $"{shortName} (disabled)" : shortName, childRef, disabled));
            }

            if (children.Count == 0)
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
                Cursor = Cursors.Hand,
            };

            listBox.Items.AddRange([.. children]);

            var darkMode = Application.IsDarkModeEnabled;
            var linkColor = darkMode ? LinkColorDark : LinkColorLight;
            var disabledLinkColor = darkMode ? DisabledLinkColorDark : DisabledLinkColorLight;
            Font? linkFont = null;

            listBox.DrawItem += (_, e) =>
            {
                if (e.Index < 0)
                {
                    return;
                }

                using var brush = new SolidBrush(listBox.BackColor);
                e.Graphics.FillRectangle(brush, e.Bounds);

                linkFont ??= new Font(e.Font!, FontStyle.Underline);

                var item = (ChildSystemItem)listBox.Items[e.Index];
                var color = item.Disabled ? disabledLinkColor : linkColor;

                System.Windows.Forms.TextRenderer.DrawText(e.Graphics, item.Text, linkFont, e.Bounds, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            };

            listBox.Disposed += (_, _) => linkFont?.Dispose();

            listBox.MouseClick += (_, e) =>
            {
                if (e.Button != MouseButtons.Left)
                {
                    return;
                }

                var index = listBox.IndexFromPoint(e.Location);

                if (index >= 0)
                {
                    Viewers.Resource.OpenExternalReference(GuiContext, ((ChildSystemItem)listBox.Items[index]).ChildRef);
                }
            };

            Themer.ThemeControl(listBox);

            listBox.Height = listBox.ItemHeight * children.Count + 2;

            using (UiControl.BeginGroup("Children"))
            {
                UiControl.AddControl(listBox);
            }
        }

        private void AddFunctionGroup(string groupName, IReadOnlyList<ParticleUpgradeTrace.TracedFunction> functions, Func<string, bool> isSupported)
        {
            Debug.Assert(UiControl != null);

            if (functions.Count == 0)
            {
                return;
            }

            using (UiControl.BeginGroup(groupName))
            {
                UiControl.AddControl(BuildFunctionList(functions, isSupported));
            }
        }

        private static ListBox BuildFunctionList(IReadOnlyList<ParticleUpgradeTrace.TracedFunction> functions, Func<string, bool> isSupported)
        {
            var listBox = new ListBox
            {
                Dock = DockStyle.Fill,
                DrawMode = DrawMode.OwnerDrawFixed,
                BorderStyle = BorderStyle.None,
                SelectionMode = SelectionMode.None,
                IntegralHeight = false,
            };

            foreach (var function in functions)
            {
                var displayName = StripClassPrefix(function.Class);

                if (function.RemovedByUpgrade)
                {
                    listBox.Items.Add(new ParticleFunctionItem($"{displayName} (removed by upgrade)", FunctionSupport.Removed));
                    continue;
                }

                if (function.OriginalClass != null)
                {
                    displayName = $"{displayName} (was {StripClassPrefix(function.OriginalClass)})";
                }

                var support = isSupported(function.Class) ? FunctionSupport.Supported : FunctionSupport.Unsupported;
                listBox.Items.Add(new ParticleFunctionItem(displayName, support));
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
                var color = item.Support switch
                {
                    FunctionSupport.Unsupported => UnsupportedColor,
                    FunctionSupport.Removed => RemovedColor,
                    _ => listBox.ForeColor,
                };

                System.Windows.Forms.TextRenderer.DrawText(e.Graphics, item.Text, e.Font, e.Bounds, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            };

            Themer.ThemeControl(listBox);

            listBox.Height = listBox.ItemHeight * functions.Count + 2;

            return listBox;
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

        private enum FunctionSupport
        {
            Supported,
            Unsupported,
            Removed,
        }

        private sealed record ParticleFunctionItem(string Text, FunctionSupport Support);

        private sealed record ChildSystemItem(string Text, string ChildRef, bool Disabled);

        protected override void OnPicked(object? sender, PickingTexture.PickingResponse pixelInfo)
        {
            //
        }
    }
}
