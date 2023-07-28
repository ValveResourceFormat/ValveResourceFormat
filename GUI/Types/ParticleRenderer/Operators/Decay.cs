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
            foreach (ref var particle in particles)
            {
                if (particle.Age > particle.Lifetime)
                {
                    particle.Kill();
                }
            }
        }
    }
}
