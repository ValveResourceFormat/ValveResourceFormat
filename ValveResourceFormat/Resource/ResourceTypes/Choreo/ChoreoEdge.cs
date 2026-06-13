using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.Choreo.Curves;

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
        /// Converts this edge to a <see cref="KVObject"/>.
        /// </summary>
        /// <returns>A <see cref="KVObject"/> representing this edge.</returns>
        public KVObject ToKeyValues()
        {
            var kv = KVObject.Collection();

            kv.Add("type", CurveType.ToKeyValue());
            kv.Add("zero_value", ZeroValue);

            return kv;
        }
    }
}
