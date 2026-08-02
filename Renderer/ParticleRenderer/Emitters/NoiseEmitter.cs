using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Emitters
{
    /// <summary>
    /// Emits particles at a rate modulated by noise, producing organic variation between a minimum
    /// and maximum emission rate.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_NoiseEmitter">C_OP_NoiseEmitter</seealso>
    class NoiseEmitter : ParticleFunctionEmitter
    {
        public override bool IsFinished { get; protected set; }

        private readonly INumberProvider emissionDuration = new LiteralNumberProvider(0);
        private readonly INumberProvider startTime = new LiteralNumberProvider(0);

        private readonly INumberProvider noiseScale = new LiteralNumberProvider(0.1f);
        private readonly INumberProvider emissionMin = new LiteralNumberProvider(0);
        private readonly INumberProvider emissionMax = new LiteralNumberProvider(100f);
        private readonly float noiseOffset;

        private Action<float>? particleEmitCallback;

        private float time;
        private EmissionAccumulator accumulator;

        public NoiseEmitter(ParticleDefinitionParser parse) : base(parse)
        {
            emissionDuration = parse.NumberProvider("m_flEmissionDuration", emissionDuration);
            startTime = parse.NumberProvider("m_flStartTime", startTime);

            noiseScale = parse.NumberProvider("m_flNoiseScale", noiseScale);
            emissionMin = parse.NumberProvider("m_flOutputMin", emissionMin);
            emissionMax = parse.NumberProvider("m_flOutputMax", emissionMax);
            noiseOffset = parse.Float("m_flOffset", noiseOffset);
        }

        public override void Start(Action<float> particleEmitCallback)
        {
            this.particleEmitCallback = particleEmitCallback;

            time = 0f;
            accumulator.Reset();

            IsFinished = false;
        }

        public override void Stop()
        {
            IsFinished = true;
            particleEmitCallback = null;
        }

        public override void Emit(float frameTime, ParticleSystemRenderState particleSystemState)
        {
            if (IsFinished)
            {
                return;
            }

            var frameStart = time;
            time += frameTime;

            var nextStartTime = startTime.NextNumber(particleSystemState);
            var nextEmissionDuration = emissionDuration.NextNumber(particleSystemState);

            if (TryGetEmissionWindow(frameStart, time, nextStartTime, nextEmissionDuration, out var windowStart, out var windowEnd))
            {
                var noise = (Noise.Simplex1D((time + noiseOffset) * noiseScale.NextNumber(particleSystemState)) * 0.5f) + 0.5f;
                var emissionMinValue = emissionMin.NextNumber(particleSystemState);
                var emissionMaxValue = emissionMax.NextNumber(particleSystemState);
                var emissionRate = emissionMinValue + noise * (emissionMaxValue - emissionMinValue);

                accumulator.Charge(emissionRate, windowStart, windowEnd, time, particleEmitCallback);
            }

            if (nextEmissionDuration != 0f && time > nextStartTime + nextEmissionDuration)
            {
                IsFinished = true;
            }
        }
    }
}
