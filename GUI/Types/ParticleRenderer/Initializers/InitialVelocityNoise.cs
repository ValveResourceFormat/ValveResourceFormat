using GUI.Types.ParticleRenderer.Utils;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class InitialVelocityNoise : ParticleFunctionInitializer
    {
        private readonly IVectorProvider outputMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider outputMax = new LiteralVectorProvider(Vector3.One);
        private readonly INumberProvider noiseScale = new LiteralNumberProvider(1f);

        public InitialVelocityNoise(ParticleDefinitionParser parse) : base(parse)
        {
            outputMin = parse.VectorProvider("m_vecOutputMin", outputMin);
            outputMax = parse.VectorProvider("m_vecOutputMax", outputMax);
            noiseScale = parse.NumberProvider("m_flNoiseScale", noiseScale);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var noiseScale = this.noiseScale.NextNumber(ref particle, particleSystemState);
            var r = new Vector3(
                Noise.Simplex1D(particleSystemState.Age * noiseScale),
                Noise.Simplex1D((particleSystemState.Age * noiseScale) + 101723),
                Noise.Simplex1D((particleSystemState.Age * noiseScale) + 555557));

            var min = outputMin.NextVector(ref particle, particleSystemState);
            var max = outputMax.NextVector(ref particle, particleSystemState);

            particle.Velocity = Vector3.Lerp(min, max, r);

            return particle;
        }
    }
}
