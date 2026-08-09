using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Sets a particle attribute at spawn from a control-point snapshot, reading the snapshot element
    /// at the particle's spawn ordinal (wrapping) or at a random one.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitFromCPSnapshot">C_INIT_InitFromCPSnapshot</seealso>
    class InitFromCPSnapshot : ParticleFunctionInitializer
    {
        private readonly SnapshotRead snapshot;
        private readonly bool localSpaceAngles;

        /// <summary>Manual row to start from. -1 means none, and negative values walk the rows in order.</summary>
        private readonly INumberProvider startIndex = new LiteralNumberProvider(-1);

        public InitFromCPSnapshot(ParticleDefinitionParser parse) : base(parse)
        {
            snapshot = new SnapshotRead(parse);
            localSpaceAngles = parse.Boolean("m_bLocalSpaceAngles", localSpaceAngles);
            startIndex = parse.NumberProvider("m_nManualSnapshotIndex", startIndex);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var column = snapshot.Column(particleSystemState);
            var rowCount = snapshot.RowCount(particleSystemState);

            if (column == null || rowCount == 0)
            {
                return particle;
            }

            var startPoint = Math.Max(0, startIndex.NextInt(ref particle, particleSystemState));
            var row = snapshot.SelectRow(ref particle, rowCount, startPoint, particleSystemState);

            // A Position write is mirrored into PositionPrevious, and a velocity write goes through
            // Particle.Velocity for the emit path's Verlet encoding.
            CPSnapshotSampler.WriteAttribute(ref particle, snapshot.AttributeToWrite, column, row, snapshot.LocalSpaceCP,
                writePositionPrevious: true, atSpawn: true, 0f, particleSystemState, localSpaceAngles);

            return particle;
        }
    }
}
