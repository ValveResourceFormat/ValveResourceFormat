using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class Decay : IParticleOperator
    {
        public Decay()
        {
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            for (var i = 0; i < particles.Length; ++i)
            {
                if (particles[i].Age > particles[i].Lifetime)
                {
                    particles[i].Kill();
                }
            }
        }
    }
}
