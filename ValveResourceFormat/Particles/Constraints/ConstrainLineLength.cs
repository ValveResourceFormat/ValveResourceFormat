namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// Treats consecutive particles as the two ends of a line, the first with the second, the third
    /// with the fourth and so on, and holds each line's length between <c>m_flMinDistance</c> and
    /// <c>m_flMaxDistance</c> by moving both ends evenly about its midpoint. Particles are handled in
    /// blocks of four, two blocks at a time, so with four particles or fewer nothing is held, and the
    /// particles of an odd last block are left alone. Only <see cref="ParticleField.Position"/> is written.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ConstrainLineLength">C_OP_ConstrainLineLength</seealso>
    class ConstrainLineLength : ParticleFunctionConstraint
    {
        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Position.Mask();

        private const int BlockSize = 4;

        private readonly float minDistance = 1f;
        private readonly float maxDistance = 10f;

        public ConstrainLineLength(ParticleDefinitionParser parse) : base(parse)
        {
            minDistance = parse.Float("m_flMinDistance", minDistance);
            maxDistance = parse.Float("m_flMaxDistance", maxDistance);
        }

        public override bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;
            var blocks = MathUtils.DivideRoundUp(current.Length, BlockSize);

            if (blocks < 2)
            {
                return false;
            }

            var handled = Math.Min(current.Length, blocks / 2 * 2 * BlockSize);
            var positions = current.Vectors(ParticleField.Position);

            for (var i = 0; i + 1 < handled; i += 2)
            {
                ref var first = ref positions[i];
                ref var second = ref positions[i + 1];

                var delta = first - second;
                var length = delta.Length();

                if (length == 0f)
                {
                    continue;
                }

                var middle = (first + second) * 0.5f;
                var half = delta * (0.5f * MathF.Max(minDistance, MathF.Min(maxDistance, length)) / length);

                first = middle + half;
                second = middle - half;
            }

            return true;
        }
    }
}
