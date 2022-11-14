using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    public class Decay : IParticleOperator
    {
#pragma warning disable CA1801
        public Decay(IKeyValueCollection keyValues)
        {
        }
#pragma warning restore CA1801

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            for (var i = 0; i < particles.Length; ++i)
            {
                particles[i].Lifetime -= frameTime;
            }
        }
    }
}
