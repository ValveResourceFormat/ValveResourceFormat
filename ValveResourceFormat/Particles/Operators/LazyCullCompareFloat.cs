namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Caps a particle's remaining life at a cull time when a comparison matches, a kill left to the
    /// next lifespan decay.
    /// </summary>
    /// <remarks>
    /// The comparison is between two values of <c>m_flComparsion1</c>: particle i compares those of
    /// particles 2i and 2i + 1, so an input with one value for every particle always matches. Only the
    /// particles past the middle, which have no such pair, compare <c>m_flComparsion1</c> with
    /// <c>m_flComparsion2</c>. Operator strength is not read.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LazyCullCompareFloat">C_OP_LazyCullCompareFloat</seealso>
    class LazyCullCompareFloat : ParticleFunctionOperator
    {
        private readonly INumberProvider comparison1 = new LiteralNumberProvider(0f);
        private readonly INumberProvider comparison2 = new LiteralNumberProvider(0f);
        private readonly INumberProvider cullTime = new LiteralNumberProvider(0f);

        public LazyCullCompareFloat(ParticleDefinitionParser parse) : base(parse)
        {
            comparison1 = parse.NumberProvider("m_flComparsion1", comparison1);
            comparison2 = parse.NumberProvider("m_flComparsion2", comparison2);
            cullTime = parse.NumberProvider("m_flCullTime", cullTime);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.LifeDuration.Mask() | ParticleField.CreationTime.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;
            var lifetimes = current.Floats(ParticleField.LifeDuration);

            var first = comparison1.Fill(current, particleSystemState);
            var second = comparison2.Fill(current, particleSystemState);
            var cullTimes = cullTime.Fill(current, particleSystemState);

            var ages = current.ReadAges();

            var uniform = !first.ContainsAnyExcept(first[0]);

            for (var i = 0; i < lifetimes.Length; i++)
            {
                if (Matches(first, second, i, uniform))
                {
                    var age = ages[i];
                    lifetimes[i] = MathF.Min(lifetimes[i] - age, cullTimes[i]) + age;
                }
            }
        }

        private static bool Matches(ReadOnlySpan<float> first, ReadOnlySpan<float> second, int index, bool uniform)
        {
            if (uniform)
            {
                return !float.IsNaN(first[0]);
            }

            // Past the middle the pair would run beyond the evaluated values, so m_flComparsion2 is compared instead
            var pair = 2 * index;

            return pair + 1 < first.Length
                ? first[pair] == first[pair + 1]
                : first[index] == second[index];
        }
    }
}
