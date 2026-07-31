namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Initializes the particle animation sequence to a value between a min and max sequence index.
    /// Supports sequential (linear), shuffled, or pure-random selection modes.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RandomSequence">C_INIT_RandomSequence</seealso>
    class RandomSequence : ParticleFunctionInitializer
    {
        private readonly int sequenceMin;
        private readonly int sequenceMax;
        private readonly bool shuffle;
        private readonly bool linear;

        private readonly int[] list = [];
        private int current;

        // In Behavior Ver 12+ there is a "weight list" that weights the randomness
        public RandomSequence(ParticleDefinitionParser parse) : base(parse)
        {
            sequenceMin = parse.Int32("m_nSequenceMin", sequenceMin);
            sequenceMax = parse.Int32("m_nSequenceMax", sequenceMax);
            shuffle = parse.Boolean("m_bShuffle", shuffle);
            linear = parse.Boolean("m_bLinear", linear);

            if (shuffle || linear)
            {
                var count = Math.Max(sequenceMax - sequenceMin + 1, 1);
                list = new int[count];
                for (var i = 0; i < count; i++)
                {
                    list[i] = sequenceMin + i;
                }

                if (shuffle)
                {
                    Shuffle();
                }
            }
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            if (shuffle || linear)
            {
                if (current >= list.Length)
                {
                    if (shuffle)
                    {
                        Shuffle();
                    }

                    current = 0;
                }

                particle.Sequence = list[current];
                current++;
            }
            else
            {
                particle.Sequence = sequenceMax > sequenceMin ? Random.Shared.Next(sequenceMin, sequenceMax + 1) : sequenceMin;
            }

            return particle;
        }

        private void Shuffle()
        {
            for (var i = list.Length - 1; i > 0; i--)
            {
                var j = Random.Shared.Next(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
