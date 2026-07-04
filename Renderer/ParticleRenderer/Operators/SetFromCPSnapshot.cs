using System.Collections;
using ValveResourceFormat.Blocks;
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
        private readonly int ControlPointNumber;
        private readonly ParticleField AttributeToWrite;
        private readonly ParticleField AttributeToRead;
        private readonly int LocalSpaceCP = -1;
        private readonly bool Random;
        private readonly bool Reverse;
        private readonly bool WritePrevious;
        private readonly INumberProvider StartPoint = new LiteralNumberProvider(0);
        private readonly INumberProvider Increment = new LiteralNumberProvider(1);

        private bool snapshotResolved;
        private ParticleSnapshot? cachedSnapshot;
        private IEnumerable? readAttributeData;

        public SetFromCPSnapshot(ParticleDefinitionParser parse) : base(parse)
        {
            ControlPointNumber = parse.Int32("m_nControlPointNumber", 0);
            AttributeToWrite = parse.ParticleField("m_nAttributeToWrite", ParticleField.Position);
            AttributeToRead = parse.ParticleField("m_nAttributeToRead", AttributeToWrite);
            LocalSpaceCP = parse.Int32("m_nLocalSpaceCP", LocalSpaceCP);
            Random = parse.Boolean("m_bRandom", false);
            Reverse = parse.Boolean("m_bReverse", false);
            WritePrevious = parse.Boolean("m_bPrev", false);
            StartPoint = parse.NumberProvider("m_nSnapShotStartPoint", StartPoint);
            Increment = parse.NumberProvider("m_nSnapShotIncrement", Increment);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            if (!snapshotResolved)
            {
                ResolveSnapshot(particleSystemState);
            }

            if (cachedSnapshot == null || readAttributeData == null)
            {
                return;
            }

            var numParticles = (int)cachedSnapshot.NumParticles;
            if (numParticles == 0)
            {
                return;
            }

            foreach (ref var particle in particles.Current)
            {
                var idx = CPSnapshotSampler.SelectIndex(particle.ParticleID, numParticles, Random, Reverse,
                    StartPoint.NextInt(ref particle, particleSystemState), Increment.NextInt(ref particle, particleSystemState));
                CPSnapshotSampler.WriteAttribute(ref particle, AttributeToWrite, readAttributeData, idx, LocalSpaceCP, WritePrevious, atSpawn: false, frameTime, particleSystemState);
            }
        }

        private void ResolveSnapshot(ParticleSystemRenderState particleSystemState)
        {
            snapshotResolved = true;
            cachedSnapshot = particleSystemState.GetControlPointSnapshot(ControlPointNumber);

            if (cachedSnapshot == null)
            {
                return;
            }

            var readAttributeName = ParticleSnapshot.GetSnapshotAttributeName(AttributeToRead);
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
