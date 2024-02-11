using ValveResourceFormat.ResourceTypes.Choreo.Curves;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    public class ChoreoEdge
    {
        public CurveType CurveType { get; init; }
        public float ZeroValue { get; init; }

        public ChoreoEdge(CurveType curveType, float zeroValue)
        {
            CurveType = curveType;
            ZeroValue = zeroValue;
        }

        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null);

            kv.AddProperty("type", CurveType.ToKeyValue());
            kv.AddProperty("zero_value", new KVValue(KVType.FLOAT, ZeroValue));

            return kv;
        }
    }
}
