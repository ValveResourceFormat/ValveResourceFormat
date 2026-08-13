namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Initializes the particle animation sequence to a value between a min and max sequence index.
    /// Supports sequential (linear), shuffled, or pure-random selection modes.
    /// </summary>
    /// <remarks>
    /// A weighted list, where one is authored, replaces the range entirely: the sequences come from the
    /// list and are picked in proportion to their weights, and the min and max are widened to span them.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RandomSequence">C_INIT_RandomSequence</seealso>
    class RandomSequence : ParticleFunctionInitializer
    {
        private readonly int sequenceMin;
        private readonly int sequenceMax;
        private readonly bool shuffle;
        private readonly bool linear;

        private readonly (int Sequence, float Weight)[] weightedList = [];
        private readonly float totalWeight;
        private readonly int initialWeightedCursor;
        private int weightedCursor;

        private readonly int[] list = [];
        private int current;

        public RandomSequence(ParticleDefinitionParser parse) : base(parse)
        {
            sequenceMin = parse.Int32("m_nSequenceMin", sequenceMin);
            sequenceMax = parse.Int32("m_nSequenceMax", sequenceMax);
            shuffle = parse.Boolean("m_bShuffle", shuffle);
            linear = parse.Boolean("m_bLinear", linear);

            var entries = parse.Array("m_WeightedList");

            if (entries.Length > 0)
            {
                weightedList = new (int, float)[entries.Length];
                sequenceMin = int.MaxValue;
                sequenceMax = 0;

                for (var i = 0; i < entries.Length; i++)
                {
                    var sequence = entries[i].Int32("m_nSequence");
                    var weight = entries[i].Float("m_flRelativeWeight", 1f);

                    weightedList[i] = (sequence, weight);
                    sequenceMin = Math.Min(sequenceMin, sequence);
                    sequenceMax = Math.Max(sequenceMax, sequence);

                    if (weight > 0f)
                    {
                        totalWeight += weight;
                    }
                }

                initialWeightedCursor = shuffle || linear ? sequenceMin : 0;
                weightedCursor = initialWeightedCursor;
            }
            else if (shuffle || linear)
            {
                var count = Math.Max(sequenceMax - sequenceMin + 1, 1);
                list = new int[count];
                for (var i = 0; i < count; i++)
                {
                    list[i] = sequenceMin + i;
                }

                current = list.Length;
            }
        }

        public override void Reset()
        {
            current = list.Length;
            weightedCursor = initialWeightedCursor;
        }

        public override ulong WrittenFields => FieldMask(ParticleField.SequenceNumber);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            if (weightedList.Length > 0)
            {
                particle.SequenceNumber = NextWeightedSequence(particleSystemState);
            }
            else if (shuffle || linear)
            {
                if (current >= list.Length)
                {
                    if (shuffle)
                    {
                        Shuffle(particleSystemState);
                    }

                    current = 0;
                }

                particle.SequenceNumber = list[current];
                current++;
            }
            else
            {
                var sample = particleSystemState.Random.Next();

                particle.SequenceNumber = sequenceMax > sequenceMin
                    ? Math.Min(sequenceMin + (int)(sample * (sequenceMax - sequenceMin + 1)), sequenceMax)
                    : sequenceMin;
            }

            return particle;
        }

        /// <summary>
        /// Picks the next entry from the weighted list. A linear list walks it with the running cursor
        /// and takes no random draw at all; otherwise a single draw is scaled by the total weight.
        /// </summary>
        private int NextWeightedSequence(ParticleSystemRenderState particleSystemState)
        {
            var sequence = 0;

            if (totalWeight > 0f)
            {
                var pick = linear
                    ? weightedCursor
                    : totalWeight * particleSystemState.Random.Next();

                foreach (var (entrySequence, weight) in weightedList)
                {
                    if (weight <= 0f)
                    {
                        continue;
                    }

                    pick -= weight;
                    sequence = entrySequence;

                    if (pick < 0f)
                    {
                        break;
                    }
                }
            }

            if (linear)
            {
                weightedCursor++;

                if (weightedCursor >= MathF.Ceiling(totalWeight))
                {
                    weightedCursor = 0;
                }
            }

            return sequence;
        }

        private void Shuffle(ParticleSystemRenderState particleSystemState)
        {
            for (var i = list.Length - 1; i > 0; i--)
            {
                var j = Math.Min((int)(particleSystemState.Random.Next() * (i + 1)), i);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
