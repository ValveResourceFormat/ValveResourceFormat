namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Clamps each particle's velocity to a maximum speed and raises it to a minimum speed.
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

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                var maxVelocity = maxVelocityProvider.NextNumber(ref particle, particleSystemState);
                var minVelocity = minVelocityProvider.NextNumber(ref particle, particleSystemState);

                var originalVelocity = particle.Velocity;
                var speed = originalVelocity.Length();

                if (speed > maxVelocity)
                {
                    particle.Velocity = originalVelocity * (maxVelocity / speed);
                }
                else if (speed > 0f && speed < minVelocity)
                {
                    particle.Velocity = originalVelocity * (minVelocity / speed);
                }
                else
                {
                    continue;
                }

                particle.Velocity = Vector3.Lerp(originalVelocity, particle.Velocity, strength);

                // Motion lives in the Verlet position pair; write the clamp back so the next
                // integration step actually uses it.
                particle.PositionPrevious = particle.Position - (particle.Velocity * frameTime);
            }
        }
    }
}
