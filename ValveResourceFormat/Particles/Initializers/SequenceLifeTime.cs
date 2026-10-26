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

        public override ulong WrittenFields => ParticleField.LifeDuration.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var durations = particleSystemState.SequenceDurations;

            if (frameRate == 0f || durations == null)
            {
                return;
            }

            var sequenceNumbers = run.ReadFloats(ParticleField.SequenceNumber);
            var lifetimes = run.Floats(ParticleField.LifeDuration);

            for (var i = 0; i < lifetimes.Length; i++)
            {
                // A NaN sequence number reaches no sequence rather than converting to sequence 0
                var sequenceNumber = sequenceNumbers[i];
                var sequence = float.IsNaN(sequenceNumber) ? -1 : (int)sequenceNumber;
                var totalTime = sequence >= 0 && sequence < durations.Length
                    ? durations[sequence]
                    : 0f;

                lifetimes[i] = totalTime != 0f ? totalTime / frameRate : LifetimeWithoutSequence;
            }
        }
    }
}
