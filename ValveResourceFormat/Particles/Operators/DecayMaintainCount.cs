namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Kills particles so that no more than a target count are alive, oldest first unless
    /// <c>m_bKillNewest</c>. The target is a fixed count, or the size of the snapshot on a control
    /// point, times a scale. With a decay delay the excess particles are not killed outright but given
    /// that long to live.
    /// </summary>
    /// <remarks>
    /// A delayed particle is marked by a negative lifetime whose magnitude is the age it dies at.
    /// With <c>m_bLifespanDecay</c> particles past their lifetime also die, as with a lifespan decay.
    /// A snapshot subset matches nothing, so it gives a target of 0. Survivors keep their order.
    /// Operator strength is not read.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_DecayMaintainCount">C_OP_DecayMaintainCount</seealso>
    class DecayMaintainCount : ParticleFunctionOperator
    {
        private readonly int particlesToMaintain = 100;
        private readonly float decayDelay;
        private readonly int snapshotControlPoint = -1;
        private readonly bool hasSnapshotSubset;
        private readonly bool lifespanDecay = true;
        private readonly INumberProvider scale = new LiteralNumberProvider(1f);
        private readonly bool killNewest;

        private int scheduledDecayCount;

        public DecayMaintainCount(ParticleDefinitionParser parse) : base(parse)
        {
            particlesToMaintain = parse.Int32("m_nParticlesToMaintain", particlesToMaintain);
            decayDelay = parse.Float("m_flDecayDelay", decayDelay);
            snapshotControlPoint = Math.Clamp(parse.Int32("m_nSnapshotControlPoint", snapshotControlPoint), -1, ParticleSystemState.MaxControlPoint + 1);
            hasSnapshotSubset = !string.IsNullOrEmpty(parse.String("m_strSnapshotSubset"));
            lifespanDecay = parse.Boolean("m_bLifespanDecay", lifespanDecay);
            scale = parse.CollectionNumberProvider("m_flScale", scale);
            killNewest = parse.Boolean("m_bKillNewest", killNewest);
        }

        public override bool IsOrderImportant => true;

        public override void Initialize(ParticleSystemState particleSystemState) => scheduledDecayCount = 0;

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var count = particlesToMaintain;

            if (snapshotControlPoint >= 0 && particleSystemState.GetControlPointSnapshot(snapshotControlPoint) is { } snapshot)
            {
                count = hasSnapshotSubset ? 0 : (int)snapshot.NumParticles;
            }

            var target = Math.Min((int)(count * scale.NextNumber(particleSystemState)), particles.Capacity);

            var current = particles.Current;
            var excess = current.Length > target ? current.Length - target - scheduledDecayCount : 0;

            for (var n = 0; n < current.Length; n++)
            {
                var particle = current[killNewest ? current.Length - 1 - n : n];
                var lifetime = particle.Lifetime;

                var expired = (lifespanDecay || (decayDelay != 0f && lifetime < 0f)) && particle.Age > MathF.Abs(lifetime);

                if (expired)
                {
                    particle.Kill();
                    excess--;
                    scheduledDecayCount = Math.Max(scheduledDecayCount - 1, 0);
                }
                else if (excess > 0 && lifetime >= 0f)
                {
                    if (decayDelay == 0f)
                    {
                        particle.Kill();
                    }
                    else
                    {
                        particle.Lifetime = -(decayDelay + particle.Age);
                        scheduledDecayCount++;
                    }

                    excess--;
                }
            }
        }
    }
}
