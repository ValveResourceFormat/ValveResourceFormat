using System.Collections;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// The snapshot a particle function reads from, bound to a control point and optionally narrowed
    /// to a named range of its rows by <c>m_strSnapshotSubset</c>. Resolved on first use, because
    /// control points carry no snapshot until the system runs.
    ///
    /// <para>Emitters author <c>m_nSnapshotControlPoint</c> (default none), while snapshot readers
    /// author <c>m_nControlPointNumber</c> (default 0).</para>
    ///
    /// <para>Subsets are row ranges the game registers on a live snapshot through the particle system
    /// manager. A compiled <c>.vsnap</c> does not carry them, so a function that authors one is
    /// unbound rather than reading the whole table, which would always be too many rows.</para>
    /// </summary>
    class SnapshotBinding
    {
        private readonly int controlPoint;
        private readonly bool hasSubset;

        public SnapshotBinding(ParticleDefinitionParser parse, string controlPointKey = "m_nSnapshotControlPoint", int defaultControlPoint = -1)
        {
            controlPoint = parse.Int32(controlPointKey, defaultControlPoint);
            hasSubset = !string.IsNullOrEmpty(parse.Data.GetStringProperty("m_strSnapshotSubset"));
        }

        /// <summary>
        /// Loads the snapshot a particle system authors as <c>m_hSnapshot</c>, which it publishes on a
        /// control point for its own functions and its children to read. This is the producing side of
        /// a binding; everything else on this class is the consuming side.
        /// </summary>
        public static ParticleSnapshot? LoadAuthored(ParticleDefinitionParser parse, IFileLoader fileLoader)
        {
            if (!parse.Data.ContainsKey("m_hSnapshot"))
            {
                return null;
            }

            var path = parse.Data.GetStringProperty("m_hSnapshot");

            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            return fileLoader.LoadFileCompiled(path)?.GetBlockByType(BlockType.SNAP) as ParticleSnapshot;
        }

        /// <summary>Whether a snapshot control point is authored at all, supported or not.</summary>
        public bool HasControlPoint => controlPoint >= 0;

        /// <summary>Whether a usable snapshot control point is authored.</summary>
        public bool IsBound => HasControlPoint && !hasSubset;

        /// <summary>
        /// The bound snapshot, or null when none is authored, the control point carries none, or the
        /// function authors a subset. Looked up on every call.
        /// </summary>
        public ParticleSnapshot? Resolve(ParticleSystemState particleSystemState)
            => IsBound ? particleSystemState.GetControlPointSnapshot(controlPoint) : null;

        /// <summary>How many rows the bound snapshot offers, or 0 when nothing is bound.</summary>
        public int Count(ParticleSystemState particleSystemState)
            => (int)(Resolve(particleSystemState)?.NumParticles ?? 0);

        /// <summary>
        /// The bound snapshot's column holding <paramref name="field"/>, or null when the field has no
        /// snapshot representation or the snapshot does not carry that column.
        /// </summary>
        public IEnumerable? ResolveAttribute(ParticleSystemState particleSystemState, ParticleField field)
        {
            var resolvedSnapshot = Resolve(particleSystemState);

            if (resolvedSnapshot == null)
            {
                return null;
            }

            var attributeName = ParticleSnapshot.GetSnapshotAttributeName(field);

            if (attributeName == null)
            {
                return null;
            }

            foreach (var ((name, _), data) in resolvedSnapshot.AttributeData)
            {
                if (name == attributeName)
                {
                    return data;
                }
            }

            return null;
        }
    }
}
