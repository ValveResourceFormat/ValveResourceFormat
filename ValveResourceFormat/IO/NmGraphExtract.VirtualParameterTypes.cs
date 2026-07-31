using System.IO;

namespace ValveResourceFormat.IO;

public sealed partial class NmGraphExtract
{
    private static readonly string[] TypedVirtualParameterPrefixes =
    [
        "VirtualParameter",
        "ControlParameter",
        "ParameterReference",
        "Const",
        "Cached",
    ];

    private static readonly Dictionary<string, string> VirtualParameterValueTypesByStem = CreateVirtualParameterValueTypeMap();

    private static string GetVirtualParameterValueType(CompiledNodeClass compiledClass)
    {
        foreach (var prefix in TypedVirtualParameterPrefixes)
        {
            if (compiledClass.TryGetTypedSuffix(prefix, out var valueType))
            {
                return valueType;
            }
        }

        if (VirtualParameterValueTypesByStem.TryGetValue(compiledClass.Stem, out var mappedValueType))
        {
            return mappedValueType;
        }

        throw new InvalidDataException($"Unable to infer virtual parameter value type for node class {compiledClass.Name}.");
    }

    private static Dictionary<string, string> CreateVirtualParameterValueTypeMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        Add("Bool",
        [
            "Not",
            "And",
            "Or",
            "IDComparison",
            "FloatComparison",
            "FloatRangeComparison",
            "TimeCondition",
            "IDEventCondition",
            "GraphEventCondition",
            "FootEventCondition",
            "TransitionEventCondition",
            "SyncEventIndexCondition",
            "IsTargetSet",
        ]);

        Add("Float",
        [
            "FloatRemap",
            "FloatClamp",
            "FloatEase",
            "FloatSpring",
            "FloatCurve",
            "FloatMath",
            "FloatAngleMath",
            "FloatSelector",
            "IDToFloat",
            "VectorInfo",
            "TargetInfo",
            "CurrentSyncEventIndex",
            "CurrentSyncEventPercentageThrough",
        ]);

        Add("ID",
        [
            "CurrentSyncEventID",
            "IDSwitch",
        ]);

        Add("BoneMask",
        [
            "BoneMask",
            "BoneMaskBlend",
            "BoneMaskSwitch",
            "BoneMaskSelector",
        ]);

        Add("Vector",
        [
            "VectorCreate",
        ]);

        Add("Target",
        [
            "TargetPoint",
            "TargetOffset",
        ]);

        return map;

        void Add(string valueType, IReadOnlyList<string> stems)
        {
            foreach (var stem in stems)
            {
                map.Add(stem, valueType);
            }
        }
    }
}
