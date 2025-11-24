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
        private GLViewerTrackBarControl slowmodeTrackBar;

        private bool ShowRenderBounds { get; set; }

        public GLParticleViewer(VrfGuiContext guiContext, ParticleSystem particleSystem) : base(guiContext, Frustum.CreateEmpty())
        {
            this.particleSystem = particleSystem;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                slowmodeTrackBar?.Dispose();
            }
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

            Camera.SetLocation(new Vector3(200, 200, 200));
            Camera.LookAt(Vector3.Zero);
        }

        protected override void InitializeControl()
        {
            AddRenderModeSelectionControl();
            AddBaseGridControl();

            slowmodeTrackBar = AddTrackBar(value =>
            {
                particleSceneNode.FrametimeMultiplier = value / 100f;
            });
            slowmodeTrackBar.TrackBar.TickFrequency = 10;
            slowmodeTrackBar.TrackBar.Minimum = 0;
            slowmodeTrackBar.TrackBar.Maximum = 100;
            slowmodeTrackBar.TrackBar.Value = 100;

            AddCheckBox("Show render bounds", ShowRenderBounds, value => SelectedNodeRenderer.SelectNode(value ? particleSceneNode : null));
        }

        protected override void OnPicked(object sender, PickingTexture.PickingResponse pixelInfo)
        {
            //
        }
    }
}
