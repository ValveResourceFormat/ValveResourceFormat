using ValveResourceFormat.Blocks;
using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles
{

    /// <summary>Snapshots this system publishes to its control points.</summary>
    public partial class ParticleSystemSimulation
    {
        /// <summary>
        /// The snapshot bound to each control point: the system's own <c>m_hSnapshot</c> or an injected
        /// runtime one. Functions look it up through <see cref="ParticleSystemState"/>, which walks up
        /// to the parent.
        /// </summary>
        private readonly Dictionary<int, ParticleSnapshot> controlPointSnapshots = [];

        /// <summary>
        /// Binds the snapshot to its control point. A runtime snapshot handed in at construction (the cable
        /// node's rope points) overrides the authored <c>m_hSnapshot</c>.
        /// </summary>
        private void PublishSnapshot(ParticleDefinitionParser parse, ParticleSnapshot? runtimeSnapshot)
        {
            var controlPoint = parse.Int32("m_nSnapshotControlPoint", 0);
            var snapshot = runtimeSnapshot ?? SnapshotBinding.LoadAuthored(parse, fileLoader);

            if (snapshot != null)
            {
                controlPointSnapshots[controlPoint] = snapshot;
            }
        }

        /// <summary>
        /// Replaces the control point's snapshot with one built at runtime. Readers pick it up on next use.
        /// </summary>
        internal void SetControlPointSnapshot(int controlPoint, ParticleSnapshot snapshot)
        {
            controlPointSnapshots[controlPoint] = snapshot;
        }

        /// <summary>
        /// Gets the particle snapshot associated with the given control point, or null if none exists.
        /// </summary>
        internal ParticleSnapshot? GetControlPointSnapshot(int controlPoint)
        {
            controlPointSnapshots.TryGetValue(controlPoint, out var snap);
            return snap;
        }
    }
}
