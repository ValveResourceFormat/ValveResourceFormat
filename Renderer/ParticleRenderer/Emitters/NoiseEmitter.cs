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
        private double particlesToEmit;
        private long particlesFlushed;

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
            // Source 2 starts the accumulator at 1 so short or slow emitters (rate * duration <= 1) still emit
            particlesToEmit = 1d;
            particlesFlushed = 0;

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

            if (nextStartTime <= time)
            {
                var windowStart = frameStart;
                var windowEnd = time;

                if (nextEmissionDuration != 0f)
                {
                    windowStart = MathF.Max(nextStartTime, windowStart);
                    windowEnd = MathF.Min(nextStartTime + nextEmissionDuration, windowEnd);
                }

                if (windowEnd > windowStart)
                {
                    var noise = (Noise.Simplex1D((time + noiseOffset) * noiseScale.NextNumber(particleSystemState)) * 0.5f) + 0.5f;
                    var emissionMinValue = emissionMin.NextNumber(particleSystemState);
                    var emissionMaxValue = emissionMax.NextNumber(particleSystemState);
                    var emissionRate = emissionMinValue + noise * (emissionMaxValue - emissionMinValue);

                    particlesToEmit += Math.Max(0f, emissionRate) * (windowEnd - windowStart);

                    var flushTo = (long)Math.Floor(particlesToEmit);
                    var toEmit = flushTo - particlesFlushed;

                    if (toEmit > 0)
                    {
                        particlesFlushed = flushTo;

                        // Creation times spread evenly across the charged interval, as the engine does
                        var step = (windowEnd - windowStart) / toEmit;

                        for (var i = 0; i < toEmit; i++)
                        {
                            particleEmitCallback?.Invoke(time - (windowStart + (i * step)));
                        }
                    }
                }
            }

            if (nextEmissionDuration != 0f && time > nextStartTime + nextEmissionDuration)
            {
                IsFinished = true;
            }
        }
    }
}
