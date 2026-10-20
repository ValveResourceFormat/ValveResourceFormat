namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Gives a particle exactly as long as its sheet sequence needs to play once at the configured
    /// frame rate, so a clamping sequence reaches its last frame as the particle dies.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_SequenceLifeTime">C_INIT_SequenceLifeTime</seealso>
    class SequenceLifeTime : ParticleFunctionInitializer
    {
        private const float LifetimeWithoutSequence = 1f;

        private readonly float frameRate = 30f;

        public SequenceLifeTime(ParticleDefinitionParser parse) : base(parse)
        {
            frameRate = parse.Float("m_flFramerate", frameRate);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.LifeDuration);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var durations = particleSystemState.SequenceDurations;

            if (frameRate == 0f || durations == null)
            {
                return particle;
            }

            var totalTime = particle.SequenceNumber >= 0 && particle.SequenceNumber < durations.Length
                ? durations[particle.SequenceNumber]
                : 0f;

            particle.Lifetime = totalTime > 0f ? totalTime / frameRate : LifetimeWithoutSequence;
            return particle;
        }
    }
}
