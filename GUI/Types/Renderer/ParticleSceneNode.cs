using System.Collections.Generic;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    class ParticleSceneNode : SceneNode
    {
        private readonly ParticleRenderer.ParticleRenderer particleRenderer;

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

        public override IEnumerable<string> GetSupportedRenderModes() => particleRenderer.GetSupportedRenderModes();
    }
}
