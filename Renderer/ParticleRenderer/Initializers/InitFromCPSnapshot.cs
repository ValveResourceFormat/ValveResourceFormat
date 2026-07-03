using System.Collections;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Initializes particle attributes from a control-point-associated snapshot (.vsnap) file.
    /// Each particle reads data at its index from the snapshot, wrapping around when the particle
    /// count exceeds the snapshot size.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitFromCPSnapshot">C_INIT_InitFromCPSnapshot</seealso>
    class InitFromCPSnapshot : ParticleFunctionInitializer
    {
        private readonly int ControlPointNumber;
        private readonly ParticleField AttributeToRead;
        private readonly ParticleField AttributeToWrite;
        private readonly int LocalSpaceCP = -1;
        private readonly bool Random;
        private readonly bool Reverse;
        // The manual index defaults to -1 = none; negative values fall back to the plain mapping.
        private readonly INumberProvider StartIndex = new LiteralNumberProvider(-1);
        private readonly INumberProvider Increment = new LiteralNumberProvider(1);

        // Cached snapshot lookup
        private ParticleSnapshot? cachedSnapshot;
        private bool snapshotResolved;
        private string? readAttributeName;
        private IEnumerable? readAttributeData;

        public InitFromCPSnapshot(ParticleDefinitionParser parse) : base(parse)
        {
            ControlPointNumber = parse.Int32("m_nControlPointNumber", 0);
            AttributeToWrite = parse.ParticleField("m_nAttributeToWrite", ParticleField.Position);
            AttributeToRead = parse.ParticleField("m_nAttributeToRead", AttributeToWrite);
            LocalSpaceCP = parse.Int32("m_nLocalSpaceCP", LocalSpaceCP);
            Random = parse.Boolean("m_bRandom", false);
            Reverse = parse.Boolean("m_bReverse", false);
            StartIndex = parse.NumberProvider("m_nManualSnapshotIndex", StartIndex);
            Increment = parse.NumberProvider("m_nSnapShotIncrement", Increment);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            if (!snapshotResolved)
            {
                ResolveSnapshot(particleSystemState);
            }

            if (cachedSnapshot == null || readAttributeData == null)
            {
                return particle;
            }

            var numParticles = (int)cachedSnapshot.NumParticles;

            if (numParticles == 0)
            {
                return particle;
            }

            var startPoint = Math.Max(0, StartIndex.NextInt(ref particle, particleSystemState));
            var increment = Increment.NextInt(ref particle, particleSystemState);
            var idx = Utils.CPSnapshotSampler.SelectIndex(particle.ParticleID, numParticles, Random, Reverse, startPoint, increment);
            // A Position write is always mirrored into PositionPrevious. A PREV_XYZ
            // (velocity) write goes through Particle.Velocity for the emit path's Verlet encoding.
            Utils.CPSnapshotSampler.WriteAttribute(ref particle, AttributeToWrite, readAttributeData, idx, LocalSpaceCP, true, atSpawn: true, 0f, particleSystemState);

            return particle;
        }

        private void ResolveSnapshot(ParticleSystemRenderState particleSystemState)
        {
            snapshotResolved = true;
            cachedSnapshot = particleSystemState.GetControlPointSnapshot(ControlPointNumber);

            if (cachedSnapshot == null)
            {
                return;
            }

            readAttributeName = Utils.CPSnapshotFields.GetSnapshotAttributeName(AttributeToRead);

            if (readAttributeName == null)
            {
                return;
            }

            foreach (var ((name, _), data) in cachedSnapshot.AttributeData)
            {
                if (name == readAttributeName)
                {
                    readAttributeData = data;
                    return;
                }
            }
        }
    }
}
