using System.Collections;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// Shared half of <c>C_INIT_InitFromCPSnapshot</c> and <c>C_OP_SetFromCPSnapshot</c>: which
    /// snapshot to read, which of its columns, and which particle attribute to write. Each function
    /// adds how it walks the rows, when the write happens and the keys only it carries.
    /// </summary>
    class SnapshotRead
    {
        private readonly SnapshotBinding binding;

        private ParticleSnapshot? columnSnapshot;
        private IEnumerable? column;

        /// <summary>The particle attribute the snapshot value is written to.</summary>
        public ParticleField AttributeToWrite { get; }

        /// <summary>The snapshot column the value is read from, defaulting to the written attribute.</summary>
        public ParticleField AttributeToRead { get; }

        /// <summary>Control point whose frame the written value is moved into, or -1 for none.</summary>
        public int LocalSpaceCP { get; }

        public SnapshotRead(ParticleDefinitionParser parse)
        {
            binding = new SnapshotBinding(parse, "m_nControlPointNumber", 0, clampControlPoint: true);
            AttributeToWrite = parse.ParticleField("m_nAttributeToWrite", ParticleField.Position);

            var attributeToRead = parse.ParticleField("m_nAttributeToRead", AttributeToWrite);
            AttributeToRead = attributeToRead < 0 ? AttributeToWrite : attributeToRead;

            LocalSpaceCP = Math.Clamp(parse.Int32("m_nLocalSpaceCP", 0), -1, ParticleSystemState.MaxControlPoint);
        }

        /// <summary>The bound snapshot, or null when there is none.</summary>
        public ParticleSnapshot? Snapshot(ParticleSystemState particleSystemState) => binding.Resolve(particleSystemState);

        /// <summary>
        /// The column to read, looked up again whenever the bound snapshot is replaced, and null when
        /// the snapshot is missing or carries no such column. A null column means the function has
        /// nothing to do.
        /// </summary>
        public IEnumerable? Column(ParticleSystemState particleSystemState)
        {
            var snapshot = binding.Resolve(particleSystemState);

            if (!ReferenceEquals(snapshot, columnSnapshot))
            {
                columnSnapshot = snapshot;
                column = binding.ResolveAttribute(particleSystemState, AttributeToRead);
            }

            return column;
        }

        /// <summary>How many rows the bound snapshot offers.</summary>
        public int RowCount(ParticleSystemState particleSystemState) => binding.Count(particleSystemState);
    }
}
