using System;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class RingWave : IParticleInitializer
    {
        private readonly bool evenDistribution;
        private readonly INumberProvider initialRadius = new LiteralNumberProvider(0);
        private readonly INumberProvider thickness = new LiteralNumberProvider(1);
        private readonly float particlesPerOrbit = -1f;
        private float orbitCount;

        public RingWave(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_bEvenDistribution"))
            {
                evenDistribution = keyValues.GetProperty<bool>("m_bEvenDistribution");
            }

            if (keyValues.ContainsKey("m_flParticlesPerOrbit"))
            {
                particlesPerOrbit = keyValues.GetFloatProperty("m_flParticlesPerOrbit");
            }

            if (keyValues.ContainsKey("m_flInitialRadius"))
            {
                initialRadius = keyValues.GetNumberProvider("m_flInitialRadius");
            }

            if (keyValues.ContainsKey("m_flThickness"))
            {
                thickness = keyValues.GetNumberProvider("m_flThickness");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var radius = initialRadius.NextNumber() + (Random.Shared.NextDouble() * thickness.NextNumber());

            var angle = GetNextAngle();

            particle.Position += (float)radius * new Vector3((float)Math.Cos(angle), (float)Math.Sin(angle), 0);

            return particle;
        }

        private double GetNextAngle()
        {
            if (evenDistribution)
            {
                var offset = orbitCount / particlesPerOrbit;

                orbitCount = (orbitCount + 1) % particlesPerOrbit;

                return offset * 2 * Math.PI;
            }
            else
            {
                // Return a random angle between 0 and 2pi
                return 2 * Math.PI * Random.Shared.NextDouble();
            }
        }
    }
}
