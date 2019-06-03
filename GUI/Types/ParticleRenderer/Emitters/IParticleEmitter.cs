using System;

namespace GUI.Types.ParticleRenderer.Emitters
{
    public interface IParticleEmitter
    {
        void Start(Action<Particle> particleEmitCallback);

        void Stop();

        void Update(float frameTime);
    }
}
