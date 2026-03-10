using ValveResourceFormat.Renderer.Particles;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Scene node that renders particle system effects.
    /// </summary>
    public class ParticleSceneNode : SceneNode
    {
        private readonly ParticleRenderer particleRenderer;

        /// <summary>Gets or sets a time-scale multiplier applied to the particle simulation each frame.</summary>
        public float FrametimeMultiplier { get; set; } = 1.0f;

        /// <summary>
        /// Initializes a new instance of the <see cref="ParticleSceneNode"/> class.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="particleSystem">The particle system resource to simulate and render.</param>
        public ParticleSceneNode(Scene scene, ParticleSystem particleSystem)
            : base(scene)
        {
            particleRenderer = new ParticleRenderer(particleSystem, Scene.RendererContext);
            LocalBoundingBox = particleRenderer.LocalBoundingBox;
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override void Render(Scene.RenderContext context)
        {
            if (context.RenderPass != RenderPass.Translucent || context.ReplacementShader is not null)
            {
                return;
            }

            particleRenderer.Render(context.Camera);
        }

        /// <inheritdoc/>
        public override IEnumerable<string> GetSupportedRenderModes() => particleRenderer.GetSupportedRenderModes();

        /// <inheritdoc/>
        public override void SetRenderMode(string mode) => particleRenderer.SetRenderMode(mode);
    }
}
