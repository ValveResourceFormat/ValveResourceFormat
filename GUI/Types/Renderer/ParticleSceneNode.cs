using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    internal class ParticleSceneNode : SceneNode
    {
        private ParticleRenderer.ParticleRenderer particleRenderer;

        public ParticleSceneNode(Scene scene, ParticleSystem particleSystem)
            : base(scene)
        {
            particleRenderer = new ParticleRenderer.ParticleRenderer(particleSystem, Scene.GuiContext);
            LocalBoundingBox = particleRenderer.BoundingBox;
        }

        public override void Update(Scene.UpdateContext context)
        {
            particleRenderer.Position = Transform.Translation;
            particleRenderer.Update(context.Timestep);

            LocalBoundingBox = particleRenderer.BoundingBox.Translate(-particleRenderer.Position);
        }

        public override void Render(Scene.RenderContext context)
        {
            particleRenderer.Render(context.Camera, context.RenderPass);
        }
    }
}
