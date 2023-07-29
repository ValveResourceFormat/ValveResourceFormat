using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Emitters
{
    class InstantaneousEmitter : IParticleEmitter
    {
        public bool IsFinished { get; private set; }

        private Action particleEmitCallback;

        private readonly INumberProvider emitCount;
        private readonly INumberProvider startTime;

        private float time;

        public InstantaneousEmitter(ParticleDefinitionParser parse)
        {
            emitCount = parse.Data.GetNumberProvider("m_nParticlesToEmit");
            startTime = parse.Data.GetNumberProvider("m_flStartTime");
        }

        public void Start(Action particleEmitCallback)
        {
            this.particleEmitCallback = particleEmitCallback;

            IsFinished = false;

            time = 0;
        }

        public void Stop()
        {
            IsFinished = true;
        }

        public void Update(float frameTime)
        {
            if (IsFinished)
            {
                return;
            }

            time += frameTime;

            if (time >= startTime.NextNumber())
            {
                var numToEmit = (int)emitCount.NextNumber(); // Get value from number provider
                for (var i = 0; i < numToEmit; i++)
                {
                    particleEmitCallback();
                }

                IsFinished = true;
            }
        }
    }
}
