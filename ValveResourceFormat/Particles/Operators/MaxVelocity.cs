namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Clamps each particle's velocity to a maximum speed and raises it to a minimum speed, by moving the
    /// position relative to the previous position. The minimum wins when it exceeds the maximum, and a
    /// particle at rest is first given a small random step so it can be pushed up to the minimum.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_MaxVelocity">C_OP_MaxVelocity</seealso>
    class MaxVelocity : ParticleFunctionOperator
    {
        private readonly INumberProvider maxVelocityProvider = new LiteralNumberProvider(0);
        private readonly INumberProvider minVelocityProvider = new LiteralNumberProvider(0);

        public MaxVelocity(ParticleDefinitionParser parse) : base(parse)
        {
            maxVelocityProvider = parse.NumberProvider("m_flMaxVelocity", maxVelocityProvider);
            minVelocityProvider = parse.NumberProvider("m_flMinVelocity", minVelocityProvider);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                var maxVelocity = maxVelocityProvider.NextNumber(ref particle, particleSystemState);
                var minVelocity = minVelocityProvider.NextNumber(ref particle, particleSystemState);

                var step = particle.Position - particle.PositionPrevious;

                if (minVelocity > 0f && step.LengthSquared() <= 0f)
                {
                    var x = particleSystemState.Random.NextBetween(-0.1f, 0.1f);
                    var y = particleSystemState.Random.NextBetween(-0.1f, 0.1f);
                    step = new Vector3(x, y, 0.1f - x - y);
                }

                var length = step.Length();
                var scale = 1f;

                if (maxVelocity * frameTime < length)
                {
                    scale = maxVelocity * frameTime / length;
                }

                if (length < minVelocity * frameTime)
                {
                    scale = minVelocity * frameTime / length;
                }

                var clamped = (step * scale) + particle.PositionPrevious;

                particle.Position = strength >= 1f ? clamped : (clamped * strength) + (particle.Position * (1f - strength));
            }
        }
    }
}
