using System;

namespace GUI.Types.ParticleRenderer.Emitters
{
    public interface IParticleEmitter
    {
        void Start(Action particleEmitCallback);

#pragma warning disable CA1716 // Identifiers should not match keywords
        void Stop();
#pragma warning restore CA1716 // Identifiers should not match keywords

        void Update(float frameTime);

        bool IsFinished { get; }
    }
}
