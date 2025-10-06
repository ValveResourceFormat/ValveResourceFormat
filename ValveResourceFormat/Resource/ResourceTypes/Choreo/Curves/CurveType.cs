using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo.Curves
{
    /// <summary>
    /// Represents a curve interpolation type for choreography animations.
    /// </summary>
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

        /// <summary>
        /// Gets or sets the input curve type.
        /// </summary>
        public byte InType { get; set; }

        /// <summary>
        /// Gets or sets the output curve type.
        /// </summary>
        public byte OutType { get; set; }

        /// <summary>
        /// Gets the name of the input interpolation type.
        /// </summary>
        public readonly string InTypeName => Interpolators[InType];

        /// <summary>
        /// Gets the name of the output interpolation type.
        /// </summary>
        public readonly string OutTypeName => Interpolators[OutType];

        /// <summary>
        /// Converts this curve type to a KeyValue.
        /// </summary>
        /// <returns>A KeyValue representing this curve type.</returns>
        public readonly KVValue ToKeyValue()
        {
            var curveType = $"curve_{InTypeName}_to_curve_{OutTypeName}";
            return new KVValue(curveType);
        }
    }
}
