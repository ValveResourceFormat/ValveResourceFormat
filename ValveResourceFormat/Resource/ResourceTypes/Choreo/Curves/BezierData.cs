using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.Choreo.Curves
{
    /// <summary>
    /// Flags for Bezier curve interpolation.
    /// </summary>
    [Flags]
    public enum BezierFlags
    {
#pragma warning disable CS1591
        None,
        Unified,
        Unweighted,
#pragma warning restore CS1591
    }

    /// <summary>
    /// Represents Bezier curve data for choreography animations.
    /// </summary>
    public struct BezierData
    {
        /// <summary>
        /// Gets or sets the Bezier flags.
        /// </summary>
        public BezierFlags Flags { get; set; }

        /// <summary>
        /// Gets or sets the in degrees.
        /// </summary>
        public float InDegrees { get; set; }

        /// <summary>
        /// Gets or sets the in weight.
        /// </summary>
        public float InWeight { get; set; }

        /// <summary>
        /// Gets or sets the out degrees.
        /// </summary>
        public float OutDegrees { get; set; }

        /// <summary>
        /// Gets or sets the out weight.
        /// </summary>
        public float OutWeight { get; set; }

        /// <summary>
        /// Converts this Bezier data to a <see cref="KVObject"/>.
        /// </summary>
        /// <returns>A <see cref="KVObject"/> representing this Bezier data.</returns>
        public readonly KVObject ToKeyValue()
        {
            var kv = new KVObject();

            var unified = Flags.HasFlag(BezierFlags.Unified);
            kv.Add("unified", unified);
            var unweighted = Flags.HasFlag(BezierFlags.Unweighted);
            kv.Add("unweighted", unweighted);

            var inKV = new KVObject();
            inKV.Add("deg", InDegrees);
            inKV.Add("weight", InWeight);
            kv.Add("in", inKV);

            var outKV = new KVObject();
            outKV.Add("deg", OutDegrees);
            outKV.Add("weight", OutWeight);
            kv.Add("out", outKV);

            return kv;
        }
    }
}
