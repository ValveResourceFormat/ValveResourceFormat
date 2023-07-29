using System.Numerics;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with particle controls (control points? particle counts?).
    /// Renders a list of ParticleRenderers.
    /// </summary>
    class GLParticleViewer : GLSceneViewer
    {
        private ParticleSystem particleSystem;
        private ParticleSceneNode particleSceneNode;
        private GLViewerTrackBarControl slowmodeTrackBar;

        public GLParticleViewer(VrfGuiContext guiContext, ParticleSystem particleSystem) : base(guiContext, Frustum.CreateEmpty())
        {
            this.particleSystem = particleSystem;
        }

        // splash07a
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                slowmodeTrackBar?.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void LoadScene()
        {
            particleSceneNode = new ParticleSceneNode(Scene, particleSystem)
            {
                Transform = Matrix4x4.Identity
            };
            Scene.Add(particleSceneNode, true);
        }

        protected override void InitializeControl()
        {
            AddRenderModeSelectionControl();

            slowmodeTrackBar = AddTrackBar(value =>
            {
                particleSceneNode.FrametimeMultiplier = value / 100f;
            });
            slowmodeTrackBar.TrackBar.TickFrequency = 10;
            slowmodeTrackBar.TrackBar.Minimum = 0;
            slowmodeTrackBar.TrackBar.Maximum = 100;
            slowmodeTrackBar.TrackBar.Value = 100;
        }

        protected override void OnPicked(object sender, PickingTexture.PickingResponse pixelInfo)
        {
            //
        }
    }
}
