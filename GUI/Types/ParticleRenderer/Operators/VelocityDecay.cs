using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    /// <summary>
    /// Cull particle when its velocity is below a certain threshold.
    /// </summary>
    class VelocityDecay : IParticleOperator
    {
        private readonly float minVelocity;
        public VelocityDecay(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flMinVelocity"))
            {
                minVelocity = keyValues.GetFloatProperty("m_flMinVelocity");
            }
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                if (particle.Speed <= minVelocity)
                {
                    particle.Kill();
                }
            }
        }
    }
}
