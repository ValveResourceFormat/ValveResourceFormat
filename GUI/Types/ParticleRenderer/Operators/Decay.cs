using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    public class Decay : IParticleOperator
    {
        public Decay(IKeyValueCollection keyValues)
        {
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            for (var i = 0; i < particles.Length; ++i)
            {
                particles[i].Lifetime -= frameTime;
            }
        }
    }
}
