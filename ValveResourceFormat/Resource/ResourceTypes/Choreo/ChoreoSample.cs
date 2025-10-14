using ValveResourceFormat.ResourceTypes.Choreo.Curves;
using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    /// <summary>
    /// Represents a sample point in a choreography curve.
    /// </summary>
    public class ChoreoSample
    {
        /// <summary>
        /// Gets the time of the sample.
        /// </summary>
        public float Time { get; private set; }

        /// <summary>
        /// Gets the value of the sample.
        /// </summary>
        public float Value { get; private set; }

        /// <summary>
        /// Gets the Bezier curve data for the sample.
        /// </summary>
        public BezierData? Bezier { get; private set; }

        /// <summary>
        /// Gets the curve type for the sample.
        /// </summary>
        public CurveType? Curve { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChoreoSample"/> class.
        /// </summary>
        /// <param name="time">The time of the sample.</param>
        /// <param name="value">The value of the sample.</param>
        public ChoreoSample(float time, float value)
        {
            Time = time;
            Value = value;
        }

        /// <summary>
        /// Sets the Bezier curve data for this sample.
        /// </summary>
        /// <param name="flags">The Bezier flags.</param>
        /// <param name="inDeg">The in degrees.</param>
        /// <param name="inWeight">The in weight.</param>
        /// <param name="outDeg">The out degrees.</param>
        /// <param name="outWeight">The out weight.</param>
        public void SetBezierData(BezierFlags flags, float inDeg, float inWeight, float outDeg, float outWeight)
        {
            Bezier = new BezierData
            {
                Flags = flags,
                InDegrees = inDeg,
                InWeight = inWeight,
                OutDegrees = outDeg,
                OutWeight = outWeight
            };
        }

        /// <summary>
        /// Sets the curve type for this sample.
        /// </summary>
        /// <param name="inType">The input curve type.</param>
        /// <param name="outType">The output curve type.</param>
        public void SetCurveType(byte inType, byte outType)
        {
            Curve = new CurveType
            {
                InType = inType,
                OutType = outType
            };
        }

        /// <summary>
        /// Converts this sample to a <see cref="KVObject"/>.
        /// </summary>
        /// <returns>A <see cref="KVObject"/> representing this sample.</returns>
        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null);

            kv.AddProperty("time", Time);
            kv.AddProperty("value", Value);

            if (Curve != null)
            {
                kv.AddProperty("curvetype", Curve.Value.ToKeyValue());
            }

            if (Bezier != null)
            {
                kv.AddProperty("bezier", Bezier.Value.ToKeyValue());
            }

            return kv;
        }
    }
}
