namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Sets the particle alpha to a random value uniformly sampled between a minimum and maximum
    /// (0–255 integer range, stored normalised). Corresponds to <c>C_INIT_RandomAlpha</c>.
    /// </summary>
    class RandomAlpha : ParticleFunctionInitializer
    {
        private readonly int alphaMin = 255;
        private readonly int alphaMax = 255;

        public RandomAlpha(ParticleDefinitionParser parse) : base(parse)
        {
            alphaMin = parse.Int32("m_nAlphaMin", alphaMin);
            alphaMax = parse.Int32("m_nAlphaMax", alphaMax);

            MathUtils.MinMaxFixUp(ref alphaMin, ref alphaMax);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var alpha = ParticleCollection.RandomBetween(particle.ParticleID, alphaMin, alphaMax) / 255f;

            particle.Alpha = alpha;

            return particle;
        }
    }
}
