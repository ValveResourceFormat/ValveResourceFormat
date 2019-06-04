using System.Collections.Generic;
using OpenTK;

namespace GUI.Types.ParticleRenderer.Renderers
{
    public interface IParticleRenderer
    {
        void Render(IEnumerable<Particle> particles, Matrix4 projectionMatrix, Matrix4 modelViewMatrix);
    }
}
