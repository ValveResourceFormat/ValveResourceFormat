using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.Choreo.Enums;

namespace ValveResourceFormat.ResourceTypes.Choreo.Curves
{
    /// <summary>
    /// Represents a curve interpolation type for choreography animations.
    /// </summary>
    public struct CurveType
    {
        // Indexed by CurveInterpolator
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

        internal static CurveInterpolator ValidateInterpolator(byte type)
        {
            UnexpectedMagicException.Assert(type < Interpolators.Length, type);
            return (CurveInterpolator)type;
        }

        internal static CurveType Parse(string name)
        {
            const string Prefix = "curve_";
            const string Separator = "_to_curve_";

            var separator = name.IndexOf(Separator, StringComparison.Ordinal);
            UnexpectedMagicException.Assert(name.StartsWith(Prefix, StringComparison.Ordinal) && separator > 0, name);

            var inType = Array.IndexOf(Interpolators, name[Prefix.Length..separator]);
            var outType = Array.IndexOf(Interpolators, name[(separator + Separator.Length)..]);
            UnexpectedMagicException.Assert(inType >= 0 && outType >= 0, name);

            return new CurveType { InType = (CurveInterpolator)inType, OutType = (CurveInterpolator)outType };
        }

        /// <summary>
        /// Gets or sets the input curve type.
        /// </summary>
        public CurveInterpolator InType { get; set; }

        /// <summary>
        /// Gets or sets the output curve type.
        /// </summary>
        public CurveInterpolator OutType { get; set; }

        /// <summary>
        /// Gets the name of the input interpolation type.
        /// </summary>
        public readonly string InTypeName => Interpolators[(int)InType];

        /// <summary>
        /// Gets the name of the output interpolation type.
        /// </summary>
        public readonly string OutTypeName => Interpolators[(int)OutType];

        /// <summary>
        /// Converts this curve type to a <see cref="KVObject"/>.
        /// </summary>
        /// <returns>A <see cref="KVObject"/> representing this curve type.</returns>
        public readonly KVObject ToKeyValue()
        {
            var curveType = $"curve_{InTypeName}_to_curve_{OutTypeName}";
            return curveType;
        }
    }
}
