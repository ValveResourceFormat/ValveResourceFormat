namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Sets a particle lifetime based on the configured animation frame rate.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_SequenceLifeTime">C_INIT_SequenceLifeTime</seealso>
    class SequenceLifeTime : ParticleFunctionInitializer
    {
        private readonly float frameRate = 30f;

        public SequenceLifeTime(ParticleDefinitionParser parse) : base(parse)
        {
            frameRate = parse.Float("m_flFramerate", frameRate);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var rate = Math.Max(0.0001f, frameRate);
            particle.Lifetime = 1f / rate;
            return particle;
        }
    }
}
