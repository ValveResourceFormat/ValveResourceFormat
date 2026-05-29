using System.Numerics;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Describes a model's level-of-detail (LOD) structure: which meshes belong to which LOD level
    /// and the per-level switch values. Built once from the compiled model's
    /// <c>m_refLODGroupMasks</c> (bit N set =&gt; mesh is in LOD level N) and
    /// <c>m_lodGroupSwitchDistances</c>.
    /// </summary>
    public sealed class ModelLodInfo
    {
        private readonly long[] meshLodMasks;

        /// <summary>
        /// Gets the per-level switch values. Index N is the value at which LOD level N becomes active.
        /// These are the engine's screen-size metric (<c>100 / on-screen size of a unit sphere</c>),
        /// not world units. Empty for models without LOD switch data.
        /// </summary>
        public IReadOnlyList<float> SwitchDistances { get; }

        /// <summary>Gets the bitwise OR of every mesh's LOD mask.</summary>
        public long CombinedMask { get; }

        /// <summary>
        /// Gets the lowest LOD level that actually contains meshes. Normally 0, but models that leave
        /// LOD0 empty return the next populated level. Returns 0 when no LOD bits are set.
        /// </summary>
        public int LowestLevel { get; }

        /// <summary>Gets the sorted distinct LOD levels present across all meshes, e.g. <c>[0, 1, 2]</c>.</summary>
        public IReadOnlyList<int> AvailableLevels { get; }

        /// <summary>
        /// Gets the total number of LOD levels the model declares, including empty levels
        /// (e.g. a misconfigured empty LOD0).
        /// </summary>
        public int LevelCount { get; }

        /// <summary>
        /// Initializes LOD info from a model's mesh LOD masks (<c>m_refLODGroupMasks</c>) and switch
        /// distances (<c>m_lodGroupSwitchDistances</c>).
        /// </summary>
        public ModelLodInfo(long[] meshLodMasks, float[] switchDistances)
        {
            this.meshLodMasks = meshLodMasks ?? [];
            SwitchDistances = switchDistances ?? [];

            var combined = 0L;
            foreach (var mask in this.meshLodMasks)
            {
                combined |= mask;
            }

            CombinedMask = combined;
            LowestLevel = LowestSetLevel(combined);

            var levels = new List<int>();
            var bits = (ulong)combined;
            for (var level = 0; bits != 0; level++, bits >>= 1)
            {
                if ((bits & 1) != 0)
                {
                    levels.Add(level);
                }
            }

            AvailableLevels = levels;

            var fromMask = combined == 0 ? 0 : 64 - BitOperations.LeadingZeroCount((ulong)combined);
            LevelCount = Math.Max(fromMask, SwitchDistances.Count);
        }

        /// <summary>
        /// Determines whether the mesh at <paramref name="meshIndex"/> is present in LOD level
        /// <paramref name="level"/>. Meshes without a mask entry are treated as always present.
        /// </summary>
        public bool IsMeshInLevel(int meshIndex, int level)
            => meshIndex >= meshLodMasks.Length || (meshLodMasks[meshIndex] & 1L << level) != 0;

        /// <summary>
        /// Selects the LOD level for a given screen-size metric, mirroring Source's
        /// <c>GetLODForMetric</c>: the highest available level whose switch value the metric has
        /// reached, never below <see cref="LowestLevel"/>.
        /// </summary>
        public int SelectLevel(float metric)
        {
            var target = LowestLevel;

            foreach (var level in AvailableLevels)
            {
                if (level < SwitchDistances.Count && SwitchDistances[level] <= metric)
                {
                    target = level;
                }
            }

            return target;
        }

        /// <summary>Returns the index of the lowest set bit in <paramref name="combinedMask"/>, or 0 if none.</summary>
        public static int LowestSetLevel(long combinedMask)
            => combinedMask == 0 ? 0 : BitOperations.TrailingZeroCount((ulong)combinedMask);
    }
}
