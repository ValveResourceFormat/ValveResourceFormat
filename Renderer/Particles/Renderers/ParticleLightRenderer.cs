using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// A renderer that keeps a scene light on each of up to <see cref="LightCount"/> particles. The
    /// scene draws the lights; drawing the renderer only lets the next simulation step keep them.
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

        /// <summary>
        /// How many lights the pool holds for a step with <paramref name="particleCount"/> particles,
        /// by default as many as there are particles, up to <c>m_nMaxAllowed</c>.
        /// </summary>
        protected virtual int LightCount(int particleCount) => Math.Min(particleCount, maxAllowed);

        /// <summary>
        /// Creates a scene light for the pool with the settings that stay fixed. The pool sets the colour,
        /// brightness, range, cone, luminaire size, falloff and placement from its slot every frame.
        /// </summary>
        protected abstract SceneLight CreateLight(Scene scene);

        /// <summary>
        /// Claims and fills the slot of each particle that is lit this step, or returns false to leave
        /// every light as it was.
        /// </summary>
        protected abstract bool UpdateLights(ParticleCollection particles, ParticleSystemState systemState);

        /// <inheritdoc/>
        public override void Hide(ParticleCollection particles)
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
            Lights.BeginStep(LightCount(particles.Count));

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
            (VisibleAlpha, VisibleRadius) = EvaluateControlPointVisibility(systemState, camera);
            drawn = true;
        }

        /// <summary>
        /// The light colour scale from the visibility control point as the last draw saw it, 1 without one.
        /// </summary>
        protected float VisibleAlpha { get; private set; } = 1f;

        /// <summary>
        /// The light range scale from the visibility control point as the last draw saw it, 1 without one.
        /// </summary>
        protected float VisibleRadius { get; private set; } = 1f;

        /// <summary>Finds the named light style of the scene's game, or null when the name is empty or unknown.</summary>
        protected static CurveLightStyle? FindLightStyle(Scene scene, string name)
        {
            if (name.Length == 0)
            {
                return null;
            }

            scene.LightStyles.TryGetValue(name, out var style);
            return style;
        }

        /// <inheritdoc/>
        public override void Delete() => Lights.Delete();
    }
}
