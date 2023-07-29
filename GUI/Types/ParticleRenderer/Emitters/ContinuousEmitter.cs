using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Emitters
{
    class ContinuousEmitter : IParticleEmitter
    {
        public bool IsFinished { get; private set; }

        private readonly INumberProvider emissionDuration = new LiteralNumberProvider(0);
        private readonly INumberProvider startTime = new LiteralNumberProvider(0);
        private readonly INumberProvider emitRate = new LiteralNumberProvider(100);
        private readonly float emitInterval = 0.01f;

        private Action particleEmitCallback;

        private float time;
        private float lastEmissionTime;

        public ContinuousEmitter(ParticleDefinitionParser parse)
        {
            emissionDuration = parse.NumberProvider("m_flEmissionDuration", emissionDuration);

            startTime = parse.NumberProvider("m_flStartTime", startTime);

            if (parse.Data.ContainsKey("m_flEmitRate"))
            {
                emitRate = parse.Data.GetNumberProvider("m_flEmitRate");
                emitInterval = 1.0f / emitRate.NextNumber();
            }
        }

        public void Start(Action particleEmitCallback)
        {
            this.particleEmitCallback = particleEmitCallback;

            time = 0f;
            lastEmissionTime = 0;

            IsFinished = false;
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

            var nextStartTime = startTime.NextNumber();
            var nextEmissionDuration = emissionDuration.NextNumber();

            if (time >= nextStartTime && (nextEmissionDuration == 0f || time <= nextStartTime + nextEmissionDuration))
            {
                var numToEmit = (int)MathF.Floor((time - lastEmissionTime) / emitInterval);
                var emitCount = Math.Min(5 * (int)emitRate.NextNumber(), numToEmit); // Limit the amount of particles to emit at once in case of refocus
                for (var i = 0; i < emitCount; i++)
                {
                    particleEmitCallback();
                }

                lastEmissionTime += numToEmit * emitInterval;
            }

            if (nextEmissionDuration != 0f && time > nextStartTime + nextEmissionDuration)
            {
                IsFinished = true;
            }
        }
    }
}
