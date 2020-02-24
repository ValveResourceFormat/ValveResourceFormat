using System;
using System.Collections.Generic;
using OpenTK;

namespace GUI.Types.ParticleRenderer.Renderers
{
    public interface IParticleRenderer
    {
        void Render(ParticleBag particles, Matrix4 projectionMatrix, Matrix4 modelViewMatrix);
    }
}
