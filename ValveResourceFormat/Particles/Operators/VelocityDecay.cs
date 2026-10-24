namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Kills a particle when it moves too slowly. The test compares the squared step over the step
    /// length against the squared minimum, so the threshold on the real speed is the minimum over the
    /// square root of the step length. The minimum is at least 0.001.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_VelocityDecay">C_OP_VelocityDecay</seealso>
    class VelocityDecay : ParticleFunctionOperator
    {
        private readonly float minVelocity = 1f;
        public VelocityDecay(ParticleDefinitionParser parse) : base(parse)
        {
            minVelocity = MathF.Max(0.001f, parse.Float("m_flMinVelocity", minVelocity));
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var threshold = minVelocity * minVelocity;

            foreach (var particle in particles.Current)
            {
                if ((particle.Position - particle.PositionPrevious).LengthSquared() / frameTime <= threshold)
                {
                    particle.Kill();
                }
            }
        }
    }
}
