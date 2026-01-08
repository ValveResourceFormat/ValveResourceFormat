namespace GUI.Types.ParticleRenderer.Operators
{
    /// <summary>
    /// Cull particle when its velocity is below a certain threshold.
    /// </summary>
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
