using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

public sealed partial class NmGraphExtract
{
    private static readonly Dictionary<string, FlowNodeSpec> FlowNodeSpecs = new(StringComparer.Ordinal)
    {
        ["Not"] = SimpleValueSpec("CNmGraphDocNotNode", [new PinDef("Not", "Bool")], "Bool", [Required("m_nInputValueNodeIdx", 0)]),
        ["And"] = SimpleValueSpec(
            "CNmGraphDocAndNode",
            node => CreateRepeatedPins("And", "Bool", GetDynamicInputCount(node, "m_conditionNodeIndices", 2)),
            "Bool",
            arrayConnections: [ArrayConnection("m_conditionNodeIndices", 0)]),
        ["Or"] = SimpleValueSpec(
            "CNmGraphDocOrNode",
            node => CreateRepeatedPins("Or", "Bool", GetDynamicInputCount(node, "m_conditionNodeIndices", 2)),
            "Bool",
            arrayConnections: [ArrayConnection("m_conditionNodeIndices", 0)]),
        ["CachedBool"] = CachedValueSpec("CNmGraphDocCachedBoolNode", "Bool"),
        ["CachedFloat"] = CachedValueSpec("CNmGraphDocCachedFloatNode", "Float"),
        ["CachedID"] = CachedValueSpec("CNmGraphDocCachedIDNode", "ID"),
        ["CachedTarget"] = CachedValueSpec("CNmGraphDocCachedTargetNode", "Target"),
        ["CachedVector"] = CachedValueSpec("CNmGraphDocCachedVectorNode", "Vector"),
        ["VectorCreate"] = SimpleValueSpec(
            "CNmGraphDocVectorCreateNode",
            [
                new PinDef("Vector", "Vector"),
                new PinDef("X", "Float"),
                new PinDef("Y", "Float"),
                new PinDef("Z", "Float"),
            ],
            "Vector",
            [
                Optional("m_inputVectorValueNodeIdx", 0),
                Optional("m_inputValueXNodeIdx", 1),
                Optional("m_inputValueYNodeIdx", 2),
                Optional("m_inputValueZNodeIdx", 3),
            ]),
        ["VectorNegate"] = SimpleValueSpec("CNmGraphDocVectorNegateNode", [new PinDef("Vector", "Vector")], "Vector", [Required("m_nInputValueNodeIdx", 0)]),
        ["IsTargetSet"] = SimpleValueSpec("CNmGraphDocIsTargetSetNode", [new PinDef("Target", "Target")], "Bool", [Required("m_nInputValueNodeIdx", 0)]),
        ["BoneMaskBlend"] = SimpleValueSpec(
            "CNmGraphDocBoneMaskBlendNode",
            [
                new PinDef("Blend Weight", "Float"),
                new PinDef("Source", "BoneMask"),
                new PinDef("Target", "BoneMask"),
            ],
            "BoneMask",
            [
                Required("m_nBlendWeightValueNodeIdx", 0),
                Required("m_nSourceMaskNodeIdx", 1),
                Required("m_nTargetMaskNodeIdx", 2),
            ]),
        ["Scale"] = SimpleValueSpec(
            "CNmGraphDocScaleNode",
            [
                new PinDef("Input", "Pose"),
                new PinDef("Mask", "BoneMask"),
                new PinDef("Enable", "Bool"),
            ],
            "Pose",
            [
                Required("m_nChildNodeIdx", 0),
                Optional("m_nMaskNodeIdx", 1),
                Optional("m_nEnableNodeIdx", 2),
            ]),
        ["CurrentSyncEventID"] = SimpleValueSpec("CNmGraphDocCurrentSyncEventIDNode", [], "ID"),
        ["CurrentSyncEventIndex"] = SimpleValueSpec("CNmGraphDocCurrentSyncEventNode", [], "Float", extraFields: [("m_infoType", "Index")]),
        ["CurrentSyncEventPercentageThrough"] = SimpleValueSpec("CNmGraphDocCurrentSyncEventNode", [], "Float", extraFields: [("m_infoType", "PercentageThrough")]),
        ["ZeroPose"] = SimplePoseSpec("CNmGraphDocZeroPoseNode"),
        ["ReferencePose"] = SimplePoseSpec("CNmGraphDocReferencePoseNode"),
        ["IsInactiveBranchCondition"] = SimpleValueSpec("CNmGraphDocIsInactiveBranchConditionNode", [], "Bool"),
        ["StateCompletedCondition"] = SimpleValueSpec("CNmGraphDocStateCompletedConditionNode", [], "Bool"),
    };

    private bool TryCreateFlowNodeFromSpec(string stem, int nodeIndex, KVObject compiledNode, out KVObject node)
    {
        if (FlowNodeSpecs.TryGetValue(stem, out var spec))
        {
            node = spec.CreateNode(this, nodeIndex, compiledNode);
            return true;
        }

        node = null!;
        return false;
    }

    private bool TryWireFlowNodeFromSpec(string stem, KVObject compiledNode, KVObject node, FlowGraphBuilder graphBuilder)
    {
        if (!FlowNodeSpecs.TryGetValue(stem, out var spec))
        {
            return false;
        }

        spec.WireInputs(this, compiledNode, node, graphBuilder);
        return true;
    }

    private static FlowNodeSpec SimplePoseSpec(string className)
        => SimpleValueSpec(className, [], "Pose", allowMultipleOutConnections: false);

    private static FlowNodeSpec CachedValueSpec(string className, string valueType)
        => SimpleValueSpec(
            className,
            [new PinDef("Value", valueType)],
            valueType,
            [Required("m_nInputValueNodeIdx", 0)],
            extraFieldsFactory: node => [("m_mode", GetOptionalString(node, "m_mode", "OnEntry"))]);

    private static FlowNodeSpec SimpleValueSpec(
        string className,
        IReadOnlyList<PinDef> inputPins,
        string outputType,
        IReadOnlyList<NodeInputConnection> inputConnections = null!,
        IReadOnlyList<NodeInputArrayConnection> arrayConnections = null!,
        bool allowMultipleOutConnections = true,
        IReadOnlyList<(string Key, object Value)> extraFields = null!,
        Func<KVObject, IReadOnlyList<(string Key, object Value)>>? extraFieldsFactory = null)
        => new(
            className,
            _ => inputPins,
            [new PinDef(outputType == "Pose" ? "Pose" : "Result", outputType, AllowMultipleOutConnections: allowMultipleOutConnections)],
            inputConnections ?? [],
            arrayConnections ?? [],
            extraFields ?? [],
            extraFieldsFactory);

    private static FlowNodeSpec SimpleValueSpec(
        string className,
        Func<KVObject, IReadOnlyList<PinDef>> inputPinsFactory,
        string outputType,
        IReadOnlyList<NodeInputConnection> inputConnections = null!,
        IReadOnlyList<NodeInputArrayConnection> arrayConnections = null!,
        bool allowMultipleOutConnections = true,
        IReadOnlyList<(string Key, object Value)> extraFields = null!,
        Func<KVObject, IReadOnlyList<(string Key, object Value)>>? extraFieldsFactory = null)
        => new(
            className,
            inputPinsFactory,
            [new PinDef(outputType == "Pose" ? "Pose" : "Result", outputType, AllowMultipleOutConnections: allowMultipleOutConnections)],
            inputConnections ?? [],
            arrayConnections ?? [],
            extraFields ?? [],
            extraFieldsFactory);

    private static NodeInputConnection Required(string fieldName, int inputIndex)
        => new(fieldName, inputIndex, true);

    private static NodeInputConnection Optional(string fieldName, int inputIndex)
        => new(fieldName, inputIndex, false);

    private static NodeInputArrayConnection ArrayConnection(string fieldName, int inputIndexOffset)
        => new(fieldName, inputIndexOffset);

    private sealed class FlowNodeSpec(
        string className,
        Func<KVObject, IReadOnlyList<PinDef>> inputPinsFactory,
        IReadOnlyList<PinDef> outputPins,
        IReadOnlyList<NodeInputConnection> inputConnections,
        IReadOnlyList<NodeInputArrayConnection> arrayConnections,
        IReadOnlyList<(string Key, object Value)> extraFields,
        Func<KVObject, IReadOnlyList<(string Key, object Value)>>? extraFieldsFactory)
    {
        public KVObject CreateNode(NmGraphExtract owner, int nodeIndex, KVObject compiledNode)
        {
            var node = CreateBaseNode(className, MakeGuid($"node:{nodeIndex}"), owner.GetNodeName(nodeIndex));
            node.Add("m_inputPins", MakePins(inputPinsFactory(compiledNode)));
            node.Add("m_outputPins", MakePins(outputPins));

            foreach (var (key, value) in extraFields)
            {
                AddValue(node, key, value);
            }

            if (extraFieldsFactory is not null)
            {
                foreach (var (key, value) in extraFieldsFactory(compiledNode))
                {
                    AddValue(node, key, value);
                }
            }

            return node;
        }

        public void WireInputs(NmGraphExtract owner, KVObject compiledNode, KVObject node, FlowGraphBuilder graphBuilder)
        {
            foreach (var connection in inputConnections)
            {
                var sourceIndex = connection.Required
                    ? (int)compiledNode.GetInt64Property(connection.FieldName)
                    : (int)compiledNode.GetInt64Property(connection.FieldName, -1);
                owner.ConnectIfValid(sourceIndex, node, connection.InputIndex, graphBuilder);
            }

            foreach (var connection in arrayConnections)
            {
                var sourceIndices = compiledNode.GetIntegerArray(connection.FieldName)?.Select(value => (int)value).ToArray() ?? [];
                for (var i = 0; i < sourceIndices.Length; i++)
                {
                    owner.ConnectIfValid(sourceIndices[i], node, i + connection.InputIndexOffset, graphBuilder);
                }
            }
        }

    }

    private readonly record struct NodeInputConnection(string FieldName, int InputIndex, bool Required);

    private readonly record struct NodeInputArrayConnection(string FieldName, int InputIndexOffset);
}
