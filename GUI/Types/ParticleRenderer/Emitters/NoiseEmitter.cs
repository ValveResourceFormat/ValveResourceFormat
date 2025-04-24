using GUI.Types.ParticleRenderer.Utils;

namespace GUI.Types.ParticleRenderer.Emitters
{
    class NoiseEmitter : ParticleFunctionEmitter
    {
        public override bool IsFinished { get; protected set; }

        private readonly INumberProvider emissionDuration = new LiteralNumberProvider(0);
        private readonly INumberProvider startTime = new LiteralNumberProvider(0);

        private readonly INumberProvider noiseScale = new LiteralNumberProvider(0.1f);
        private readonly INumberProvider emissionMin = new LiteralNumberProvider(0);
        private readonly INumberProvider emissionMax = new LiteralNumberProvider(100f);

        private Action? particleEmitCallback;

        private float time;
        private float particlesToEmit;

        public NoiseEmitter(ParticleDefinitionParser parse) : base(parse)
        {
            emissionDuration = parse.NumberProvider("m_flEmissionDuration", emissionDuration);
            startTime = parse.NumberProvider("m_flStartTime", startTime);

            noiseScale = parse.NumberProvider("m_flNoiseScale", noiseScale);
            emissionMin = parse.NumberProvider("m_flOutputMin", emissionMin);
            emissionMax = parse.NumberProvider("m_flOutputMax", emissionMax);
        }

        public override void Start(Action particleEmitCallback)
        {
            this.particleEmitCallback = particleEmitCallback;

            time = 0f;

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
                // Calculate current emission rate based on noise
                var noise = Noise.Simplex1D(time * noiseScale.NextNumber());
                var valueScale = emissionMax.NextNumber() - emissionMin.NextNumber();
                var valueBase = emissionMin.NextNumber();
                var emissionRate = valueBase + noise * valueScale;

                // Aggregate emission into num of particles to emit
                particlesToEmit += Math.Clamp(emissionRate, 0, emissionMax.NextNumber()) * frameTime; // Limit the amount of particles to emit at once in case of refocus

                // If nr of particles to emit is > 0, emit it
                while (particlesToEmit > 1.0f)
                {
                    particleEmitCallback?.Invoke();
                    particlesToEmit -= 1.0f;
                }
            }

            if (nextEmissionDuration != 0f && time > nextStartTime + nextEmissionDuration)
            {
                IsFinished = true;
            }
        }
    }
}
