using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Animation;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

public sealed partial class NmGraphExtract
{
    private static readonly Dictionary<string, FlowNodeSpec> FlowNodeSpecs = new(StringComparer.Ordinal)
    {
        ["Not"] = ValueSpec<NotNode>("CNmGraphDocNotNode", [new PinDef("Not", "Bool")], "Bool", static node => [node.InputValueNodeIdx]),
        ["And"] = ValueSpec<AndNode>(
            "CNmGraphDocAndNode",
            static node => CreateRepeatedPins("And", "Bool", node.ConditionNodeIndices.Length),
            "Bool",
            static node => [.. node.ConditionNodeIndices]),
        ["Or"] = ValueSpec<OrNode>(
            "CNmGraphDocOrNode",
            static node => CreateRepeatedPins("Or", "Bool", node.ConditionNodeIndices.Length),
            "Bool",
            static node => [.. node.ConditionNodeIndices]),
        ["CachedBool"] = ValueSpec<CachedBoolNode>("CNmGraphDocCachedBoolNode", [new PinDef("Value", "Bool")], "Bool", static node => [node.InputValueNodeIdx], static node => node.Mode),
        ["CachedFloat"] = ValueSpec<CachedFloatNode>("CNmGraphDocCachedFloatNode", [new PinDef("Value", "Float")], "Float", static node => [node.InputValueNodeIdx], static node => node.Mode),
        ["CachedID"] = ValueSpec<CachedIDNode>("CNmGraphDocCachedIDNode", [new PinDef("Value", "ID")], "ID", static node => [node.InputValueNodeIdx], static node => node.Mode),
        ["CachedTarget"] = ValueSpec<CachedTargetNode>("CNmGraphDocCachedTargetNode", [new PinDef("Value", "Target")], "Target", static node => [node.InputValueNodeIdx], static node => node.Mode),
        ["CachedVector"] = ValueSpec<CachedVectorNode>("CNmGraphDocCachedVectorNode", [new PinDef("Value", "Vector")], "Vector", static node => [node.InputValueNodeIdx], static node => node.Mode),
        ["VectorCreate"] = ValueSpec<VectorCreateNode>(
            "CNmGraphDocVectorCreateNode",
            [
                new PinDef("Vector", "Vector"),
                new PinDef("X", "Float"),
                new PinDef("Y", "Float"),
                new PinDef("Z", "Float"),
            ],
            "Vector",
            static node => [node.InputVectorValueNodeIdx, node.InputValueXNodeIdx, node.InputValueYNodeIdx, node.InputValueZNodeIdx]),
        ["VectorNegate"] = ValueSpec<VectorNegateNode>("CNmGraphDocVectorNegateNode", [new PinDef("Vector", "Vector")], "Vector", static node => [node.InputValueNodeIdx]),
        ["IsTargetSet"] = ValueSpec<IsTargetSetNode>("CNmGraphDocIsTargetSetNode", [new PinDef("Target", "Target")], "Bool", static node => [node.InputValueNodeIdx]),
        ["BoneMaskBlend"] = ValueSpec<BoneMaskBlendNode>(
            "CNmGraphDocBoneMaskBlendNode",
            [
                new PinDef("Blend Weight", "Float"),
                new PinDef("Source", "BoneMask"),
                new PinDef("Target", "BoneMask"),
            ],
            "BoneMask",
            static node => [node.BlendWeightValueNodeIdx, node.SourceMaskNodeIdx, node.TargetMaskNodeIdx]),
        ["Scale"] = ValueSpec<ScaleNode>(
            "CNmGraphDocScaleNode",
            [
                new PinDef("Input", "Pose"),
                new PinDef("Mask", "BoneMask"),
                new PinDef("Enable", "Bool"),
            ],
            "Pose",
            static node => [node.ChildNodeIdx, node.MaskNodeIdx, node.EnableNodeIdx]),
        ["CurrentSyncEventID"] = SettingsSpec("CNmGraphDocCurrentSyncEventIDNode", "ID"),
        ["CurrentSyncEvent"] = new(
            "CNmGraphDocCurrentSyncEventNode",
            static _ => [],
            OutputPins("Float", allowMultipleOutConnections: true),
            static _ => [],
            [],
            static node => [("m_infoType", Typed<CurrentSyncEventNode>(node).InfoType.ToString())]),
        ["ZeroPose"] = SettingsSpec("CNmGraphDocZeroPoseNode", "Pose", allowMultipleOutConnections: false),
        ["ReferencePose"] = SettingsSpec("CNmGraphDocReferencePoseNode", "Pose", allowMultipleOutConnections: false),
        ["IsInactiveBranchCondition"] = SettingsSpec("CNmGraphDocIsInactiveBranchConditionNode", "Bool"),
        ["StateCompletedCondition"] = SettingsSpec("CNmGraphDocStateCompletedConditionNode", "Bool"),
    };

    private bool TryCreateFlowNodeFromSpec(string stem, int nodeIndex, out KVObject node)
    {
        if (FlowNodeSpecs.TryGetValue(stem, out var spec))
        {
            node = spec.CreateNode(this, nodeIndex, GetDefinition(nodeIndex));
            return true;
        }

        node = null!;
        return false;
    }

    private bool TryWireFlowNodeFromSpec(string stem, int nodeIndex, KVObject node, FlowGraphBuilder graphBuilder)
    {
        if (!FlowNodeSpecs.TryGetValue(stem, out var spec))
        {
            return false;
        }

        spec.WireInputs(this, GetDefinition(nodeIndex), node, graphBuilder);
        return true;
    }

    // A node with no inputs, described by its class alone
    private static FlowNodeSpec SettingsSpec(string className, string outputType, bool allowMultipleOutConnections = true)
        => new(className, static _ => [], OutputPins(outputType, allowMultipleOutConnections), static _ => [], [], null);

    private static FlowNodeSpec ValueSpec<TNode>(
        string className,
        IReadOnlyList<PinDef> inputPins,
        string outputType,
        Func<TNode, int[]> inputs,
        Func<TNode, CachedValueMode>? cachedMode = null)
        where TNode : Animation.GraphNode
        => new(
            className,
            _ => inputPins,
            OutputPins(outputType, allowMultipleOutConnections: true),
            node => inputs(Typed<TNode>(node)),
            [],
            cachedMode is null ? null : node => [("m_mode", cachedMode(Typed<TNode>(node)).ToString())]);

    private static FlowNodeSpec ValueSpec<TNode>(
        string className,
        Func<TNode, IReadOnlyList<PinDef>> inputPinsFactory,
        string outputType,
        Func<TNode, int[]> inputs)
        where TNode : Animation.GraphNode
        => new(
            className,
            node => inputPinsFactory(Typed<TNode>(node)),
            OutputPins(outputType, allowMultipleOutConnections: true),
            node => inputs(Typed<TNode>(node)),
            [],
            null);

    private static PinDef[] OutputPins(string outputType, bool allowMultipleOutConnections)
        => [new PinDef(outputType == "Pose" ? "Pose" : "Result", outputType, AllowMultipleOutConnections: allowMultipleOutConnections)];

    private static TNode Typed<TNode>(Animation.GraphNode? node) where TNode : Animation.GraphNode
        => node as TNode ?? throw new InvalidDataException($"Expected a {typeof(TNode).Name}.");

    private sealed class FlowNodeSpec(
        string className,
        Func<Animation.GraphNode?, IReadOnlyList<PinDef>> inputPinsFactory,
        IReadOnlyList<PinDef> outputPins,
        Func<Animation.GraphNode?, int[]> inputs,
        IReadOnlyList<(string Key, object Value)> extraFields,
        Func<Animation.GraphNode?, IReadOnlyList<(string Key, object Value)>>? extraFieldsFactory)
    {
        public KVObject CreateNode(NmGraphExtract owner, int nodeIndex, Animation.GraphNode? definition)
        {
            var node = CreateBaseNode(className, owner.MakeGuid(), owner.GetNodeName(nodeIndex));
            node.Add("m_inputPins", owner.MakePins(inputPinsFactory(definition)));
            node.Add("m_outputPins", owner.MakePins(outputPins));

            foreach (var (key, value) in extraFields)
            {
                AddValue(node, key, value);
            }

            if (extraFieldsFactory is not null)
            {
                foreach (var (key, value) in extraFieldsFactory(definition))
                {
                    AddValue(node, key, value);
                }
            }

            return node;
        }

        // Inputs connect in the order they are listed
        public void WireInputs(NmGraphExtract owner, Animation.GraphNode? definition, KVObject node, FlowGraphBuilder graphBuilder)
        {
            var sourceIndices = inputs(definition);

            for (var i = 0; i < sourceIndices.Length; i++)
            {
                owner.ConnectIfValid(sourceIndices[i], node, i, graphBuilder);
            }
        }
    }
}
