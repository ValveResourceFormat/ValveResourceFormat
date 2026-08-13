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
        private readonly bool absVal;
        private readonly bool absValInv;
        private readonly float emissionScale;
        private readonly int scaleControlPoint = -1;
        private readonly int scaleControlPointField;
        private readonly int worldNoisePoint = -1;
        private readonly float worldNoiseScale = 0.001f;
        private readonly Vector3 offsetLoc;
        private readonly float worldTimeScale;

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
            absVal = parse.Boolean("m_bAbsVal", absVal);
            absValInv = parse.Boolean("m_bAbsValInv", absValInv);
            emissionScale = parse.Float("m_flEmissionScale", emissionScale);
            scaleControlPoint = parse.Int32("m_nScaleControlPoint", scaleControlPoint);
            scaleControlPointField = parse.Int32("m_nScaleControlPointField", scaleControlPointField);
            worldNoisePoint = parse.Int32("m_nWorldNoisePoint", worldNoisePoint);
            worldNoiseScale = parse.Float("m_flWorldNoiseScale", worldNoiseScale);
            offsetLoc = parse.Vector3("m_vecOffsetLoc", offsetLoc);
            worldTimeScale = parse.Float("m_flWorldTimeScale", worldTimeScale);
        }

        public override void Start(Action<float> particleEmitCallback)
        {
            this.particleEmitCallback = particleEmitCallback;

            time = 0f;
            accumulator.Reset(initialCharge: 1d, flushEpsilon: 0f);

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
                var basePosition = worldNoisePoint < 0
                    ? Vector3.Zero
                    : (particleSystemState.GetControlPoint(worldNoisePoint).Position + offsetLoc) * worldNoiseScale;

                // The engine's world time term ticks in milliseconds
                var sampleTime = ((time + noiseOffset) * noiseScale.NextNumber(particleSystemState))
                    + (particleSystemState.WorldTime * 1000f * worldTimeScale);
                var noise = Noise.Value3D(basePosition + new Vector3(sampleTime));

                var absScale = absVal ? 1f : 0.5f;
                var normalized = absVal ? MathF.Abs(noise) : noise;

                if (absValInv)
                {
                    normalized = 1f - normalized;
                }

                var emissionMinValue = emissionMin.NextNumber(particleSystemState);
                var emissionMaxValue = emissionMax.NextNumber(particleSystemState);
                var span = emissionMaxValue - emissionMinValue;
                var emissionRate = MathF.Max(0f, emissionMinValue + ((1f - absScale) * span) + (absScale * span * normalized)) * strength;

                if (scaleControlPoint >= 0 && scaleControlPointField != -1)
                {
                    var component = scaleControlPointField is >= 0 and <= 2
                        ? particleSystemState.GetControlPoint(scaleControlPoint).Position.GetComponent(scaleControlPointField)
                        : 0f;

                    emissionRate *= MathF.Max(0f, component);
                }

                // The highest index scales the rate without the +1 the continuous emitter applies
                var indexScale = particleSystemState.HighestControlPoint * emissionScale;

                if (indexScale != 0f)
                {
                    emissionRate *= indexScale;
                }

                accumulator.Charge(emissionRate, windowStart, windowEnd, time, particleEmitCallback);
            }

            if (nextEmissionDuration != 0f && time > nextStartTime + nextEmissionDuration)
            {
                IsFinished = true;
            }
        }
    }
}
