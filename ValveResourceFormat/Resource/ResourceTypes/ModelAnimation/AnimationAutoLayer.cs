using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents an animation auto layer that defines blending and timing parameters for layered animations.
    /// Auto layers allow animations to be automatically blended together based on configured parameters.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/animationsystem/CSeqAutoLayer">CSeqAutoLayer</seealso>
    public class AnimationAutoLayer
    {
        /// <summary>
        /// Gets or sets the local reference index for the animation layer.
        /// </summary>
        public int LocalReference { get; set; }

        /// <summary>
        /// Gets the name <see cref="LocalReference"/> resolves to against the sequence group's shared
        /// name array, an animation for most layers or another sequence for one that blends generated
        /// animations. Empty when the layer was read outside sequence data, or the index is out of range.
        /// </summary>
        public string ReferencedAnimationName { get; internal set; } = string.Empty;

        /// <summary>
        /// Gets or sets the local pose index for the animation layer.
        /// </summary>
        public int LocalPose { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this layer is applied after (post) the base animation.
        /// </summary>
        public bool Post { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether spline interpolation is used for blending.
        /// </summary>
        public bool Spline { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether cross-fading is enabled for this layer.
        /// </summary>
        public bool XFade { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether blending is disabled for this layer.
        /// </summary>
        public bool NoBlend { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this layer uses local animation space.
        /// </summary>
        public bool Local { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this layer represents a pose.
        /// </summary>
        public bool Pose { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether frame fetching is enabled for this layer.
        /// </summary>
        public bool FetchFrame { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this layer's animation is subtracted from the base animation.
        /// </summary>
        public bool Subtract { get; set; }

        /// <summary>
        /// Gets or sets where the blend curve starts ramping in. A fraction of the referenced sequence's
        /// own cycle (0 at its first frame, 1 at its last), or a fraction along the pose parameter's range
        /// when <see cref="Pose"/> is set.
        /// </summary>
        public float Start { get; set; }

        /// <summary>
        /// Gets or sets where the blend curve reaches full influence. See <see cref="Start"/> for units.
        /// </summary>
        public float Peak { get; set; }

        /// <summary>
        /// Gets or sets where the blend curve starts fading out. See <see cref="Start"/> for units.
        /// </summary>
        public float Tail { get; set; }

        /// <summary>
        /// Gets or sets where the blend curve reaches zero influence. See <see cref="Start"/> for units.
        /// </summary>
        public float End { get; set; }

        /// <summary>
        /// Initializes a new instance of <see cref="AnimationAutoLayer"/> from a KeyValues object.
        /// </summary>
        /// <param name="autoLayerKV">The KeyValues object containing auto layer data.</param>
        public AnimationAutoLayer(KVObject autoLayerKV)
        {
            LocalReference = autoLayerKV.GetInt32Property("m_nLocalReference");
            LocalPose = autoLayerKV.GetInt32Property("m_nLocalPose");

            var flags = autoLayerKV.GetSubCollection("m_flags");
            Post = flags.GetBooleanProperty("m_bPost");
            Spline = flags.GetBooleanProperty("m_bSpline");
            XFade = flags.GetBooleanProperty("m_bXFade");
            NoBlend = flags.GetBooleanProperty("m_bNoBlend");
            Local = flags.GetBooleanProperty("m_bLocal");
            Pose = flags.GetBooleanProperty("m_bPose");
            FetchFrame = flags.GetBooleanProperty("m_bFetchFrame");
            Subtract = flags.GetBooleanProperty("m_bSubtract");

            Start = autoLayerKV.GetFloatProperty("m_start");
            Peak = autoLayerKV.GetFloatProperty("m_peak");
            Tail = autoLayerKV.GetFloatProperty("m_tail");
            End = autoLayerKV.GetFloatProperty("m_end");
        }
    }
}
