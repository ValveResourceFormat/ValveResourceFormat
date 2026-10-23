using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Base class for fade operators whose fade duration is drawn per particle from a min/max
    /// range with an exponent bias.
    /// </summary>
    abstract class CGeneralRandomFade : ParticleFunctionOperator
    {
        private readonly float fadeTimeMin = 0.25f;
        private readonly float fadeTimeMax = 0.25f;
        private readonly float randomExponent = 1f;

        /// <summary>Whether times are expressed as a fraction of the particle's lifetime rather than seconds.</summary>
        protected readonly bool proportional = true;

        /// <param name="parse">The function's definition.</param>
        /// <param name="fadeTimeKeyPrefix">The key prefix of the fade time range and exponent.</param>
        /// <param name="randomFromZeroIsInstant">
        /// Whether a random range starting at 0 collapses to a fade of <see cref="ParticleMath.FloatEpsilon"/>.
        /// </param>
        protected CGeneralRandomFade(ParticleDefinitionParser parse, string fadeTimeKeyPrefix, bool randomFromZeroIsInstant = false) : base(parse)
        {
            fadeTimeMin = parse.Float(fadeTimeKeyPrefix + "Min", fadeTimeMin);
            fadeTimeMax = parse.Float(fadeTimeKeyPrefix + "Max", fadeTimeMax);
            randomExponent = parse.Float(fadeTimeKeyPrefix + "Exp", randomExponent);
            proportional = parse.Boolean("m_bProportional", proportional);

            if (randomFromZeroIsInstant && fadeTimeMin != fadeTimeMax && fadeTimeMin == 0f)
            {
                fadeTimeMin = ParticleMath.FloatEpsilon;
                fadeTimeMax = ParticleMath.FloatEpsilon;
            }
        }

        /// <summary>
        /// Fade duration for this particle, as a fraction of lifetime or in seconds per
        /// <see cref="proportional"/>. Constant across frames.
        /// </summary>
        protected float GetFadeTime(Particle particle, ParticleSystemState particleSystemState)
        {
            if (fadeTimeMin == fadeTimeMax)
            {
                return fadeTimeMin;
            }

            var draw = ParticleMath.PowQuarterSteps(particleSystemState.Random.ForParticle(particle.ParticleId), randomExponent);

            return (draw * (fadeTimeMax - fadeTimeMin)) + fadeTimeMin;
        }
    }
}
