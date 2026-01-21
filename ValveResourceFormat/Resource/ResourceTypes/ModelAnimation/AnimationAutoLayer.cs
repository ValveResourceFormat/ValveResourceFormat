using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents an animation auto layer that defines blending and timing parameters for layered animations.
    /// Auto layers allow animations to be automatically blended together based on configured parameters.
    /// </summary>
    public class AnimationAutoLayer
    {
        /// <summary>
        /// Gets or sets the local reference index for the animation layer.
        /// </summary>
        public int LocalReference { get; set; }

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
        /// Gets or sets the start time of the blend curve.
        /// </summary>
        public float Start { get; set; }

        /// <summary>
        /// Gets or sets the peak time of the blend curve where the layer has maximum influence.
        /// </summary>
        public float Peak { get; set; }

        /// <summary>
        /// Gets or sets the tail time where the blend curve begins to fade out.
        /// </summary>
        public float Tail { get; set; }

        /// <summary>
        /// Gets or sets the end time of the blend curve.
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

            var flags = autoLayerKV.GetProperty<KVObject>("m_flags");
            Post = flags.GetProperty<bool>("m_bPost");
            Spline = flags.GetProperty<bool>("m_bSpline");
            XFade = flags.GetProperty<bool>("m_bXFade");
            NoBlend = flags.GetProperty<bool>("m_bNoBlend");
            Local = flags.GetProperty<bool>("m_bLocal");
            Pose = flags.GetProperty<bool>("m_bPose");
            FetchFrame = flags.GetProperty<bool>("m_bFetchFrame");
            Subtract = flags.GetProperty<bool>("m_bSubtract");

            Start = autoLayerKV.GetFloatProperty("m_start");
            Peak = autoLayerKV.GetFloatProperty("m_peak");
            Tail = autoLayerKV.GetFloatProperty("m_tail");
            End = autoLayerKV.GetFloatProperty("m_end");
        }
    }
}
