using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Each frame, writes a particle attribute from a control-point snapshot. Used (for example) to keep
    /// rope/cable pin flags (<see cref="ParticleField.ForceScale"/>) asserted so pinned particles stay
    /// immovable as the simulation runs. Reads the snapshot element at the particle's id (wrapping).
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetFromCPSnapshot">C_OP_SetFromCPSnapshot</seealso>
    class SetFromCPSnapshot : ParticleFunctionOperator
    {
        private readonly SnapshotRead snapshot;
        private readonly bool writePrevious = true;
        private readonly INumberProvider startPoint = new LiteralNumberProvider(0);

        public SetFromCPSnapshot(ParticleDefinitionParser parse) : base(parse)
        {
            snapshot = new SnapshotRead(parse);
            writePrevious = parse.Boolean("m_bPrev", writePrevious);
            startPoint = parse.NumberProvider("m_nSnapShotStartPoint", startPoint);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            var column = snapshot.Column(particleSystemState);
            var rowCount = snapshot.RowCount(particleSystemState);

            if (column == null || rowCount == 0)
            {
                return;
            }

            foreach (ref var particle in particles.Current)
            {
                var row = snapshot.SelectRow(ref particle, rowCount, startPoint.NextInt(ref particle, particleSystemState), particleSystemState);

                CPSnapshotSampler.WriteAttribute(ref particle, snapshot.AttributeToWrite, column, row, snapshot.LocalSpaceCP,
                    writePrevious, atSpawn: false, frameTime, particleSystemState);
            }
        }
    }
}
