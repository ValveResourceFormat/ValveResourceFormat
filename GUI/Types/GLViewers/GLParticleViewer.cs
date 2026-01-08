using GUI.Controls;
using GUI.Types.Renderer;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;

#nullable disable

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// GL Render control with particle controls (control points? particle counts?).
    /// Renders a list of ParticleRenderers.
    /// </summary>
    class GLParticleViewer : GLSceneViewer
    {
        private readonly ParticleSystem particleSystem;
        private ParticleSceneNode particleSceneNode;
        private GLViewerSliderControl slowmodeTrackBar;

        private bool ShowRenderBounds { get; set; }

        public GLParticleViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, ParticleSystem particleSystem) : base(vrfGuiContext, rendererContext, Frustum.CreateEmpty())
        {
            this.particleSystem = particleSystem;
        }

        public override void Dispose()
        {
            base.Dispose();

            slowmodeTrackBar?.Dispose();
        }

        protected override void LoadScene()
        {
            particleSceneNode = new ParticleSceneNode(Scene, particleSystem)
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
            AddRenderModeSelectionControl();
            AddBaseGridControl();

            slowmodeTrackBar = UiControl.AddTrackBar(value =>
            {
                particleSceneNode.FrametimeMultiplier = value;
            });

            UiControl.AddCheckBox("Show render bounds", ShowRenderBounds, value => SelectedNodeRenderer.SelectNode(value ? particleSceneNode : null));

            base.AddUiControls();
        }

        protected override void OnPicked(object sender, PickingTexture.PickingResponse pixelInfo)
        {
            //
        }
    }
}
