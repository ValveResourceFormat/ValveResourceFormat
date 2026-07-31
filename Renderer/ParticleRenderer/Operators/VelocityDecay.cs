namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Kills a particle when its speed falls at or below a minimum velocity threshold.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_VelocityDecay">C_OP_VelocityDecay</seealso>
    class VelocityDecay : ParticleFunctionOperator
    {
        private readonly float minVelocity;
        public VelocityDecay(ParticleDefinitionParser parse) : base(parse)
        {
            minVelocity = parse.Float("m_flMinVelocity", minVelocity);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                if (particle.Speed <= minVelocity)
                {
                    particle.Kill();
                }
            }
        }
    }
}
