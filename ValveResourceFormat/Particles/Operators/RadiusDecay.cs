namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Kills particles once their radius has shrunk to the minimum.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RadiusDecay">C_OP_RadiusDecay</seealso>
    class RadiusDecay : ParticleFunctionOperator
    {
        private readonly float minRadius = 1f;

        public RadiusDecay(ParticleDefinitionParser parse) : base(parse)
        {
            minRadius = parse.Float("m_flMinRadius", minRadius);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                if (particle.Radius <= minRadius)
                {
                    particle.Kill();
                }
            }
        }
    }
}
