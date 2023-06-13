using System;

namespace GUI.Types.ParticleRenderer.Operators
{
    interface IParticleOperator
    {
        void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState);
    }
}
