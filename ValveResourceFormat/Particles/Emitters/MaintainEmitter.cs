using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Emitters
{
    /// <summary>
    /// Keeps the system topped up to a particle count, emitting whenever particles die, either at once
    /// or limited to an emission rate.
    /// </summary>
    /// <remarks>
    /// A snapshot on <c>m_nSnapshotControlPoint</c> replaces the authored count with its row count.
    /// The particles of one top-up are spread evenly over the step, or all created at its start with
    /// <c>m_bEmitInstantaneously</c> and no emission rate. With <c>m_bFinalEmitOnStop</c> a stopped
    /// emitter tops up once more before it finishes. The emission duration counts from
    /// <c>m_flStartTime</c> on the system's clock, not from the last restart.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_MaintainEmitter">C_OP_MaintainEmitter</seealso>
    class MaintainEmitter : ParticleFunctionEmitter
    {
        private readonly INumberProvider particlesToMaintain = new LiteralNumberProvider(100f);
        private readonly INumberProvider emissionDuration = new LiteralNumberProvider(0f);
        private readonly INumberProvider scale = new LiteralNumberProvider(1f);
        private readonly float startTime;
        private readonly float emissionRate = -1f;
        private readonly bool emitInstantaneously;
        private readonly bool finalEmitOnStop;
        private readonly SnapshotBinding snapshotBinding;

        private float timeBase;

        // Fractional particles the rate has earned but not yet spent
        private float rateBudget;

        private bool emitting = true;
        private bool finalEmitPending;

        public MaintainEmitter(ParticleDefinitionParser parse) : base(parse)
        {
            particlesToMaintain = parse.NumberProvider("m_nParticlesToMaintain", particlesToMaintain);
            emissionDuration = parse.NumberProvider("m_flEmissionDuration", emissionDuration);
            scale = parse.NumberProvider("m_flScale", scale);
            startTime = parse.Float("m_flStartTime", startTime);
            emissionRate = parse.Float("m_flEmissionRate", emissionRate);
            emitInstantaneously = parse.Boolean("m_bEmitInstantaneously", emitInstantaneously);
            finalEmitOnStop = parse.Boolean("m_bFinalEmitOnStop", finalEmitOnStop);
            snapshotBinding = new SnapshotBinding(parse);
        }

        public override void Reset()
        {
            timeBase = 0f;
            rateBudget = 0f;
            emitting = true;
            finalEmitPending = false;
        }

        public override void Restart(ParticleSystemState particleSystemState)
        {
            timeBase = particleSystemState.Age;
        }

        public override void StartEmission()
        {
            emitting = true;
        }

        public override void StopEmission()
        {
            rateBudget = 0f;

            if (finalEmitOnStop)
            {
                finalEmitPending = true;
                return;
            }

            emitting = false;
        }

        public override bool MayCreateMoreParticles(ParticleSystemState particleSystemState)
        {
            var duration = emissionDuration.NextNumber(particleSystemState);

            if (duration != 0f && particleSystemState.Age - particleSystemState.CurrentStep > duration + startTime)
            {
                return false;
            }

            return emitting && !(OpFadeOscillatePeriod != 0f && GetFunctionTime(particleSystemState) > OpEndFadeOutTime);
        }

        public override void Emit(ParticleSystemState particleSystemState, float strength, Action<float> emit)
        {
            if (!emitting)
            {
                return;
            }

            if (finalEmitPending)
            {
                emitting = false;
                finalEmitPending = false;
            }

            var time = particleSystemState.Age;
            var start = timeBase + startTime;

            if (start > time)
            {
                return;
            }

            var target = TargetCount(particleSystemState);
            var active = particleSystemState.Data?.Particles.Count ?? 0;

            if (active >= target || target <= 0)
            {
                return;
            }

            var duration = emissionDuration.NextNumber(particleSystemState);

            if (duration != 0f && time - particleSystemState.CurrentStep > duration + startTime)
            {
                return;
            }

            var count = target - active;

            if (emissionRate > 0f)
            {
                var budget = Math.Clamp(emissionRate * particleSystemState.CurrentStep, 0f, count) + rateBudget;
                count = (int)budget;
                rateBudget = budget - count;
            }

            count = Math.Max(0, Math.Min(count, FreeSlots(particleSystemState)));

            if (count == 0)
            {
                return;
            }

            var creationTime = MathF.Max(particleSystemState.StepStartAge, start);
            var spacing = !emitInstantaneously || emissionRate > 0f ? (time - creationTime) / count : 0f;

            for (var i = 0; i < count; i++)
            {
                creationTime = MathF.Min(creationTime, time);
                emit(creationTime);
                creationTime += spacing;
            }
        }

        private int TargetCount(ParticleSystemState particleSystemState)
        {
            var target = (int)particlesToMaintain.NextNumber(particleSystemState);

            if (snapshotBinding.Resolve(particleSystemState) is { } snapshot)
            {
                target = (int)snapshot.NumParticles;
            }

            return Math.Max(0, (int)(target * scale.NextNumber(particleSystemState)));
        }
    }
}
