namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets the sequence to a value between min and max, picked linearly, shuffled, or purely at random.
    /// </summary>
    /// <remarks>
    /// A weighted list replaces the range entirely: sequences are picked in proportion to weight,
    /// and min and max widen to span them.
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

        private const int TableSize = 64;

        /// <summary>The order a shuffled or linear pick walks, indexed by sequence number.</summary>
        private readonly int[] table = new int[TableSize];
        private int next;

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
        }

        /// <summary>Lays out the walk, shuffling it with draws from the system's table when asked to.</summary>
        public override void Initialize(ParticleSystemState particleSystemState)
        {
            weightedCursor = initialWeightedCursor;
            Array.Clear(table);
            next = 0;

            if (weightedList.Length > 0 || !(shuffle || linear))
            {
                return;
            }

            next = sequenceMin;

            for (var sequence = Math.Max(sequenceMin, 0); sequence <= Math.Min(sequenceMax, TableSize - 1); sequence++)
            {
                SetEntry(sequence, sequence);
            }

            if (shuffle)
            {
                Shuffle(particleSystemState);
            }
        }

        public override ulong WrittenFields => ParticleField.SequenceNumber.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var sequences = run.Floats(ParticleField.SequenceNumber);

            for (var i = 0; i < sequences.Length; i++)
            {
                if (weightedList.Length > 0)
                {
                    sequences[i] = NextWeightedSequence(particleSystemState);
                }
                else if (shuffle || linear)
                {
                    if (next > sequenceMax)
                    {
                        if (shuffle)
                        {
                            Shuffle(particleSystemState);
                        }

                        next = sequenceMin;
                    }

                    sequences[i] = GetEntry(next);
                    next++;
                }
                else
                {
                    sequences[i] = sequenceMin + particleSystemState.Random.NextIndex(sequenceMax - sequenceMin + 1);
                }
            }
        }

        /// <summary>
        /// Picks the next weighted entry. A linear list walks it with the cursor and draws no random value;
        /// otherwise one draw is scaled by the total weight.
        /// </summary>
        private int NextWeightedSequence(ParticleSystemState particleSystemState)
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

        private void Shuffle(ParticleSystemState particleSystemState)
        {
            for (var i = sequenceMax; i > sequenceMin; i--)
            {
                var j = sequenceMin + particleSystemState.Random.NextIndex(i - sequenceMin + 1);
                var swapped = GetEntry(i);
                SetEntry(i, GetEntry(j));
                SetEntry(j, swapped);
            }
        }

        private int GetEntry(int sequence) => (uint)sequence < TableSize ? table[sequence] : 0;

        private void SetEntry(int sequence, int value)
        {
            if ((uint)sequence < TableSize)
            {
                table[sequence] = value;
            }
        }
    }
}
