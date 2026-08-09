namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Sets a scalar particle field to a random value between a minimum and a maximum, authored over the
    /// 0-255 integer range and stored normalised. An exponent shapes the distribution across the range.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RandomAlpha">C_INIT_RandomAlpha</seealso>
    class RandomAlpha : ParticleFunctionInitializer
    {
        private readonly ParticleField outputField = ParticleField.Alpha;
        private readonly float alphaMin = 1f;
        private readonly float alphaMax = 1f;
        private readonly float alphaExponent = 1f;

        public RandomAlpha(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            alphaMin = parse.Int32("m_nAlphaMin", 255) / 255f;
            alphaMax = parse.Int32("m_nAlphaMax", 255) / 255f;
            alphaExponent = Math.Clamp(parse.Float("m_flAlphaRandExponent", alphaExponent), -255f, 255f);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            particle.SetScalar(outputField, particleSystemState.Random.NextWithExponentBetween(alphaExponent, alphaMin, alphaMax));

            return particle;
        }
    }
}
