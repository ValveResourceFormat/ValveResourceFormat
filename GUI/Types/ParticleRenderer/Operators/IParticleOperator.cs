using System.Collections.Generic;

namespace GUI.Types.ParticleRenderer.Operators
{
    public interface IParticleOperator
    {
        void Update(IEnumerable<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState);
    }
}
