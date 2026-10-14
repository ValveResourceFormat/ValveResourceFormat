using System.Linq;
using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>Coordinate space of a position or direction (<c>SmartPropSpace_t</c>).</summary>
    internal enum SmartPropSpace
    {
        World,
        Object,
        Element,
    }

    /// <summary>Axis of the current rotation (<c>SmartPropDirection_t</c>).</summary>
    internal enum SmartPropDirection
    {
        Forward,
        Left,
        Up,
    }

    /// <summary>Comparison of a variable filter (<c>SmartPropVariableComparison_t</c>).</summary>
    internal enum SmartPropVariableComparison
    {
        Equal,
        NotEqual,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual,
    }

    internal enum ChoiceSelectionMode
    {
        Random,
        First,
        Specific,
    }

    internal enum DistributionMode
    {
        Random,
        Regular,
    }

    internal enum RadiusPlacementMode
    {
        Sphere,
        Circle,
    }

    internal enum FitPickMode
    {
        LargestFirst,
        Random,
        AllInOrder,
    }

    internal enum FitScaleMode
    {
        None,
        ScaleEndToFit,
        ScaleEqually,
        ScaleMaximize,
    }

    internal enum GridPlacementMode
    {
        Segment,
        Fill,
    }

    internal enum GridOriginBasis
    {
        Center,
        Corner,
    }

    internal enum PathPositions
    {
        All,
        Nth,
        StartAndEnd,
        ControlPoints,
    }

    internal enum ApplyColorMode
    {
        MultiplyObject,
        MultiplyCurrent,
        Replace,
    }

    internal enum ColorSelectionMode
    {
        SpecificColor,
        GradientRandom,
        GradientRandomStop,
        GradientLocation,
    }

    internal enum TraceNoHitResult
    {
        Nothing,
        Discard,
        MoveToStart,
        MoveToEnd,
    }

    /// <summary>
    /// Enumerator name tables of the smart prop schema enums. Values are the enumerator indices.
    /// </summary>
    internal static class SmartPropEnums
    {
        public static readonly string[] Space = NamesOf<SmartPropSpace>();
        public static readonly string[] ChoiceSelectionMode = NamesOf<ChoiceSelectionMode>();
        public static readonly string[] DistributionMode = NamesOf<DistributionMode>();
        public static readonly string[] RadiusPlacementMode = NamesOf<RadiusPlacementMode>();
        public static readonly string[] PickMode = NamesOf<FitPickMode>();
        public static readonly string[] ScaleMode = NamesOf<FitScaleMode>();
        public static readonly string[] GridPlacementMode = NamesOf<GridPlacementMode>();
        public static readonly string[] GridOriginBasis = NamesOf<GridOriginBasis>();
        public static readonly string[] PathPositions = NamesOf<PathPositions>();
        public static readonly string[] PlaceMeshOrientationMode = ["FIRST_OPEN_EDGE", "FIRST_CLOSED_EDGE", "UVMAP1", "UVMAP2"];
        public static readonly string[] DetailFadeLevel = ["NONE", "MOST_AGGRESSIVE", "MORE_AGGRESSIVE", "NORMAL", "LESS_AGGRESSIVE", "LEAST_AGGRESSIVE"];
        public static readonly string[] DeformableAttachMode = ["RELATIVE", "SNAP", "STIFFEN"];
        public static readonly string[] DeformableOrientMode = ["NONE", "FORWARD_NORMAL", "UP_NORMAL", "BACKWARD_NORMAL", "MAINTAIN_OFFSET"];
        public static readonly string[] ConfigurationHandleShape = NamesOf<SmartPropHandleShape>();
        public static readonly string[] Direction = NamesOf<SmartPropDirection>();
        public static readonly string[] ApplyColorMode = NamesOf<ApplyColorMode>();
        public static readonly string[] ColorSelectionMode = NamesOf<ColorSelectionMode>();
        public static readonly string[] TraceNoHitResult = NamesOf<TraceNoHitResult>();
        public static readonly string[] VariableComparison = NamesOf<SmartPropVariableComparison>();

        /// <summary>Enum variable classes and their enumerator tables.</summary>
        public static readonly Dictionary<string, string[]> VariableClasses = new(StringComparer.Ordinal)
        {
            ["CSmartPropVariable_ApplyColorMode"] = ApplyColorMode,
            ["CSmartPropVariable_ChoiceSelectionMode"] = ChoiceSelectionMode,
            ["CSmartPropVariable_ColorSelectionMode"] = ColorSelectionMode,
            ["CSmartPropVariable_CoordinateSpace"] = Space,
            ["CSmartPropVariable_DirectionVector"] = Direction,
            ["CSmartPropVariable_DistributionMode"] = DistributionMode,
            ["CSmartPropVariable_GridOriginMode"] = GridOriginBasis,
            ["CSmartPropVariable_GridPlacementMode"] = GridPlacementMode,
            ["CSmartPropVariable_OrientationMode"] = PlaceMeshOrientationMode,
            ["CSmartPropVariable_PathPositions"] = PathPositions,
            ["CSmartPropVariable_PickMode"] = PickMode,
            ["CSmartPropVariable_RadiusPlacementMode"] = RadiusPlacementMode,
            ["CSmartPropVariable_ScaleMode"] = ScaleMode,
            ["CSmartPropVariable_TraceNoHit"] = TraceNoHitResult,
        };

        /// <summary>
        /// Reads a plain (not bindable) enum member: an enumerator name or index, the default when missing or unknown.
        /// </summary>
        public static int FromMember(KVObject data, string key, string[] names, int defaultValue)
        {
            var value = SmartPropParser.Get(data, key);

            if (value == null)
            {
                return defaultValue;
            }

            if (value.ValueType == KVValueType.String)
            {
                var index = Array.IndexOf(names, (string)value!);
                return index < 0 ? defaultValue : index;
            }

            return SmartPropValue.FromKV(value).GetInt();
        }

        /// <summary>Schema enumerator names of an enum declared in schema order: <c>MultiplyObject</c> is <c>MULTIPLY_OBJECT</c>.</summary>
        private static string[] NamesOf<TEnum>() where TEnum : struct, Enum
            => [.. Enum.GetNames<TEnum>().Select(static name => string.Concat(name.Select(static (c, i) => i > 0 && char.IsUpper(c) ? $"_{c}" : char.ToUpperInvariant(c).ToString())))];

        /// <summary>Exact, case-sensitive enumerator lookup; unknown names are 0.</summary>
        public static int FromName(string[] names, string name)
        {
            var index = Array.IndexOf(names, name);
            return index < 0 ? 0 : index;
        }
    }
}
