using System.Collections;
using ValveResourceFormat.Renderer.Particles.Utils;

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
        private readonly SnapshotBinding snapshotBinding;
        private readonly ParticleField AttributeToRead;
        private readonly ParticleField AttributeToWrite;
        private readonly int LocalSpaceCP;
        private readonly bool Random;
        private readonly bool Reverse;
        private readonly bool LocalSpaceAngles;
        private readonly int RandomSeed;

        // Advanced once per random draw, so a fixed seed still walks the table rather than repeating
        private int randomSampleCounter;
        // The manual index defaults to -1 = none; negative values fall back to the plain mapping.
        private readonly INumberProvider StartIndex = new LiteralNumberProvider(-1);
        private readonly INumberProvider Increment = new LiteralNumberProvider(1);

        private bool snapshotResolved;
        private IEnumerable? readAttributeData;

        public InitFromCPSnapshot(ParticleDefinitionParser parse) : base(parse)
        {
            snapshotBinding = new SnapshotBinding(parse, "m_nControlPointNumber", 0);
            AttributeToWrite = parse.ParticleField("m_nAttributeToWrite", ParticleField.Position);
            AttributeToRead = parse.ParticleField("m_nAttributeToRead", AttributeToWrite);
            LocalSpaceCP = parse.Int32("m_nLocalSpaceCP", 0);
            Random = parse.Boolean("m_bRandom", false);
            Reverse = parse.Boolean("m_bReverse", false);
            RandomSeed = parse.Int32("m_nRandomSeed", 0);
            LocalSpaceAngles = parse.Boolean("m_bLocalSpaceAngles", LocalSpaceAngles);
            StartIndex = parse.NumberProvider("m_nManualSnapshotIndex", StartIndex);
            Increment = parse.NumberProvider("m_nSnapShotIncrement", Increment);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            if (!snapshotResolved)
            {
                snapshotResolved = true;
                readAttributeData = snapshotBinding.ResolveAttribute(particleSystemState, AttributeToRead);
            }

            if (readAttributeData == null)
            {
                return particle;
            }

            var numParticles = snapshotBinding.Count(particleSystemState);

            if (numParticles == 0)
            {
                return particle;
            }

            var startPoint = Math.Max(0, StartIndex.NextInt(ref particle, particleSystemState));
            var increment = Increment.NextInt(ref particle, particleSystemState);
            var idx = Utils.CPSnapshotSampler.SelectIndex(particle.UniqueParticleId, numParticles, Random, Reverse, startPoint, increment,
                RandomSeed, ref randomSampleCounter, particleSystemState);
            // A Position write is always mirrored into PositionPrevious. A PREV_XYZ
            // (velocity) write goes through Particle.Velocity for the emit path's Verlet encoding.
            Utils.CPSnapshotSampler.WriteAttribute(ref particle, AttributeToWrite, readAttributeData, idx, LocalSpaceCP, true, atSpawn: true, 0f, particleSystemState, LocalSpaceAngles);

            return particle;
        }
    }
}
