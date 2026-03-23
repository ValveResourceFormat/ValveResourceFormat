using ValveResourceFormat.Blocks;
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
        /// <param name="particleSnapshot">Optional snapshot to provide initial particle data (e.g. from a map entity).</param>
        public ParticleSceneNode(Scene scene, ParticleSystem particleSystem, ParticleSnapshot? particleSnapshot = null)
            : base(scene)
        {
            particleRenderer = new ParticleRenderer(particleSystem, Scene.RendererContext, particleSnapshot);
            LocalBoundingBox = particleRenderer.LocalBoundingBox;
        }

        /// <summary>
        /// Restarts the particle system from the beginning.
        /// </summary>
        public void Restart() => particleRenderer.Restart();

        /// <inheritdoc/>
        public override void Update(Scene.UpdateContext context)
        {
            if (!LayerEnabled)
            {
                return;
            }

            particleRenderer.MainControlPoint.Position = Transform.Translation;

            var frameTime = context.Timestep * FrametimeMultiplier;

            if (frameTime > 0f)
            {
                particleRenderer.Update(frameTime);
                LocalBoundingBox = particleRenderer.LocalBoundingBox;
            }

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
