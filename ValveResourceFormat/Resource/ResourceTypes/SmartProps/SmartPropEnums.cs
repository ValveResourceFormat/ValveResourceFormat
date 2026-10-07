namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// Enumerator name tables of the smart prop schema enums. Values are the enumerator indices.
    /// </summary>
    internal static class SmartPropEnums
    {
        public static readonly string[] Space = ["WORLD", "OBJECT", "ELEMENT"];
        public static readonly string[] ChoiceSelectionMode = ["RANDOM", "FIRST", "SPECIFIC"];
        public static readonly string[] DistributionMode = ["RANDOM", "REGULAR"];
        public static readonly string[] RadiusPlacementMode = ["SPHERE", "CIRCLE"];
        public static readonly string[] PickMode = ["LARGEST_FIRST", "RANDOM", "ALL_IN_ORDER"];
        public static readonly string[] ScaleMode = ["NONE", "SCALE_END_TO_FIT", "SCALE_EQUALLY", "SCALE_MAXIMIZE"];
        public static readonly string[] GridPlacementMode = ["SEGMENT", "FILL"];
        public static readonly string[] GridOriginBasis = ["CENTER", "CORNER"];
        public static readonly string[] PathPositions = ["ALL", "NTH", "START_AND_END", "CONTROL_POINTS"];
        public static readonly string[] PlaceMeshOrientationMode = ["FIRST_OPEN_EDGE", "FIRST_CLOSED_EDGE", "UVMAP1", "UVMAP2"];
        public static readonly string[] DetailFadeLevel = ["NONE", "MOST_AGGRESSIVE", "MORE_AGGRESSIVE", "NORMAL", "LESS_AGGRESSIVE", "LEAST_AGGRESSIVE"];
        public static readonly string[] DeformableAttachMode = ["RELATIVE", "SNAP", "STIFFEN"];
        public static readonly string[] DeformableOrientMode = ["NONE", "FORWARD_NORMAL", "UP_NORMAL", "BACKWARD_NORMAL", "MAINTAIN_OFFSET"];
        public static readonly string[] ConfigurationHandleShape = ["NONE", "SQUARE", "CIRCLE", "DIAMOND"];
        public static readonly string[] Direction = ["FORWARD", "LEFT", "UP"];
        public static readonly string[] ApplyColorMode = ["MULTIPLY_OBJECT", "MULTIPLY_CURRENT", "REPLACE"];
        public static readonly string[] ColorSelectionMode = ["SPECIFIC_COLOR", "GRADIENT_RANDOM", "GRADIENT_RANDOM_STOP", "GRADIENT_LOCATION"];
        public static readonly string[] TraceNoHitResult = ["NOTHING", "DISCARD", "MOVE_TO_START", "MOVE_TO_END"];
        public static readonly string[] VariableComparison = ["EQUAL", "NOT_EQUAL", "LESS", "LESS_OR_EQUAL", "GREATER", "GREATER_OR_EQUAL"];

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

        /// <summary>Exact, case-sensitive enumerator lookup; unknown names are 0.</summary>
        public static int FromName(string[] names, string name)
        {
            var index = Array.IndexOf(names, name);
            return index < 0 ? 0 : index;
        }
    }
}
