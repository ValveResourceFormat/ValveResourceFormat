namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Clamps each particle's velocity to a maximum speed, optionally reading the maximum velocity
    /// from a component of a control point.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_MaxVelocity">C_OP_MaxVelocity</seealso>
    class MaxVelocity : ParticleFunctionOperator
    {
        private readonly float maxVelocity;
        private readonly float minVelocity;
        private readonly int overrideCP = -1;
        private readonly int overrideCPField;

        public MaxVelocity(ParticleDefinitionParser parse) : base(parse)
        {
            maxVelocity = parse.Float("m_flMaxVelocity", maxVelocity);
            minVelocity = parse.Float("m_flMinVelocity", minVelocity);
            overrideCP = parse.Int32("m_nOverrideCP", overrideCP);
            overrideCPField = parse.Int32("m_nOverrideCPField", overrideCPField);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            var maxVelocity = this.maxVelocity;
            var minVelocity = this.minVelocity;
            if (overrideCP > -1)
            {
                var controlPoint = particleSystemState.GetControlPoint(overrideCP);

                maxVelocity = controlPoint.Position.GetComponent(overrideCPField);
            }

            foreach (ref var particle in particles.Current)
            {
                var speed = particle.Velocity.Length();
                if (speed > maxVelocity)
                {
                    particle.Velocity = Vector3.Normalize(particle.Velocity) * maxVelocity;
                }
                else if (speed < minVelocity)
                {
                    particle.Velocity = Vector3.Normalize(particle.Velocity) * minVelocity;
                }
            }
        }
    }
}
