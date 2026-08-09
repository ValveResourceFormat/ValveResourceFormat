using System.Collections;

namespace ValveResourceFormat.Renderer.Particles.Utils
{
    /// <summary>
    /// The half of <c>C_INIT_InitFromCPSnapshot</c> and <c>C_OP_SetFromCPSnapshot</c> that is the same
    /// on both: which snapshot to read, which of its columns, which particle attribute to write, and
    /// how to walk the rows. What is left to each of them is when the write happens and the handful of
    /// keys only one of them carries.
    /// </summary>
    class SnapshotRead
    {
        private readonly SnapshotBinding binding;
        private readonly bool randomRow;
        private readonly bool reverse;
        private readonly int randomSeed;
        private readonly INumberProvider increment = new LiteralNumberProvider(1);

        // Advanced once per random draw, so a fixed seed still walks the table rather than repeating
        private int sampleCounter;

        private bool columnResolved;
        private IEnumerable? column;

        /// <summary>The particle attribute the snapshot value is written to.</summary>
        public ParticleField AttributeToWrite { get; }

        /// <summary>The snapshot column the value is read from, defaulting to the written attribute.</summary>
        public ParticleField AttributeToRead { get; }

        /// <summary>Control point whose frame the written value is moved into, or negative for none.</summary>
        public int LocalSpaceCP { get; }

        public SnapshotRead(ParticleDefinitionParser parse)
        {
            binding = new SnapshotBinding(parse, "m_nControlPointNumber", 0);
            AttributeToWrite = parse.ParticleField("m_nAttributeToWrite", ParticleField.Position);
            AttributeToRead = parse.ParticleField("m_nAttributeToRead", AttributeToWrite);
            LocalSpaceCP = parse.Int32("m_nLocalSpaceCP", 0);
            randomRow = parse.Boolean("m_bRandom", false);
            reverse = parse.Boolean("m_bReverse", false);
            randomSeed = parse.Int32("m_nRandomSeed", 0);
            increment = parse.NumberProvider("m_nSnapShotIncrement", increment);
        }

        /// <summary>
        /// The column to read, resolved on first use and null when the bound snapshot is missing or
        /// carries no such column. A null column means the function has nothing to do.
        /// </summary>
        public IEnumerable? Column(ParticleSystemRenderState particleSystemState)
        {
            if (!columnResolved)
            {
                columnResolved = true;
                column = binding.ResolveAttribute(particleSystemState, AttributeToRead);
            }

            return column;
        }

        /// <summary>How many rows the bound snapshot offers.</summary>
        public int RowCount(ParticleSystemRenderState particleSystemState) => binding.Count(particleSystemState);

        /// <summary>
        /// Picks the row this particle reads, walking from <paramref name="startPoint"/> or drawing at
        /// random. The two functions author the start point under different keys, so it comes in as an
        /// argument rather than being parsed here.
        /// </summary>
        public int SelectRow(ref Particle particle, int rowCount, int startPoint, ParticleSystemRenderState particleSystemState)
        {
            return CPSnapshotSampler.SelectIndex(particle.UniqueParticleId, rowCount, randomRow, reverse, startPoint,
                increment.NextInt(ref particle, particleSystemState), randomSeed, ref sampleCounter, particleSystemState);
        }
    }
}
