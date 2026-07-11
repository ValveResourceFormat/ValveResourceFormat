namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Clamps each particle's velocity to a maximum speed and raises it to a minimum speed,
    /// optionally reading the maximum velocity from a component of a control point.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_MaxVelocity">C_OP_MaxVelocity</seealso>
    class MaxVelocity : ParticleFunctionOperator
    {
        private readonly INumberProvider maxVelocityProvider = new LiteralNumberProvider(0);
        private readonly INumberProvider minVelocityProvider = new LiteralNumberProvider(0);
        private readonly int overrideCP = -1;
        private readonly int overrideCPField;

        public MaxVelocity(ParticleDefinitionParser parse) : base(parse)
        {
            maxVelocityProvider = parse.NumberProvider("m_flMaxVelocity", maxVelocityProvider);
            minVelocityProvider = parse.NumberProvider("m_flMinVelocity", minVelocityProvider);
            overrideCP = parse.Int32("m_nOverrideCP", overrideCP);
            overrideCPField = parse.Int32("m_nOverrideCPField", overrideCPField);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var maxVelocity = overrideCP > -1
                    ? particleSystemState.GetControlPoint(overrideCP).Position.GetComponent(overrideCPField)
                    : maxVelocityProvider.NextNumber(ref particle, particleSystemState);
                var minVelocity = minVelocityProvider.NextNumber(ref particle, particleSystemState);

                var speed = particle.Velocity.Length();
                if (speed > maxVelocity)
                {
                    particle.Velocity *= maxVelocity / speed;
                }
                else if (speed > 0f && speed < minVelocity)
                {
                    particle.Velocity *= minVelocity / speed;
                }
                else
                {
                    continue;
                }

                // Motion lives in the Verlet position pair; write the clamp back so the next
                // integration step actually uses it.
                particle.PositionPrevious = particle.Position - (particle.Velocity * frameTime);
            }
        }
    }
}
