using ValveResourceFormat.Blocks;
using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles
{

    /// <summary>Snapshots this system publishes to its control points.</summary>
    public partial class ParticleSystemSimulation
    {
        /// <summary>
        /// Binds the snapshot to its control point on this system and every child, replacing whatever
        /// a child bound to the same control point itself. A runtime snapshot handed in at construction
        /// (the cable node's rope points) overrides the authored <c>m_hSnapshot</c>.
        /// </summary>
        private void PublishSnapshot(ParticleDefinitionParser parse, ParticleSnapshot? runtimeSnapshot)
        {
            var controlPoint = parse.Int32("m_nSnapshotControlPoint", 0);
            var snapshot = runtimeSnapshot ?? SnapshotBinding.LoadAuthored(parse, fileLoader);

            if (snapshot != null)
            {
                systemState.SetControlPointSnapshot(controlPoint, snapshot);
            }
        }

        /// <summary>
        /// Replaces the control point's snapshot with one built at runtime, on this system and every
        /// child. Readers pick it up on next use.
        /// </summary>
        internal void SetControlPointSnapshot(int controlPoint, ParticleSnapshot snapshot)
            => systemState.SetControlPointSnapshot(controlPoint, snapshot);
    }
}
