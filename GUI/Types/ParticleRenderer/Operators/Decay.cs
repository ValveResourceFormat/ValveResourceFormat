using System;

namespace GUI.Types.ParticleRenderer.Operators
{
    class Decay : IParticleOperator
    {
        public Decay()
        {
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            // noop, we always tick down lifetime for all particles
        }
    }
}
