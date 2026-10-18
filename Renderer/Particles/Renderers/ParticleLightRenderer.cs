using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// A renderer that keeps a scene light on each of up to <c>m_nMaxAllowed</c> particles. The scene
    /// draws the lights; drawing the renderer only lets the next simulation step keep them.
    /// </summary>
    internal abstract class ParticleLightRenderer : ParticleFunctionRenderer
    {
        private readonly int maxAllowed = 32;
        private bool drawn;

        protected ParticleLightRenderer(ParticleDefinitionParser parse, Scene scene) : base(parse)
        {
            Lights = new ParticleLightPool(scene, () => CreateLight(scene));
            maxAllowed = parse.Int32("m_nMaxAllowed", maxAllowed);
        }

        /// <summary>The lights following the particles.</summary>
        protected ParticleLightPool Lights { get; }

        /// <summary>Creates a scene light for the pool, dark until a step gives it a particle.</summary>
        protected abstract SceneLight CreateLight(Scene scene);

        /// <summary>
        /// Claims and fills the slot of each particle that is lit this step, or returns false to leave
        /// every light as it was.
        /// </summary>
        protected abstract bool UpdateLights(ParticleCollection particles, ParticleSystemState systemState);

        /// <inheritdoc/>
        public override void Hide()
        {
            Lights.ReleaseAll();
            Lights.Apply();
        }

        /// <inheritdoc/>
        public override void Act(ParticleCollection particles, ParticleSystemState systemState) => Lights.Apply();

        /// <inheritdoc/>
        /// <remarks>
        /// Lights are kept only by a step that follows a draw, so every step after the first of a frame
        /// split into several puts them all out.
        /// </remarks>
        public override void Simulate(ParticleCollection particles, ParticleSystemState systemState)
        {
            Lights.BeginStep(Math.Min(particles.Count, maxAllowed));

            if (GetOperatorRunStrength(systemState) <= 0f)
            {
                Lights.ReleaseAll();
                return;
            }

            if (drawn && !UpdateLights(particles, systemState))
            {
                return;
            }

            Lights.EndStep();
            drawn = false;
        }

        /// <inheritdoc/>
        public override void Render(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            drawn = true;
        }

        /// <inheritdoc/>
        public override void Delete() => Lights.Delete();
    }
}
