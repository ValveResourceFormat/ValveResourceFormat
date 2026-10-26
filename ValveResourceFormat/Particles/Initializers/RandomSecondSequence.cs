namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets the second sequence to a random index between min and max, inclusive. Always draws,
    /// even when the range is degenerate, and a max below min gives values below min.
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

        public override ulong WrittenFields => ParticleField.SecondSequenceNumber.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var sequences = run.Floats(ParticleField.SecondSequenceNumber);

            for (var i = 0; i < sequences.Length; i++)
            {
                sequences[i] = sequenceMin + particleSystemState.Random.NextIndex(sequenceMax - sequenceMin + 1);
            }
        }
    }
}
