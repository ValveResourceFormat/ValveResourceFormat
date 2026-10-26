using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Emitters
{
    /// <summary>
    /// Emits particles at a rate modulated by noise, producing organic variation between a minimum
    /// and maximum emission rate.
    /// </summary>
    /// <remarks>
    /// The noise is sampled at the system's clock, offset by <c>m_flOffset</c>, plus the world clock in
    /// whole milliseconds scaled by <c>m_flWorldTimeScale</c>.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_NoiseEmitter">C_OP_NoiseEmitter</seealso>
    class NoiseEmitter : ParticleFunctionEmitter
    {
        private const float MaximumRate = 2147483600f;

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

        private double totalToEmit;
        private long totalEmitted;
        private float timeBase;
        private bool active = true;

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

            if (parse.BehaviorVersion < 2)
            {
                emissionScale = MathF.Max(0f, parse.Float("m_flEmissionScale", emissionScale));
            }

            scaleControlPoint = parse.Int32("m_nScaleControlPoint", scaleControlPoint);
            scaleControlPointField = parse.Int32("m_nScaleControlPointField", scaleControlPointField);
            worldNoisePoint = parse.Int32("m_nWorldNoisePoint", worldNoisePoint);
            worldNoiseScale = parse.Float("m_flWorldNoiseScale", worldNoiseScale);
            offsetLoc = parse.Vector3("m_vecOffsetLoc", offsetLoc);
            worldTimeScale = parse.Float("m_flWorldTimeScale", worldTimeScale);
        }

        public override void AddControlPointDefaults(ControlPointDefaults defaults)
        {
            defaults.AddScale(scaleControlPoint, scaleControlPointField);
        }

        public override void Reset()
        {
            totalToEmit = 1d;
            totalEmitted = 0;
            timeBase = 0f;
            active = true;
        }

        public override void Restart(ParticleSystemState particleSystemState)
        {
            if (EmissionDuration(particleSystemState) != 0f)
            {
                timeBase = particleSystemState.Age;
            }
        }

        // A negative duration counts as 0, which emits without an end
        private float EmissionDuration(ParticleSystemState particleSystemState)
            => MathF.Max(0f, emissionDuration.NextNumber(particleSystemState));

        public override void StartEmission(ParticleSystemState particleSystemState)
        {
            active = true;
            RearmFirstParticle(particleSystemState, particleSystemState.Age);
        }

        public override void SkipToTime(float time, ParticleSystemSimulation simulation)
        {
            RearmFirstParticle(simulation.RenderState, time);
        }

        /// <summary>
        /// Once the start time has passed, restores the accumulator to its initial state, which
        /// emits a particle on the next step and drops the fractional carry.
        /// </summary>
        private void RearmFirstParticle(ParticleSystemState particleSystemState, float time)
        {
            var start = timeBase + startTime.NextNumber(particleSystemState);

            if (start >= time)
            {
                return;
            }

            totalToEmit = 1d;
            totalEmitted = 0;
        }

        public override void StopEmission()
        {
            active = false;
        }

        public override bool MayCreateMoreParticles(ParticleSystemState particleSystemState)
        {
            var duration = EmissionDuration(particleSystemState);
            var start = timeBase + startTime.NextNumber(particleSystemState);

            return active
                && !(duration != 0f && particleSystemState.Age - particleSystemState.CurrentStep > start + duration)
                && !IsPastFadeOut(particleSystemState);
        }

        public override void Emit(ParticleSystemState particleSystemState, float strength, Action<float> emit)
        {
            var time = particleSystemState.Age;
            var step = particleSystemState.CurrentStep;

            var amplitude = absVal ? 1f : 0.5f;
            var indexScale = particleSystemState.HighestControlPoint * emissionScale;

            var position = Vector3.Zero;
            var worldScale = 1f;

            if (worldNoisePoint != -1)
            {
                position = particleSystemState.GetControlPoint(worldNoisePoint).Position + offsetLoc;
                worldScale = worldNoiseScale;
            }

            var timeTerm = (noiseOffset + time) * noiseScale.NextNumber(particleSystemState);
            // The time term is the process wall clock in milliseconds, read once per axis; simulation world time is used here to keep playback deterministic
            var worldTimeTerm = (int)(particleSystemState.WorldTime * 1000f) * worldTimeScale;
            var noise = Noise.Value3D((position * worldScale) + new Vector3(timeTerm) + new Vector3(worldTimeTerm));

            if (absVal)
            {
                noise = MathF.Abs(noise);
            }

            if (absValInv)
            {
                noise = 1f - noise;
            }

            var outputMin = emissionMin.NextNumber(particleSystemState);
            var range = emissionMax.NextNumber(particleSystemState) - outputMin;
            var rate = Math.Clamp((range * amplitude * noise) + (((1f - amplitude) * range) + outputMin), 0f, MaximumRate) * strength;

            if (scaleControlPoint >= 0 && scaleControlPointField != -1)
            {
                var component = scaleControlPointField is >= 0 and <= 2
                    ? particleSystemState.GetControlPoint(scaleControlPoint).Position.GetComponent(scaleControlPointField)
                    : 0f;

                rate *= MathF.Max(0f, component);
            }

            if (indexScale != 0f)
            {
                rate *= indexScale;
            }

            var duration = EmissionDuration(particleSystemState);
            var start = timeBase + startTime.NextNumber(particleSystemState);

            if (rate == 0f || !active || (duration != 0f && time - step > start + duration) || IsPastFadeOut(particleSystemState) || start > time)
            {
                return;
            }

            var windowStart = time - step;
            var windowEnd = time;

            if (duration != 0f)
            {
                windowStart = MathF.Max(start, windowStart);
                windowEnd = MathF.Min(duration + start, time);
            }

            totalToEmit += (windowEnd - windowStart) * rate;

            var count = Math.Max((int)Math.Floor(totalToEmit) - (int)totalEmitted, 0);
            totalEmitted += count;
            count = Math.Min(count, Math.Max(FreeSlots(particleSystemState), 0));

            if (count == 0)
            {
                return;
            }

            var spacing = (windowEnd - windowStart) / count;
            var creationTime = windowStart;

            for (var i = 0; i < count; i++)
            {
                creationTime = MathF.Min(creationTime + spacing, windowEnd);
                emit(creationTime);
            }
        }
    }
}
