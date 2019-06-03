using System.Collections.Generic;

namespace GUI.Types.ParticleRenderer.Renderers
{
    public interface IParticleRenderer
    {
        void Render(IEnumerable<Particle> particles);
    }
}
