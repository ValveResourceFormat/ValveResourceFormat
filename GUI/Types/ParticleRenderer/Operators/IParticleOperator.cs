using System;

namespace GUI.Types.ParticleRenderer.Operators
{
    interface IParticleOperator
    {
        void Update(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState);
    }
}
