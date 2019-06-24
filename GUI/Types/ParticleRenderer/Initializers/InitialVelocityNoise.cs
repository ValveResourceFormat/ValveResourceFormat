using System;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class InitialVelocityNoise : IParticleInitializer
    {
        private readonly Vector3 outputMin = Vector3.Zero;
        private readonly Vector3 outputMax = Vector3.One;
        private readonly float noiseScale = 1f;

        public InitialVelocityNoise(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_vecOutputMin"))
            {
                var vectorValues = keyValues.GetArray<double>("m_vecOutputMin");
                outputMin = new Vector3((float)vectorValues[0], (float)vectorValues[1], (float)vectorValues[2]);
            }

            if (keyValues.ContainsKey("m_vecOutputMax"))
            {
                var vectorValues = keyValues.GetArray<double>("m_vecOutputMax");
                outputMax = new Vector3((float)vectorValues[0], (float)vectorValues[1], (float)vectorValues[2]);
            }

            if (keyValues.ContainsKey("m_flNoiseScale"))
            {
                noiseScale = keyValues.GetFloatProperty("m_flNoiseScale");
            }
        }

        public Particle Initialize(Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var r = new Vector3(
                Simplex1D(particleSystemState.Lifetime * noiseScale),
                Simplex1D((particleSystemState.Lifetime * noiseScale) + 101723),
                Simplex1D((particleSystemState.Lifetime * noiseScale) + 555557));

            particle.Velocity = outputMin + (r * (outputMax - outputMin));

            return particle;
        }

        // Simple perlin noise implementation

        private float Simplex1D(float t)
        {
            var previous = PseudoRandom((float)Math.Floor(t));
            var next = PseudoRandom((float)Math.Ceiling(t));

            return CosineInterpolate(previous, next, t % 1f);
        }

        /// <summary>
        /// Yes I know it's not actually a proper LCG but I need it to work without knowing the last value.
        /// </summary>
        private float PseudoRandom(float t)
            => ((1013904223517 * t) % 1664525) / 1664525f;

        private static float CosineInterpolate(float start, float end, float mu)
        {
            var mu2 = (1 - (float)Math.Cos(mu * Math.PI)) / 2f;
            return (start * (1 - mu2)) + (end * mu2);
        }
    }
}
