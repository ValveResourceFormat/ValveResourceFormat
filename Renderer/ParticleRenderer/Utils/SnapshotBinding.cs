using System.Collections;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Utils
{
    /// <summary>
    /// The snapshot a particle function reads from, bound to a control point and optionally narrowed
    /// to a named range of its rows by <c>m_strSnapshotSubset</c>. The binding resolves on first use
    /// rather than at parse time, because control points carry no snapshot until the system runs.
    ///
    /// <para>The control point key differs by function: the emitters author
    /// <c>m_nSnapshotControlPoint</c> and default to none, while the snapshot readers author
    /// <c>m_nControlPointNumber</c> and default to control point 0.</para>
    ///
    /// <para>Subsets are named row ranges the game registers on a live snapshot through the particle
    /// system manager. Nothing in a compiled <c>.vsnap</c> carries them, so a function that authors
    /// one is reported as unbound rather than reading the whole table, which would always be too many
    /// rows.</para>
    /// </summary>
    class SnapshotBinding
    {
        private readonly int controlPoint;
        private readonly bool hasSubset;

        private bool resolved;
        private ParticleSnapshot? snapshot;

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
        /// function authors a subset.
        /// </summary>
        public ParticleSnapshot? Resolve(ParticleSystemRenderState particleSystemState)
        {
            if (!resolved)
            {
                resolved = true;

                if (IsBound)
                {
                    snapshot = particleSystemState.GetControlPointSnapshot(controlPoint);
                }
            }

            return snapshot;
        }

        /// <summary>How many rows the bound snapshot offers, or 0 when nothing is bound.</summary>
        public int Count(ParticleSystemRenderState particleSystemState)
            => (int)(Resolve(particleSystemState)?.NumParticles ?? 0);

        /// <summary>
        /// The bound snapshot's column holding <paramref name="field"/>, or null when the field has no
        /// snapshot representation or the snapshot does not carry that column.
        /// </summary>
        public IEnumerable? ResolveAttribute(ParticleSystemRenderState particleSystemState, ParticleField field)
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
