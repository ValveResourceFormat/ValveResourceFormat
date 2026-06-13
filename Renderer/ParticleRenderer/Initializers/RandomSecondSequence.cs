namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Initializes the particle second animation sequence to a random value between a min and max sequence index.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RandomSecondSequence">C_INIT_RandomSecondSequence</seealso>
    class RandomSecondSequence : ParticleFunctionInitializer
    {
        private readonly int sequenceMin;
        private readonly int sequenceMax;
        private readonly bool shuffle;

        private int counter;

        public RandomSecondSequence(ParticleDefinitionParser parse) : base(parse)
        {
            sequenceMin = parse.Int32("m_nSequenceMin", sequenceMin);
            sequenceMax = parse.Int32("m_nSequenceMax", sequenceMax);
            shuffle = parse.Boolean("m_bShuffle", shuffle);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            if (shuffle)
            {
                particle.Sequence2 = Random.Shared.Next(sequenceMin, sequenceMax + 1);
            }
            else
            {
                particle.Sequence2 = sequenceMin + (sequenceMax > sequenceMin ? (counter++ % (sequenceMax - sequenceMin + 1)) : 0);
            }

            return particle;
        }
    }
}
