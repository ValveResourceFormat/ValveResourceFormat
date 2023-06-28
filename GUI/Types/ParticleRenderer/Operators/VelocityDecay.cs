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
            for (var i = 0; i < particles.Length; ++i)
            {
                if (particles[i].Speed <= minVelocity)
                {
                    particles[i].Kill();
                }
            }
        }
    }
}
