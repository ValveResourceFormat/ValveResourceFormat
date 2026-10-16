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

        private float[] firstValues = [];
        private float[] secondValues = [];

        public LazyCullCompareFloat(ParticleDefinitionParser parse) : base(parse)
        {
            comparison1 = parse.NumberProvider("m_flComparsion1", comparison1);
            comparison2 = parse.NumberProvider("m_flComparsion2", comparison2);
            cullTime = parse.NumberProvider("m_flCullTime", cullTime);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;

            if (firstValues.Length < current.Length)
            {
                firstValues = new float[particles.Capacity];
                secondValues = new float[particles.Capacity];
            }

            for (var i = 0; i < current.Length; i++)
            {
                firstValues[i] = comparison1.NextNumber(ref current[i], particleSystemState);
            }

            for (var i = 0; i < current.Length; i++)
            {
                secondValues[i] = comparison2.NextNumber(ref current[i], particleSystemState);
            }

            var first = firstValues.AsSpan(0, current.Length);
            var uniform = !first.ContainsAnyExcept(first[0]);

            for (var i = 0; i < current.Length; i++)
            {
                ref var particle = ref current[i];
                var cull = cullTime.NextNumber(ref particle, particleSystemState);

                if (Matches(first, i, uniform))
                {
                    particle.Lifetime = MathF.Min(particle.Lifetime - particle.Age, cull) + particle.Age;
                }
            }
        }

        private bool Matches(ReadOnlySpan<float> first, int index, bool uniform)
        {
            if (uniform)
            {
                return !float.IsNaN(first[0]);
            }

            var pair = 2 * index;

            return pair + 1 < first.Length
                ? first[pair] == first[pair + 1]
                : first[index] == secondValues[index];
        }
    }
}
