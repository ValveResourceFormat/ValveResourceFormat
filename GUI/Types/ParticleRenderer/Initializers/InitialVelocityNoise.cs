using System;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class InitialVelocityNoise : IParticleInitializer
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
            var noiseScale = (float)this.noiseScale.NextNumber();
            var r = new Vector3(
                Simplex1D(particleSystemState.Lifetime * noiseScale),
                Simplex1D((particleSystemState.Lifetime * noiseScale) + 101723),
                Simplex1D((particleSystemState.Lifetime * noiseScale) + 555557));

            var min = outputMin.NextVector();
            var max = outputMax.NextVector();

            particle.Velocity = min + (r * (max - min));

            return particle;
        }

        // Simple perlin noise implementation

        private static float Simplex1D(float t)
        {
            var previous = PseudoRandom((float)Math.Floor(t));
            var next = PseudoRandom((float)Math.Ceiling(t));

            return CosineInterpolate(previous, next, t % 1f);
        }

        /// <summary>
        /// Yes I know it's not actually a proper LCG but I need it to work without knowing the last value.
        /// </summary>
        private static float PseudoRandom(float t)
            => ((1013904223517 * t) % 1664525) / 1664525f;

        private static float CosineInterpolate(float start, float end, float mu)
        {
            var mu2 = (1 - (float)Math.Cos(mu * Math.PI)) / 2f;
            return (start * (1 - mu2)) + (end * mu2);
        }
    }
}
