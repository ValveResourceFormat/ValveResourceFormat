namespace GUI.Types.ParticleRenderer.Operators
{
    /// <summary>
    /// Cull particle when its alpha is below a certain threshold.
    /// </summary>
    class AlphaDecay : IParticleOperator
    {
        private readonly float minAlpha;
        public AlphaDecay(ParticleDefinitionParser parse)
        {
            minAlpha = parse.Float("m_flMinAlpha", minAlpha);
        }

        public void Update(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                if (particle.Alpha <= minAlpha)
                {
                    particle.Kill();
                }
            }
        }
    }
}
