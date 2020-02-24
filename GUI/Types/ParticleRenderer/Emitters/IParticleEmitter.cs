using System;

namespace GUI.Types.ParticleRenderer.Emitters
{
    public interface IParticleEmitter
    {
        void Start(Action particleEmitCallback);

        void Stop();

        void Update(float frameTime);

        bool IsFinished { get; }
    }
}
