namespace ValveResourceFormat.Renderer.Particles.Operators
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

        protected CGeneralRandomFade(ParticleDefinitionParser parse, string fadeTimeKeyPrefix) : base(parse)
        {
            fadeTimeMin = parse.Float(fadeTimeKeyPrefix + "Min", fadeTimeMin);
            fadeTimeMax = parse.Float(fadeTimeKeyPrefix + "Max", fadeTimeMax);
            randomExponent = parse.Float(fadeTimeKeyPrefix + "Exp", randomExponent);
            proportional = parse.Boolean("m_bProportional", proportional);
        }

        /// <summary>
        /// The fade duration for this particle, in normalized lifetime or seconds depending on
        /// <see cref="proportional"/>.
        /// </summary>
        protected float GetFadeTime(ref Particle particle)
            => fadeTimeMin == fadeTimeMax
                ? fadeTimeMin
                : ParticleCollection.RandomWithExponentBetween(particle.ParticleID, randomExponent, fadeTimeMin, fadeTimeMax);
    }
}
