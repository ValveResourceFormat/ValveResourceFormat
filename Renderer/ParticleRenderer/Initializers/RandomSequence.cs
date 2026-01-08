namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    class RandomSequence : ParticleFunctionInitializer
    {
        private readonly int sequenceMin;
        private readonly int sequenceMax;
        private readonly bool shuffle;

        private int counter;

        // In Behavior Ver 12+ there is a "weight list" that weights the randomness
        public RandomSequence(ParticleDefinitionParser parse) : base(parse)
        {
            sequenceMin = parse.Int32("m_nSequenceMin", sequenceMin);
            sequenceMax = parse.Int32("m_nSequenceMax", sequenceMax);
            shuffle = parse.Boolean("m_bShuffle", shuffle);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            if (shuffle)
            {
                particle.Sequence = Random.Shared.Next(sequenceMin, sequenceMax + 1);
            }
            else
            {
                particle.Sequence = sequenceMin + (sequenceMax > sequenceMin ? (counter++ % (sequenceMax - sequenceMin + 1)) : 0);
            }

            return particle;
        }
    }
}
