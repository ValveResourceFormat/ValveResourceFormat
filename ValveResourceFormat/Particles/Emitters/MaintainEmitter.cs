using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Emitters
{
    /// <summary>
    /// Keeps the system topped up to a particle count, emitting whenever particles die, either at once
    /// or limited to an emission rate.
    /// </summary>
    /// <remarks>
    /// A snapshot on <c>m_nSnapshotControlPoint</c> replaces the authored count with its row count.
    /// The particles of one top-up are spread evenly over the frame, or all created at its start with
    /// <c>m_bEmitInstantaneously</c> and no emission rate. With <c>m_bFinalEmitOnStop</c> a stopped
    /// emitter tops up once more before it finishes.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_MaintainEmitter">C_OP_MaintainEmitter</seealso>
    class MaintainEmitter : ParticleFunctionEmitter
    {
        public override bool IsFinished { get; protected set; }

        private readonly INumberProvider particlesToMaintain = new LiteralNumberProvider(100f);
        private readonly INumberProvider emissionDuration = new LiteralNumberProvider(0f);
        private readonly INumberProvider scale = new LiteralNumberProvider(1f);
        private readonly float startTime;
        private readonly float emissionRate = -1f;
        private readonly bool emitInstantaneously;
        private readonly bool finalEmitOnStop;
        private readonly SnapshotBinding snapshotBinding;

        private Action<float>? particleEmitCallback;

        // Fractional particles the rate has earned but not yet spent
        private float rateBudget;

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

        protected override void OnStart(Action<float> particleEmitCallback)
        {
            this.particleEmitCallback = particleEmitCallback;
            rateBudget = 0f;
            finalEmitPending = false;
            IsFinished = false;
        }

        public override void Stop()
        {
            rateBudget = 0f;

            if (finalEmitOnStop && !IsFinished)
            {
                finalEmitPending = true;
                return;
            }

            IsFinished = true;
            particleEmitCallback = null;
        }

        public override void Emit(float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (IsFinished)
            {
                return;
            }

            if (finalEmitPending)
            {
                IsFinished = true;
            }

            var elapsed = particleSystemState.Age - StartAge;
            var frameStart = elapsed - frameTime;

            if (elapsed < startTime)
            {
                return;
            }

            var duration = emissionDuration.NextNumber(particleSystemState);

            if (duration != 0f && frameStart > startTime + duration)
            {
                IsFinished = true;
                return;
            }

            var target = TargetCount(particleSystemState);
            var deficit = target - (int)particleSystemState.ParticleCount;

            if (target <= 0 || deficit <= 0)
            {
                return;
            }

            var count = deficit;

            if (emissionRate > 0f)
            {
                var budget = MathF.Max(0f, MathF.Min(deficit, emissionRate * frameTime)) + rateBudget;
                count = (int)budget;
                rateBudget = budget - count;
            }

            if (count <= 0)
            {
                return;
            }

            var creationTime = MathF.Max(frameStart, startTime);
            var spacing = emitInstantaneously && emissionRate <= 0f ? 0f : (elapsed - creationTime) / count;

            for (var i = 0; i < count; i++)
            {
                creationTime = MathF.Min(creationTime, elapsed);
                particleEmitCallback?.Invoke(elapsed - creationTime);
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
