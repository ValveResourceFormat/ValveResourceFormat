using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Emitters
{
    /// <summary>
    /// Keeps the system topped up to a particle count, emitting whenever particles die, either at once
    /// or limited to an emission rate.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_MaintainEmitter">C_OP_MaintainEmitter</seealso>
    class MaintainEmitter : ParticleFunctionEmitter
    {
        public override bool IsFinished { get; protected set; }

        private readonly INumberProvider particlesToMaintain = new LiteralNumberProvider(100f);
        private readonly INumberProvider emissionDuration = new LiteralNumberProvider(0f);
        private readonly INumberProvider scale = new LiteralNumberProvider(1f);
        private readonly float startTime;
        private readonly float emissionRate = -1f;
        private readonly SnapshotBinding snapshotBinding;

        private Action<float>? particleEmitCallback;

        // Fractional particles the rate has earned but not yet spent
        private float rateBudget;

        public MaintainEmitter(ParticleDefinitionParser parse) : base(parse)
        {
            particlesToMaintain = parse.NumberProvider("m_nParticlesToMaintain", particlesToMaintain);
            emissionDuration = parse.NumberProvider("m_flEmissionDuration", emissionDuration);
            scale = parse.NumberProvider("m_flScale", scale);
            startTime = parse.Float("m_flStartTime", startTime);
            emissionRate = parse.Float("m_flEmissionRate", emissionRate);
            snapshotBinding = new SnapshotBinding(parse);
        }

        protected override void OnStart(Action<float> particleEmitCallback)
        {
            this.particleEmitCallback = particleEmitCallback;
            rateBudget = 0f;
            IsFinished = false;
        }

        public override void Stop()
        {
            IsFinished = true;
            particleEmitCallback = null;
        }

        public override void Emit(float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (IsFinished)
            {
                return;
            }

            var elapsed = particleSystemState.Age - StartAge;

            if (elapsed < startTime)
            {
                return;
            }

            var duration = emissionDuration.NextNumber(particleSystemState);

            if (duration > 0f && elapsed > startTime + duration)
            {
                IsFinished = true;
                return;
            }

            // A bound snapshot maintains one particle per snapshot row instead of the authored count
            var target = snapshotBinding.IsBound
                ? snapshotBinding.Count(particleSystemState)
                : particlesToMaintain.NextNumber(particleSystemState);

            var wanted = (int)(target * scale.NextNumber(particleSystemState) * strength);
            var deficit = wanted - (int)particleSystemState.ParticleCount;

            if (emissionRate > 0f)
            {
                rateBudget = MathF.Min(rateBudget + (emissionRate * frameTime), MathF.Max(deficit, 0));
                deficit = Math.Min(deficit, (int)rateBudget);
                rateBudget -= MathF.Max(deficit, 0);
            }

            for (var i = 0; i < deficit; i++)
            {
                particleEmitCallback?.Invoke(0f);
            }
        }
    }
}
