using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents a local-hierarchy override of an animation (<c>m_hierarchyArray</c>): for the given
    /// frame envelope, sub-frame interpolation of <see cref="Bone"/> happens in the space of
    /// <see cref="NewParent"/> instead of its skeleton parent. An empty new parent means model space -
    /// how death animations detach a dropped weapon or limb from the root.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/animationsystem/CAnimLocalHierarchy">CAnimLocalHierarchy</seealso>
    public readonly struct AnimationLocalHierarchy
    {
        /// <summary>
        /// Gets the bone whose interpolation space changes.
        /// </summary>
        public string Bone { get; init; }

        /// <summary>
        /// Gets the bone providing the new interpolation space; empty for model (world) space.
        /// </summary>
        public string NewParent { get; init; }

        /// <summary>
        /// Gets the frame the override starts at.
        /// </summary>
        public int StartFrame { get; init; }

        /// <summary>
        /// Gets the frame the override reaches full effect at.
        /// </summary>
        public int PeakFrame { get; init; }

        /// <summary>
        /// Gets the last frame of full effect.
        /// </summary>
        public int TailFrame { get; init; }

        /// <summary>
        /// Gets the frame the override ends at.
        /// </summary>
        public int EndFrame { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AnimationLocalHierarchy"/> struct.
        /// </summary>
        public AnimationLocalHierarchy(KVObject data)
        {
            Bone = data.GetStringProperty("m_sBone");
            NewParent = data.GetStringProperty("m_sNewParent");
            StartFrame = data.GetInt32Property("m_nStartFrame");
            PeakFrame = data.GetInt32Property("m_nPeakFrame");
            TailFrame = data.GetInt32Property("m_nTailFrame");
            EndFrame = data.GetInt32Property("m_nEndFrame");
        }
    }
}
