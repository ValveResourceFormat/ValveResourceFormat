namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    class RandomScalar : ParticleFunctionInitializer
    {
        private readonly ParticleField FieldOutput = ParticleField.Radius;
        private readonly float scalarMin;
        private readonly float scalarMax;
        private readonly float exponent = 1;

        public RandomScalar(ParticleDefinitionParser parse) : base(parse)
        {
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            scalarMin = parse.Float("m_flMin", scalarMin);
            scalarMax = parse.Float("m_flMax", scalarMax);
            scalarMax = parse.Float("m_flExponent", scalarMax);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var value = ParticleCollection.RandomWithExponentBetween(particle.ParticleID, exponent, scalarMin, scalarMax);

            particle.SetScalar(FieldOutput, value);

            return particle;
        }
    }
}
