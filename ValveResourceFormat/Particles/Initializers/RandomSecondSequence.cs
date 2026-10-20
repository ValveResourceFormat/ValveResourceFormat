namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets the second sequence to a random index between min and max, inclusive. Always draws,
    /// even when the range is degenerate.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RandomSecondSequence">C_INIT_RandomSecondSequence</seealso>
    class RandomSecondSequence : ParticleFunctionInitializer
    {
        private readonly int sequenceMin;
        private readonly int sequenceMax;

        public RandomSecondSequence(ParticleDefinitionParser parse) : base(parse)
        {
            sequenceMin = parse.Int32("m_nSequenceMin", sequenceMin);
            sequenceMax = parse.Int32("m_nSequenceMax", sequenceMax);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.SecondSequenceNumber);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var sample = particleSystemState.Random.Next();

            particle.SecondSequenceNumber = sequenceMax > sequenceMin
                ? Math.Min(sequenceMin + (int)(sample * (sequenceMax - sequenceMin + 1)), sequenceMax)
                : sequenceMin;

            return particle;
        }
    }
}
