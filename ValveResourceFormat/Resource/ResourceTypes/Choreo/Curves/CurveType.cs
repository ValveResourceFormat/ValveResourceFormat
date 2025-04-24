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
        public readonly string InTypeName => Interpolators[InType];
        public readonly string OutTypeName => Interpolators[OutType];

        public readonly KVValue ToKeyValue()
        {
            var curveType = $"curve_{InTypeName}_to_curve_{OutTypeName}";
            return new KVValue(curveType);
        }
    }
}
