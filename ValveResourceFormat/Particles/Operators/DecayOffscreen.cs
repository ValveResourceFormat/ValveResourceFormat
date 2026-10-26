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
            => lastOnscreenTime = particleSystemState.StartedAge;

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

            var current = particles.Current;
            var ages = current.ReadAges();
            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);

            for (var i = 0; i < current.Length; i++)
            {
                var lifetime = lifetimes[i];

                if (lifetime <= 0f || lifetime <= ages[i])
                {
                    particles.Kill(i);
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
