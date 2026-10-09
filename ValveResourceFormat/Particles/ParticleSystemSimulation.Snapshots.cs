using ValveResourceFormat.Blocks;
using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles
{

    /// <summary>Snapshots this system publishes to its control points.</summary>
    public partial class ParticleSystemSimulation
    {
        /// <summary>
        /// The snapshot bound to each control point of this system. Only the system's own
        /// <c>m_hSnapshot</c> or an injected runtime one lands here; a function reading a snapshot
        /// looks it up through <see cref="ParticleSystemState"/>, which walks up to the parent.
        /// </summary>
        private readonly Dictionary<int, ParticleSnapshot> controlPointSnapshots = [];

        /// <summary>
        /// Binds this system's snapshot to the control point it publishes on, preferring one handed in
        /// at construction (the cable node builds its rope points that way) over the authored
        /// <c>m_hSnapshot</c> resource.
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
        /// Publishes a snapshot built while the system runs, replacing whatever the control point held.
        /// Readers resolve the control point again on their next use, so they pick up the new one.
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
