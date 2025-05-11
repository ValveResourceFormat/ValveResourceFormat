namespace GUI.Types.ParticleRenderer.Emitters
{
    class ContinuousEmitter : ParticleFunctionEmitter
    {
        public override bool IsFinished { get; protected set; }

        private readonly INumberProvider emissionDuration = new LiteralNumberProvider(0);
        private readonly INumberProvider startTime = new LiteralNumberProvider(0);
        private readonly INumberProvider emitRate = new LiteralNumberProvider(100);
        private readonly float emitInterval = 0.01f;

        private Action? particleEmitCallback;

        private float time;
        private float lastEmissionTime;

        public ContinuousEmitter(ParticleDefinitionParser parse) : base(parse)
        {
            emissionDuration = parse.NumberProvider("m_flEmissionDuration", emissionDuration);
            startTime = parse.NumberProvider("m_flStartTime", startTime);
            emitRate = parse.NumberProvider("m_flEmitRate", emitRate);

            emitInterval = 1.0f / emitRate.NextNumber();
        }

        public override void Start(Action particleEmitCallback)
        {
            this.particleEmitCallback = particleEmitCallback;

            time = 0f;
            lastEmissionTime = 0;

            IsFinished = false;
        }

        public override void Stop()
        {
            IsFinished = true;
            particleEmitCallback = null;
        }

        public override void Emit(float frameTime)
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
                for (var i = 0; i < numToEmit; i++)
                {
                    particleEmitCallback?.Invoke();
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
