using System;
using System.Collections.Generic;
using System.Numerics;
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

        public GLParticleViewer(VrfGuiContext guiContext, ParticleSystem particleSystem) : base(guiContext, Frustum.CreateEmpty())
        {
            this.particleSystem = particleSystem;
        }

        protected override void LoadScene()
        {
            var particleNode = new ParticleSceneNode(Scene, particleSystem)
            {
                Transform = Matrix4x4.Identity
            };
            Scene.Add(particleNode, true);
        }

        protected override void InitializeControl()
        {
            AddRenderModeSelectionControl();
        }

        protected override void OnPicked(object sender, PickingTexture.PickingResponse pixelInfo)
        {
            //
        }
    }
}
