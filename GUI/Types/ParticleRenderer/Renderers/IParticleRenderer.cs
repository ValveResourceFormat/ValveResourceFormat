using System.Collections.Generic;
using System.Numerics;

namespace GUI.Types.ParticleRenderer.Renderers
{
    interface IParticleRenderer
    {
        void Render(ParticleBag particles, ParticleSystemRenderState systemRenderState, Matrix4x4 viewProjectionMatrix, Matrix4x4 modelViewMatrix);
        void SetRenderMode(string renderMode);
        IEnumerable<string> GetSupportedRenderModes();
        void SetWireframe(bool wireframe);
    }
}
