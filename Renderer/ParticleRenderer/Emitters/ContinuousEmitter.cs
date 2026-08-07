namespace ValveResourceFormat.Renderer.Particles.Emitters
{
    /// <summary>
    /// Emits particles at the specified rate over time. By default (a duration of 0), the emitter
    /// continues to emit forever.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ContinuousEmitter">C_OP_ContinuousEmitter</seealso>
    class ContinuousEmitter : ParticleFunctionEmitter
    {
        public override bool IsFinished { get; protected set; }

        /// <summary>Length of time to continue emitting particles (seconds).</summary>
        private readonly INumberProvider emissionDuration = new LiteralNumberProvider(0);

        /// <summary>Time at which to begin emitting particles (seconds).</summary>
        private readonly INumberProvider startTime = new LiteralNumberProvider(0);

        /// <summary>Number of particles to spawn (per second).</summary>
        private readonly INumberProvider emitRate = new LiteralNumberProvider(100);

        private Action<float>? particleEmitCallback;

        private float time;
        private EmissionAccumulator accumulator;

        public ContinuousEmitter(ParticleDefinitionParser parse) : base(parse)
        {
            emissionDuration = parse.NumberProvider("m_flEmissionDuration", emissionDuration);
            startTime = parse.NumberProvider("m_flStartTime", startTime);
            emitRate = parse.NumberProvider("m_flEmitRate", emitRate);
        }

        public override void Start(Action<float> particleEmitCallback)
        {
            this.particleEmitCallback = particleEmitCallback;

            time = 0f;
            accumulator.Reset(initialCharge: 0d, flushEpsilon: 0.001f);

            IsFinished = false;
        }

        public override void Stop()
        {
            IsFinished = true;
            particleEmitCallback = null;
        }

        public override void Emit(float frameTime, ParticleSystemRenderState particleSystemState, float strength)
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
                // Re-evaluate the emit rate every frame: a control-point or curve-driven
                // rate changes over the emitter's lifetime.
                var rate = emitRate.NextNumber(particleSystemState) * strength;

                accumulator.Charge(rate, windowStart, windowEnd, time, particleEmitCallback);
            }

            if (nextEmissionDuration != 0f && time > nextStartTime + nextEmissionDuration)
            {
                IsFinished = true;
            }
        }
    }
}
