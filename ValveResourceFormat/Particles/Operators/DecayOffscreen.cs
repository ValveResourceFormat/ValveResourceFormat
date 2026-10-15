namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// A lifespan decay that only runs once the system has gone unseen for longer than
    /// <c>m_flOffscreenTime</c>. Until then no particle is killed by it, even past its lifetime.
    /// </summary>
    /// <remarks>
    /// The system counts as seen while the local player stands inside its bounds. Without a player it
    /// is never seen, so this is a lifespan decay that starts <c>m_flOffscreenTime</c> after the system
    /// was created. Operator strength is not read.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_DecayOffscreen">C_OP_DecayOffscreen</seealso>
    class DecayOffscreen : ParticleFunctionOperator
    {
        private readonly INumberProvider offscreenTime = new LiteralNumberProvider(2f);

        private float lastOnscreenTime;

        public DecayOffscreen(ParticleDefinitionParser parse) : base(parse)
        {
            offscreenTime = parse.CollectionNumberProvider("m_flOffscreenTime", offscreenTime);
        }

        public override void Initialize(ParticleSystemState particleSystemState)
            => lastOnscreenTime = particleSystemState.Age == float.MinValue ? 0f : particleSystemState.Age;

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (IsPlayerInsideBounds(particleSystemState))
            {
                lastOnscreenTime = particleSystemState.Age;
                return;
            }

            if (particleSystemState.Age - lastOnscreenTime <= offscreenTime.NextNumber(particleSystemState))
            {
                return;
            }

            foreach (ref var particle in particles.Current)
            {
                if (particle.Lifetime <= 0f || particle.Lifetime <= particle.Age)
                {
                    particle.Kill();
                }
            }
        }

        private static bool IsPlayerInsideBounds(ParticleSystemState particleSystemState)
            => particleSystemState.Player is { } player
                && particleSystemState.Data?.WorldBoundingBox is { } bounds
                && player.TryGetPosition(ParticleEntityPos.PARTICLE_ABS_ORIGIN, out var position)
                && bounds.Contains(position);
    }
}
