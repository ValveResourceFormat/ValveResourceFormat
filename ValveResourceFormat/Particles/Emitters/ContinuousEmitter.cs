using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Emitters
{
    /// <summary>
    /// Emits particles at the specified rate over time. By default (a duration of 0), the emitter
    /// continues to emit forever.
    /// </summary>
    /// <remarks>
    /// With <c>m_bInitFromKilledParentParticles</c> it emits one particle per parent particle event of
    /// <c>m_nEventType</c> instead, at the moment the parent raises it.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ContinuousEmitter">C_OP_ContinuousEmitter</seealso>
    class ContinuousEmitter : ParticleFunctionEmitter
    {
        private const double FlushEpsilon = 0.001;

        /// <summary>Length of time to continue emitting particles (seconds).</summary>
        private readonly INumberProvider emissionDuration = new LiteralNumberProvider(0);

        /// <summary>Time at which to begin emitting particles (seconds).</summary>
        private readonly INumberProvider startTime = new LiteralNumberProvider(0);

        /// <summary>Number of particles to spawn (per second).</summary>
        private readonly INumberProvider emitRate = new LiteralNumberProvider(100);

        /// <summary>
        /// Scales the rate by the number of rows in a snapshot. A bound control point that carries no
        /// snapshot scales the rate to zero, so the emitter stays silent until one arrives.
        /// </summary>
        private readonly SnapshotBinding snapshotBinding;

        /// <summary>
        /// Scales the rate by the number of control points in use (the highest index plus one). Only
        /// read below behavior version 2, and ignored when <see cref="scalePerParentParticle"/> is set.
        /// </summary>
        private readonly float emissionScale;

        /// <summary>Scales the rate by the parent system's live particle count, or by itself for a system without a parent.</summary>
        private readonly float scalePerParentParticle;

        private readonly bool initFromKilledParentParticles;
        private readonly ParticleEventType eventType = ParticleEventType.PARTICLE_EVENT_TYPE_MASK_KILLED;

        /// <summary>Most particles spawned per update when positive. The excess stays due for later updates.</summary>
        private readonly int limitPerUpdate;

        private readonly bool forceEmitOnFirstUpdate;
        private readonly bool forceEmitOnLastUpdate;

        /// <summary>False when the rate is a constant that never rises above zero.</summary>
        private readonly bool steadyRateCanBePositive = true;

        private double emitAccumulator;
        private long emitted;
        private float timeBase;
        private float duration;
        private float windowStart = -1f;
        private bool emitting = true;
        private bool forcePending;

        public ContinuousEmitter(ParticleDefinitionParser parse) : base(parse)
        {
            emissionDuration = parse.NumberProvider("m_flEmissionDuration", emissionDuration);
            startTime = parse.NumberProvider("m_flStartTime", startTime);
            emitRate = parse.NumberProvider("m_flEmitRate", emitRate);
            snapshotBinding = new SnapshotBinding(parse);

            if (parse.BehaviorVersion < 2)
            {
                emissionScale = MathF.Max(0f, parse.Float("m_flEmissionScale", emissionScale));
            }

            scalePerParentParticle = parse.Float("m_flScalePerParentParticle", scalePerParentParticle);
            initFromKilledParentParticles = parse.Boolean("m_bInitFromKilledParentParticles", initFromKilledParentParticles);
            eventType = parse.Enum("m_nEventType", eventType);
            limitPerUpdate = parse.Int32("m_nLimitPerUpdate", limitPerUpdate);
            forceEmitOnFirstUpdate = parse.Boolean("m_bForceEmitOnFirstUpdate", forceEmitOnFirstUpdate);
            forceEmitOnLastUpdate = parse.Boolean("m_bForceEmitOnLastUpdate", forceEmitOnLastUpdate);

            if (emitRate is LiteralNumberProvider { Value: var rate })
            {
                steadyRateCanBePositive = rate > 0f;
            }
        }

        public override ParticleEventType EventTypeMask => initFromKilledParentParticles ? eventType : ParticleEventType.PARTICLE_EVENT_TYPE_MASK_NONE;

        /// <summary>Whether emission ends: a duration is set, or the rate never rises above zero.</summary>
        private bool IsFinite => duration != 0f || !steadyRateCanBePositive;

        /// <summary>How long a finite emitter emits for after its start.</summary>
        private float EmitSpan => steadyRateCanBePositive ? duration : 0f;

        public override void Reset()
        {
            emitAccumulator = 0d;
            emitted = 0;
            timeBase = 0f;
            windowStart = -1f;
            emitting = true;
            forcePending = false;
        }

        public override void Restart(ParticleSystemState particleSystemState)
        {
            duration = emissionDuration.NextNumber(particleSystemState);

            if (!IsFinite)
            {
                return;
            }

            windowStart = particleSystemState.Age + startTime.NextNumber(particleSystemState);
            emitAccumulator = 0d;
            emitted = 0;
            timeBase = particleSystemState.Age;
        }

        public override void StartEmission()
        {
            emitting = true;
        }

        public override void StopEmission()
        {
            if (forceEmitOnLastUpdate)
            {
                forcePending = true;
            }

            emitting = false;
        }

        public override bool MayCreateMoreParticles(ParticleSystemState particleSystemState)
        {
            if (initFromKilledParentParticles || (!emitting && !forcePending) || IsPastFadeOut(particleSystemState))
            {
                return false;
            }

            var start = windowStart == -1f ? timeBase + startTime.NextNumber(particleSystemState) : windowStart;
            var time = particleSystemState.Age;

            if (duration != 0f && time - particleSystemState.CurrentStep > start + duration)
            {
                return false;
            }

            return steadyRateCanBePositive || time <= start;
        }

        public override void Emit(ParticleSystemState particleSystemState, float strength, Action<float> emit)
        {
            duration = emissionDuration.NextNumber(particleSystemState);
            var start = timeBase + startTime.NextNumber(particleSystemState);
            windowStart = start;

            var time = particleSystemState.Age;
            var step = particleSystemState.CurrentStep;

            if (start > time)
            {
                return;
            }

            var rate = MathF.Max(0f, strength * EvaluateRate(particleSystemState, start));

            if (rate <= 0f)
            {
                return;
            }

            double previous;
            double current = time;
            double span;
            double budget;

            if (initFromKilledParentParticles)
            {
                var events = particleSystemState.ParentSystem != null ? particleSystemState.EmitterEventCount : 0;

                if (events == 0)
                {
                    return;
                }

                if (IsFinite)
                {
                    current = Math.Min(Math.Max(current, start), start + EmitSpan);
                }

                previous = current;
                span = 0d;
                budget = events;
            }
            else
            {
                if (scalePerParentParticle > 0f)
                {
                    rate *= particleSystemState.ParentSystem?.Data is { } parent
                        ? parent.Particles.Count * scalePerParentParticle
                        : scalePerParentParticle;
                }
                else if (emissionScale > 0f)
                {
                    rate *= (particleSystemState.HighestControlPoint + 1) * emissionScale;
                }

                if (snapshotBinding.HasControlPoint)
                {
                    rate *= snapshotBinding.Count(particleSystemState);
                }

                if (rate <= 0f || (!emitting && !forcePending) || IsPastFadeOut(particleSystemState))
                {
                    return;
                }

                if (duration != 0f && time - step > start + duration)
                {
                    return;
                }

                previous = particleSystemState.StepStartAge;

                if (IsFinite)
                {
                    previous = Math.Max(previous, start);
                    current = Math.Min(current, start + EmitSpan);
                }

                span = Math.Min(current - previous, step);
                budget = Math.Max(0d, rate * span);
            }

            emitAccumulator += budget;

            var count = Math.Max(0, (int)Math.Floor(emitAccumulator + FlushEpsilon) - (int)emitted);

            if (forceEmitOnFirstUpdate && time - step == 0f)
            {
                count = Math.Max(count, 1);
            }

            if (forcePending)
            {
                forcePending = false;
                count = Math.Max(count, 1);
            }

            if (limitPerUpdate > 0)
            {
                count = Math.Min(count, limitPerUpdate);
            }

            if (count <= 0)
            {
                return;
            }

            emitted += count;
            count = Math.Min(count, FreeSlots(particleSystemState));

            if (count <= 0)
            {
                return;
            }

            var creationStep = (float)(span / count);
            var creationTime = (float)(creationStep + previous);
            var currentTime = (float)current;

            for (var i = 0; i < count; i++)
            {
                var particleTime = MathF.Min(creationTime, currentTime);
                emit(particleTime);
                creationTime = particleTime + creationStep;
            }
        }

        /// <summary>
        /// Reads the rate, with a rate driven by the system's age measured from the start of emission.
        /// </summary>
        private float EvaluateRate(ParticleSystemState particleSystemState, float start)
            => emitRate is CollectionAgeNumberProvider collectionAge
                ? collectionAge.AtAge(particleSystemState.Age - start)
                : emitRate.NextNumber(particleSystemState);

        public override void SkipToTime(float time, ParticleSystemSimulation simulation)
        {
            var state = simulation.RenderState;
            var start = startTime.NextNumber(state) + timeBase;

            if (start >= time || initFromKilledParentParticles)
            {
                return;
            }

            var now = state.Age;
            var from = now - time;

            if (duration != 0f || !steadyRateCanBePositive)
            {
                from = MathF.Max(start, from);
            }

            simulation.Resimulate(now - MathF.Min(now - from, 4f), time);
        }
    }
}
