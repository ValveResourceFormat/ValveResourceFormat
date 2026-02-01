using ValveResourceFormat.Renderer.Particles;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Scene node that renders particle system effects.
    /// </summary>
    public class ParticleSceneNode : SceneNode
    {
        private readonly ParticleRenderer particleRenderer;
        public float FrametimeMultiplier { get; set; } = 1.0f;

        public ParticleSceneNode(Scene scene, ParticleSystem particleSystem)
            : base(scene)
        {
            particleRenderer = new ParticleRenderer(particleSystem, Scene.RendererContext);
            LocalBoundingBox = particleRenderer.LocalBoundingBox;
        }

        public override void Update(Scene.UpdateContext context)
        {
            if (!LayerEnabled)
            {
                return;
            }

            particleRenderer.MainControlPoint.Position = Transform.Translation;
            particleRenderer.Update(context.Timestep * FrametimeMultiplier);
            LocalBoundingBox = particleRenderer.LocalBoundingBox;

            // Restart if all emitters are done and all particles expired
            if (particleRenderer.IsFinished())
            {
                particleRenderer.Restart();
            }
        }

        public override void Render(Scene.RenderContext context)
        {
            if (context.RenderPass != RenderPass.Translucent || context.ReplacementShader is not null)
            {
                return;
            }

            particleRenderer.Render(context.Camera);
        }

        public override IEnumerable<string> GetSupportedRenderModes() => particleRenderer.GetSupportedRenderModes();

        public override void SetRenderMode(string mode) => particleRenderer.SetRenderMode(mode);
    }
}
