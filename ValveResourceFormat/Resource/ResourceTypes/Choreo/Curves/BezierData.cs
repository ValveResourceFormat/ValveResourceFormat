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
        /// Converts this Bezier data to a <see cref="KVValue"/>.
        /// </summary>
        /// <returns>A <see cref="KVValue"/> representing this Bezier data.</returns>
        public readonly KVValue ToKeyValue()
        {
            var kv = new KVObject(null);

            var unified = Flags.HasFlag(BezierFlags.Unified);
            kv.Add("unified", unified);
            var unweighted = Flags.HasFlag(BezierFlags.Unweighted);
            kv.Add("unweighted", unweighted);

            var inKV = new KVObject(null);
            inKV.Add("deg", InDegrees);
            inKV.Add("weight", InWeight);
            kv.Add("in", inKV.Value);

            var outKV = new KVObject(null);
            outKV.Add("deg", OutDegrees);
            outKV.Add("weight", OutWeight);
            kv.Add("out", outKV.Value);

            return kv.Value;
        }
    }
}
