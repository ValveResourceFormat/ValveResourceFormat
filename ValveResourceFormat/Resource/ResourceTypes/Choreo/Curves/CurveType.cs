using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo.Curves
{
    public struct CurveType
    {
        private static readonly string[] Interpolators = [
            "default",
            "catmullrom_normalize_x",
            "easein",
            "easeout",
            "easeinout",
            "bspline",
            "linear_interp",
            "kochanek",
            "kochanek_early",
            "kochanek_late",
            "simple_cubic",
            "catmullrom",
            "catmullrom_normalize",
            "catmullrom_tangent",
            "exponential_decay",
            "hold",
            "bezier",
        ];
        public byte InType { get; set; }
        public byte OutType { get; set; }
        public string InTypeName
        {
            get
            {
                return Interpolators[InType];
            }
        }
        public string OutTypeName
        {
            get
            {
                return Interpolators[OutType];
            }
        }

        public KVValue ToKeyValue()
        {
            var curveIn = InTypeName;
            var curveOut = OutTypeName;
            var curveType = $"curve_{curveIn}_to_curve_{curveOut}";
            return new KVValue(KVType.STRING, curveType);
        }
    }
}
