using System;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RingWave : IParticleInitializer
    {
        private readonly bool evenDistribution;
        private readonly INumberProvider initialRadius = new LiteralNumberProvider(0);
        private readonly INumberProvider thickness = new LiteralNumberProvider(0);
        private readonly INumberProvider particlesPerOrbit = new LiteralNumberProvider(-1);

        private float orbitCount;

        public RingWave(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_bEvenDistribution"))
            {
                evenDistribution = keyValues.GetProperty<bool>("m_bEvenDistribution");
            }

            if (keyValues.ContainsKey("m_flParticlesPerOrbit"))
            {
                particlesPerOrbit = keyValues.GetNumberProvider("m_flParticlesPerOrbit");
            }

            if (keyValues.ContainsKey("m_flInitialRadius"))
            {
                initialRadius = keyValues.GetNumberProvider("m_flInitialRadius");
            }

            if (keyValues.ContainsKey("m_flThickness"))
            {
                thickness = keyValues.GetNumberProvider("m_flThickness");
            }

            // other properties: m_vInitialSpeedMin/Max, m_flRoll
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var thickness = this.thickness.NextNumber(particle, particleSystemState);
            var particlesPerOrbit = this.particlesPerOrbit.NextInt(particle, particleSystemState);

            var radius = initialRadius.NextNumber(particle, particleSystemState) + (Random.Shared.NextSingle() * thickness);

            var angle = GetNextAngle(particlesPerOrbit);

            particle.Position += radius * new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);

            return particle;
        }

        private float GetNextAngle(int particlesPerOrbit)
        {
            if (evenDistribution)
            {
                var offset = orbitCount / particlesPerOrbit;

                orbitCount = (orbitCount + 1) % particlesPerOrbit;

                return offset * 2 * MathF.PI;
            }
            else
            {
                // Return a random angle between 0 and 2pi
                return 2 * MathF.PI * Random.Shared.NextSingle();
            }
        }
    }
}
