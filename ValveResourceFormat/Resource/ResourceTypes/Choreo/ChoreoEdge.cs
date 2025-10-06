using ValveResourceFormat.ResourceTypes.Choreo.Curves;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    /// <summary>
    /// Represents an edge in a choreography curve.
    /// </summary>
    public class ChoreoEdge
    {
        /// <summary>
        /// Gets the curve type of this edge.
        /// </summary>
        public CurveType CurveType { get; init; }

        /// <summary>
        /// Gets the zero value of this edge.
        /// </summary>
        public float ZeroValue { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChoreoEdge"/> class.
        /// </summary>
        /// <param name="curveType">The curve type.</param>
        /// <param name="zeroValue">The zero value.</param>
        public ChoreoEdge(CurveType curveType, float zeroValue)
        {
            CurveType = curveType;
            ZeroValue = zeroValue;
        }

        /// <summary>
        /// Converts this edge to a KeyValues object.
        /// </summary>
        /// <returns>A KeyValues object representing this edge.</returns>
        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null);

            kv.AddProperty("type", CurveType.ToKeyValue());
            kv.AddProperty("zero_value", ZeroValue);

            return kv;
        }
    }
}
