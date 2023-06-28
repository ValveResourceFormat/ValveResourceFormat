using System.Numerics;
using GUI.Utils;
using GUI.Types.ParticleRenderer.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class InitialVelocityNoise : IParticleInitializer
    {
        private readonly IVectorProvider outputMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider outputMax = new LiteralVectorProvider(Vector3.One);
        private readonly INumberProvider noiseScale = new LiteralNumberProvider(1f);

        public InitialVelocityNoise(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_vecOutputMin"))
            {
                outputMin = keyValues.GetVectorProvider("m_vecOutputMin");
            }

            if (keyValues.ContainsKey("m_vecOutputMax"))
            {
                outputMax = keyValues.GetVectorProvider("m_vecOutputMax");
            }

            if (keyValues.ContainsKey("m_flNoiseScale"))
            {
                noiseScale = keyValues.GetNumberProvider("m_flNoiseScale");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var noiseScale = this.noiseScale.NextNumber(particle, particleSystemState);
            var r = new Vector3(
                Noise.Simplex1D(particleSystemState.Age * noiseScale),
                Noise.Simplex1D((particleSystemState.Age * noiseScale) + 101723),
                Noise.Simplex1D((particleSystemState.Age * noiseScale) + 555557));

            var min = outputMin.NextVector(particle, particleSystemState);
            var max = outputMax.NextVector(particle, particleSystemState);

            particle.Velocity = MathUtils.Lerp(r, min, max);

            return particle;
        }
    }
}
