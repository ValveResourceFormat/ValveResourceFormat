namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Initializes a scalar particle attribute to a random value between a min and max, with an optional exponent bias.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RandomScalar">C_INIT_RandomScalar</seealso>
    class RandomScalar : ParticleFunctionInitializer
    {
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly float scalarMin;
        private readonly float scalarMax;
        private readonly float exponent = 1;

        public RandomScalar(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            scalarMin = parse.Float("m_flMin", scalarMin);
            scalarMax = parse.Float("m_flMax", scalarMax);
            exponent = parse.Float("m_flExponent", exponent);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var value = particleSystemState.Random.NextWithExponentBetween(exponent, scalarMin, scalarMax);

            particle.SetScalar(fieldOutput, value);

            return particle;
        }
    }
}
