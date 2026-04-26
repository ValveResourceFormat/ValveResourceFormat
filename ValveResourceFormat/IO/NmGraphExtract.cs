using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

file static class NmGraphExtractExtensions
{
    public static long GetInt64Property(this KVObject node, string key, long defaultValue = 0)
        => node.GetIntegerProperty(key, defaultValue);
}

/// <summary>
/// Extracts Source 2 AnimGraph 2 graphs to editable document format.
/// </summary>
public class NmGraphExtract
{
    private readonly Resource resource;
    private readonly BinaryKV3 resourceData;
    private readonly KVObject graph;
    private readonly IReadOnlyList<KVObject?> compiledNodes;
    private readonly string[] nodePaths;
    private readonly string[] controlParameterIDs;
    private readonly string[] virtualParameterIDs;
    private readonly int[] virtualParameterNodeIndices;
    private readonly string[] resources;
    private readonly HashSet<int> persistentNodeIndices;
    private readonly Dictionary<int, string> rootParameterNodeIds = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="NmGraphExtract"/> class.
    /// </summary>
    public NmGraphExtract(Resource resource)
    {
        this.resource = resource;
        resourceData = resource.DataBlock as BinaryKV3
            ?? throw new InvalidDataException("Resource DataBlock is not a BinaryKV3.");
        graph = resourceData.Data;
        compiledNodes = graph.GetArray("m_nodes")?.Select(value => value as KVObject).ToArray()
            ?? throw new InvalidDataException("NmGraph is missing m_nodes.");
        nodePaths = graph.GetArray<string>("m_nodePaths")?.ToArray()
            ?? throw new InvalidDataException("NmGraph is missing m_nodePaths.");
        controlParameterIDs = graph.GetArray<string>("m_controlParameterIDs")?.ToArray() ?? [];
        virtualParameterIDs = graph.GetArray<string>("m_virtualParameterIDs")?.ToArray() ?? [];
        virtualParameterNodeIndices = graph.GetIntegerArray("m_virtualParameterNodeIndices")?.Select(value => (int)value).ToArray() ?? [];
        resources = graph.GetArray<string>("m_resources")?.ToArray() ?? [];
        persistentNodeIndices = graph.GetIntegerArray("m_persistentNodeIndices")?.Select(value => (int)value).ToHashSet() ?? [];
    }

    /// <summary>
    /// Converts the graph to a content file.
    /// </summary>
    public ContentFile ToContentFile()
    {
        var document = KVObject.Collection();
        document.Add("_class", "CNmGraphDocument");
        document.Add("m_nVersion", 0L);
        document.Add("m_pRootGraph", BuildRootGraph());
        document.Add("m_variationHierarchy", BuildVariationHierarchy());
        document.Add("m_debugParameterSets", KVObject.Array());
        document.Add("m_dictionaryIDSetIDs", KVObject.Array());

        return new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(document.ToKV3String()),
            FileName = resource.FileName is not null && resource.FileName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal)
                ? resource.FileName[..^2]
                : resource.FileName ?? "graph.vnmgraph",
        };
    }

    private KVObject BuildVariationHierarchy()
    {
        var variation = KVObject.Collection();
        variation.Add("m_ID", graph.GetStringProperty("m_variationID"));
        variation.Add("m_parentID", string.Empty);
        variation.Add("m_skeleton", graph["m_skeleton"] ?? string.Empty);
        variation.Add("m_pUserData", graph["m_pUserData"]);

        var hierarchy = KVObject.Collection();
        var variations = KVObject.Array();
        variations.Add(variation);
        hierarchy.Add("m_variations", variations);
        return hierarchy;
    }

    private KVObject BuildRootGraph()
    {
        var graphBuilder = new FlowGraphBuilder(this, "root", "BlendTree");

        foreach (var nodeIndex in persistentNodeIndices.Order())
        {
            var compiledNode = GetCompiledNode(nodeIndex);
            if (compiledNode is null)
            {
                continue;
            }

            if (IsControlParameterNode(compiledNode) || IsVirtualParameterNode(compiledNode))
            {
                var node = BuildPersistentRootNode(nodeIndex);
                graphBuilder.Nodes.Add(node);
                graphBuilder.NodeIdsByCompiledIndex[nodeIndex] = node.GetStringProperty("m_ID");
            }
        }

        var rootNode = BuildFlowNode((int)graph.GetInt64Property("m_nRootNodeIdx"), graphBuilder);
        var resultNode = CreatePoseResultNode("root/result");

        graphBuilder.Nodes.Add(resultNode);
        graphBuilder.Connect(rootNode.GetStringProperty("m_ID"), GetOutputPinId(rootNode, 0), resultNode.GetStringProperty("m_ID"), GetInputPinId(resultNode, 0));

        return graphBuilder.ToGraph();
    }

    private KVObject BuildPersistentRootNode(int nodeIndex)
    {
        var compiledNode = GetCompiledNode(nodeIndex)
            ?? throw new InvalidDataException($"Node {nodeIndex} does not exist.");
        var compiledClass = GetCompiledClass(compiledNode);

        return compiledClass.Stem switch
        {
            var stem when compiledClass.TryGetTypedSuffix("ControlParameter", out var valueType)
                => CreateControlParameterNode(nodeIndex, GetTypedDocNodeClassName(valueType, "ControlParameter"), valueType, "Value", GetControlParameterExtraFields(valueType)),
            var stem when compiledClass.TryGetTypedSuffix("VirtualParameter", out _)
                => CreateVirtualParameterNode(nodeIndex, compiledClass),
            _ => throw new InvalidDataException($"Unsupported persistent NmGraph node: {compiledClass.Name}"),
        };
    }

    private KVObject CreateControlParameterNode(int nodeIndex, string className, string outputType, string outputName, IReadOnlyList<(string Key, object Value)> extraFields)
    {
        var nodeId = MakeGuid($"root-control:{nodeIndex}");
        rootParameterNodeIds[nodeIndex] = nodeId;

        var node = CreateBaseNode(className, nodeId, GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef(outputName, outputType, AllowMultipleOutConnections: true)]));
        node.Add("m_groupName", string.Empty);
        node.Add("m_dictionaryParameterBinding", Guid.Empty.ToString());

        foreach (var (key, value) in extraFields)
        {
            AddValue(node, key, value);
        }

        return node;
    }

    private KVObject CreateVirtualParameterNode(int nodeIndex, CompiledNodeClass compiledClass)
    {
        if (!compiledClass.TryGetTypedSuffix("VirtualParameter", out var valueType))
        {
            throw new InvalidDataException($"Unsupported virtual parameter node: {compiledClass.Name}");
        }

        var nodeId = MakeGuid($"root-virtual:{nodeIndex}");
        rootParameterNodeIds[nodeIndex] = nodeId;

        var node = CreateBaseNode(GetTypedDocNodeClassName(valueType, "VirtualParameter"), nodeId, GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Value", valueType, AllowMultipleOutConnections: true)]));
        node.Add("m_groupName", string.Empty);
        node["m_pChildGraph"] = BuildVirtualParameterGraph(nodeIndex, GetTypedDocNodeClassName(valueType, "Result"), valueType);
        return node;
    }

    private KVObject BuildVirtualParameterGraph(int nodeIndex, string resultClassName, string resultType)
    {
        var compiledNode = GetCompiledNode(nodeIndex)!;
        var childNodeIndex = (int)compiledNode.GetInt64Property("m_nChildNodeIdx");
        var graphBuilder = new FlowGraphBuilder(this, $"virtual:{nodeIndex}", "VirtualParameterValueTree");

        var childNode = BuildFlowNode(childNodeIndex, graphBuilder);
        var resultNode = CreateResultNode(resultClassName, $"virtual:{nodeIndex}/result", "Out", resultType);

        graphBuilder.Nodes.Add(resultNode);
        graphBuilder.Connect(childNode.GetStringProperty("m_ID"), GetOutputPinId(childNode, 0), resultNode.GetStringProperty("m_ID"), GetInputPinId(resultNode, 0));

        return graphBuilder.ToGraph();
    }

    private KVObject BuildStateMachineNode(int nodeIndex)
    {
        var node = CreateBaseNode("CNmGraphDocStateMachineNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node["m_pChildGraph"] = BuildStateMachineGraph(nodeIndex);
        return node;
    }

    private KVObject BuildStateMachineGraph(int nodeIndex)
    {
        var stateMachine = GetCompiledNode(nodeIndex)
            ?? throw new InvalidDataException($"Missing state machine node {nodeIndex}.");
        var stateDefinitions = stateMachine.GetArray("m_stateDefinitions")?.Select(value => value as KVObject).Where(value => value is not null).Cast<KVObject>().ToArray()
            ?? [];

        var graphNodeId = MakeGuid($"state-machine-graph:{nodeIndex}");
        var graphNode = KVObject.Collection();
        graphNode.Add("_class", "CNmGraphDocStateMachineGraph");
        graphNode.Add("m_ID", graphNodeId);

        var nodes = KVObject.Array();
        var stateNodeIds = new Dictionary<int, string>();
        var stateNodes = new Dictionary<int, KVObject>();

        for (var stateIndex = 0; stateIndex < stateDefinitions.Length; stateIndex++)
        {
            var stateNodeIndex = (int)stateDefinitions[stateIndex].GetInt64Property("m_nStateNodeIdx");
            var stateNode = BuildStateNode(stateNodeIndex);
            var stateNodeId = stateNode.GetStringProperty("m_ID");
            stateNodeIds[stateIndex] = stateNodeId;
            stateNodes[stateNodeIndex] = stateNode;
            nodes.Add(stateNode);
        }

        var entryOverrideStates = stateDefinitions
            .Where(stateDefinition => (int)stateDefinition.GetInt64Property("m_nEntryConditionNodeIdx") >= 0)
            .ToArray();

        if (entryOverrideStates.Length > 0)
        {
            nodes.Add(BuildEntryOverrideConduit(nodeIndex, stateDefinitions, stateNodes));
        }

        var transitionInfos = EnumerateTransitionInfos(stateDefinitions).ToArray();
        var globalTransitions = transitionInfos.Where(info => info.GroupKind == StateMachineTransitionGroup.Global).ToArray();
        if (globalTransitions.Length > 0)
        {
            nodes.Add(BuildGlobalTransitionConduit(nodeIndex, globalTransitions, stateNodes));
        }

        foreach (var conduitGroup in transitionInfos.Where(info => info.GroupKind == StateMachineTransitionGroup.Standard).GroupBy(info => info.GroupPath))
        {
            nodes.Add(BuildTransitionConduit(nodeIndex, conduitGroup.ToArray(), stateNodes));
        }

        graphNode.Add("m_nodes", nodes);
        graphNode.Add("m_graphType", "StateMachine");
        graphNode.Add("m_viewOffset", MakeVector2(0.0f, 0.0f));
        graphNode.Add("m_flViewZoom", 1.0f);
        graphNode.Add("m_entryStateID", stateNodeIds.GetValueOrDefault((int)stateMachine.GetInt64Property("m_nDefaultStateIndex"), Guid.Empty.ToString()));
        return graphNode;
    }

    private KVObject BuildStateNode(int nodeIndex)
    {
        var compiledNode = GetCompiledNode(nodeIndex)
            ?? throw new InvalidDataException($"Missing state node {nodeIndex}.");

        var node = CreateBaseNode("CNmGraphDocStateNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        var isOffState = compiledNode.GetBooleanProperty("m_bIsOffState");
        var childNodeIndex = (int)compiledNode.GetInt64Property("m_nChildNodeIdx");
        var childNode = GetCompiledNode(childNodeIndex);
        var childNodeClass = childNode is not null ? GetCompiledClassName(childNode) : string.Empty;
        var hasChildGraph = childNode is not null;
        isOffState |= !hasChildGraph;

        node["m_pChildGraph"] = childNode switch
        {
            not null when childNodeClass == "CNmStateMachineNode::CDefinition" => BuildNestedStateMachineChildGraph(childNodeIndex),
            not null => BuildBlendTreeGraph($"state:{nodeIndex}", childNodeIndex),
            _ => null,
        };

        node["m_pSecondaryGraph"] = BuildStateLayerGraph(nodeIndex, compiledNode);
        node.Add("m_type", isOffState
            ? "OffState"
            : childNodeClass == "CNmStateMachineNode::CDefinition" ? "StateMachineState" : "BlendTreeState");
        node.Add("m_cloneSourceStateID", Guid.Empty.ToString());
        node.Add("m_stateEvents", KVObject.Array());
        node.Add("m_timedStateEvents", KVObject.Array());
        node.Add("m_events", KVObject.Array());
        node.Add("m_entryEvents", CloneArray("m_entryEvents", compiledNode));
        node.Add("m_executeEvents", CloneArray("m_executeEvents", compiledNode));
        node.Add("m_exitEvents", CloneArray("m_exitEvents", compiledNode));
        node.Add("m_timeRemainingEvents", ConvertTimedStateEvents("TimeRemaining", "m_timedRemainingEvents", compiledNode));
        node.Add("m_timeElapsedEvents", ConvertTimedStateEvents("TimeElapsed", "m_timedElapsedEvents", compiledNode));
        node.Add("m_bUseActualElapsedTimeInStateForTimedEvents", compiledNode.GetBooleanProperty("m_bUseActualElapsedTimeInStateForTimedEvents"));
        return node;
    }

    private KVObject BuildNestedStateMachineChildGraph(int childStateMachineNodeIndex)
    {
        var graphBuilder = new FlowGraphBuilder(this, $"nested-sm:{childStateMachineNodeIndex}", "BlendTree");
        var childNode = BuildStateMachineNode(childStateMachineNodeIndex);
        var resultNode = CreatePoseResultNode($"nested-sm:{childStateMachineNodeIndex}/result");

        graphBuilder.Nodes.Add(childNode);
        graphBuilder.Nodes.Add(resultNode);
        graphBuilder.Connect(childNode.GetStringProperty("m_ID"), GetOutputPinId(childNode, 0), resultNode.GetStringProperty("m_ID"), GetInputPinId(resultNode, 0));
        return graphBuilder.ToGraph();
    }

    private KVObject BuildStateLayerGraph(int stateNodeIndex, KVObject compiledStateNode)
    {
        var graphBuilder = new FlowGraphBuilder(this, $"state-layer:{stateNodeIndex}", "ValueTree");
        var resultNode = CreateStateLayerResultNode($"state-layer:{stateNodeIndex}/result");
        graphBuilder.Nodes.Add(resultNode);

        var layerInputs = new[]
        {
            (CompiledKey: "m_nLayerWeightNodeIdx", InputIndex: 0),
            (CompiledKey: "m_nLayerRootMotionWeightNodeIdx", InputIndex: 1),
            (CompiledKey: "m_nLayerBoneMaskNodeIdx", InputIndex: 2),
        };

        foreach (var (compiledKey, inputIndex) in layerInputs)
        {
            if ((int)compiledStateNode.GetInt64Property(compiledKey) is var inputNodeIndex && inputNodeIndex >= 0)
            {
                var sourceNode = BuildFlowNode(inputNodeIndex, graphBuilder);
                graphBuilder.Connect(sourceNode.GetStringProperty("m_ID"), GetOutputPinId(sourceNode, 0), resultNode.GetStringProperty("m_ID"), GetInputPinId(resultNode, inputIndex));
            }
        }

        return graphBuilder.ToGraph();
    }

    private KVObject BuildEntryOverrideConduit(int stateMachineNodeIndex, IReadOnlyList<KVObject> stateDefinitions, IReadOnlyDictionary<int, KVObject> stateNodes)
    {
        var conduitNode = CreateBaseNode("CNmGraphDocEntryStateOverrideConduitNode", MakeGuid($"entry-conduit:{stateMachineNodeIndex}"), string.Empty);
        conduitNode["m_pSecondaryGraph"] = BuildEntryOverrideGraph(stateMachineNodeIndex, stateDefinitions, stateNodes);
        return conduitNode;
    }

    private KVObject BuildEntryOverrideGraph(int stateMachineNodeIndex, IReadOnlyList<KVObject> stateDefinitions, IReadOnlyDictionary<int, KVObject> stateNodes)
    {
        var graphBuilder = new FlowGraphBuilder(this, $"entry-override:{stateMachineNodeIndex}", "EntryOverrideTree");
        var resultNode = CreateEntryOverrideConditionsNode(stateMachineNodeIndex);
        graphBuilder.Nodes.Add(resultNode);

        var pinToStateMapping = KVObject.Array();
        var row = 0;

        foreach (var stateDefinition in stateDefinitions)
        {
            var entryConditionNodeIdx = (int)stateDefinition.GetInt64Property("m_nEntryConditionNodeIdx");
            if (entryConditionNodeIdx < 0)
            {
                continue;
            }

            var stateNodeIndex = (int)stateDefinition.GetInt64Property("m_nStateNodeIdx");
            var stateNodeId = stateNodes[stateNodeIndex].GetStringProperty("m_ID");

            var overrideNode = CreateEntryOverrideNode(stateNodeIndex, stateNodeId, row++);
            graphBuilder.Nodes.Add(overrideNode);

            var conditionNode = BuildFlowNode(entryConditionNodeIdx, graphBuilder);
            graphBuilder.Connect(conditionNode.GetStringProperty("m_ID"), GetOutputPinId(conditionNode, 0), overrideNode.GetStringProperty("m_ID"), GetInputPinId(overrideNode, 0));

            var pin = CreatePin($"State {row}", "Bool", isDynamicPin: true);
            var resultPins = (KVObject)resultNode["m_inputPins"]!;
            resultPins.Add(pin);

            var mapping = KVObject.Collection();
            mapping.Add("m_pinID", pin.GetStringProperty("m_ID"));
            mapping.Add("m_stateID", stateNodeId);
            pinToStateMapping.Add(mapping);
        }

        resultNode["m_pinToStateMapping"] = pinToStateMapping;
        return graphBuilder.ToGraph();
    }

    private KVObject BuildGlobalTransitionConduit(int stateMachineNodeIndex, IReadOnlyList<TransitionInfo> transitions, IReadOnlyDictionary<int, KVObject> stateNodes)
    {
        var conduitNode = CreateBaseNode("CNmGraphDocGlobalTransitionConduitNode", MakeGuid($"global-conduit:{stateMachineNodeIndex}"), string.Empty);
        var graphBuilder = new FlowGraphBuilder(this, $"global-conduit:{stateMachineNodeIndex}", "GlobalTransitionConduit");

        var row = 0;
        foreach (var transition in transitions)
        {
            var transitionNode = CreateTransitionResultNode("CNmGraphDocGlobalTransitionNode", transition.TransitionNodeIndex, transition.CanBeForced, stateNodes[transition.TargetStateNodeIndex].GetStringProperty("m_ID"), row++);
            graphBuilder.Nodes.Add(transitionNode);

            foreach (var (sourceIndex, inputIndex) in EnumerateTransitionInputs(transition.CompiledTransitionNode, transition.ConditionNodeIndex))
            {
                if (sourceIndex < 0)
                {
                    continue;
                }

                var sourceNode = BuildFlowNode(sourceIndex, graphBuilder);
                graphBuilder.Connect(sourceNode.GetStringProperty("m_ID"), GetOutputPinId(sourceNode, 0), transitionNode.GetStringProperty("m_ID"), GetInputPinId(transitionNode, inputIndex));
            }
        }

        conduitNode["m_pSecondaryGraph"] = graphBuilder.ToGraph();
        return conduitNode;
    }

    private KVObject BuildTransitionConduit(int stateMachineNodeIndex, IReadOnlyList<TransitionInfo> transitions, IReadOnlyDictionary<int, KVObject> stateNodes)
    {
        var firstTransition = transitions[0];
        var conduitNode = CreateBaseNode("CNmGraphDocTransitionConduitNode", MakeGuid($"transition-conduit:{stateMachineNodeIndex}:{firstTransition.GroupPath}"), GetPathLeaf(firstTransition.GroupPath));
        conduitNode.Add("m_startStateID", stateNodes[firstTransition.SourceStateNodeIndex].GetStringProperty("m_ID"));
        conduitNode.Add("m_endStateID", stateNodes[firstTransition.TargetStateNodeIndex].GetStringProperty("m_ID"));

        var graphBuilder = new FlowGraphBuilder(this, $"transition-conduit:{stateMachineNodeIndex}:{firstTransition.GroupPath}", "TransitionConduit");

        var row = 0;
        foreach (var transition in transitions)
        {
            var transitionNode = CreateTransitionResultNode("CNmGraphDocTransitionNode", transition.TransitionNodeIndex, transition.CanBeForced, null, row++);
            graphBuilder.Nodes.Add(transitionNode);

            foreach (var (sourceIndex, inputIndex) in EnumerateTransitionInputs(transition.CompiledTransitionNode, transition.ConditionNodeIndex))
            {
                if (sourceIndex < 0)
                {
                    continue;
                }

                var sourceNode = BuildFlowNode(sourceIndex, graphBuilder);
                graphBuilder.Connect(sourceNode.GetStringProperty("m_ID"), GetOutputPinId(sourceNode, 0), transitionNode.GetStringProperty("m_ID"), GetInputPinId(transitionNode, inputIndex));
            }
        }

        conduitNode["m_pSecondaryGraph"] = graphBuilder.ToGraph();
        return conduitNode;
    }

    private IEnumerable<(int SourceIndex, int InputIndex)> EnumerateTransitionInputs(KVObject compiledTransitionNode, int conditionNodeIndex)
    {
        yield return (conditionNodeIndex, 0);
        yield return ((int)compiledTransitionNode.GetInt64Property("m_nDurationOverrideNodeIdx"), 1);
        yield return ((int)compiledTransitionNode.GetInt64Property("m_timeOffsetOverrideNodeIdx"), 2);
        yield return ((int)compiledTransitionNode.GetInt64Property("m_startBoneMaskNodeIdx"), 3);
        yield return ((int)compiledTransitionNode.GetInt64Property("m_targetSyncIDNodeIdx"), 4);
    }

    private IEnumerable<TransitionInfo> EnumerateTransitionInfos(IReadOnlyList<KVObject> stateDefinitions)
    {
        foreach (var stateDefinition in stateDefinitions)
        {
            var sourceStateNodeIndex = (int)stateDefinition.GetInt64Property("m_nStateNodeIdx");
            var transitions = stateDefinition.GetArray("m_transitionDefinitions")?.Select(value => value as KVObject).Where(value => value is not null).Cast<KVObject>() ?? [];

            foreach (var transition in transitions)
            {
                var conditionNodeIndex = (int)transition.GetInt64Property("m_nConditionNodeIdx");
                var transitionNodeIndex = (int)transition.GetInt64Property("m_nTransitionNodeIdx");
                var conditionPath = conditionNodeIndex >= 0 && conditionNodeIndex < nodePaths.Length ? nodePaths[conditionNodeIndex] : string.Empty;
                var transitionPath = transitionNodeIndex >= 0 && transitionNodeIndex < nodePaths.Length ? nodePaths[transitionNodeIndex] : string.Empty;
                var groupPath = GetTransitionGroupPath(conditionPath, transitionPath, sourceStateNodeIndex, (int)transition.GetInt64Property("m_nTargetStateIdx"));
                var groupKind = groupPath.Contains("Global Transitions", StringComparison.Ordinal)
                    ? StateMachineTransitionGroup.Global
                    : StateMachineTransitionGroup.Standard;

                yield return new TransitionInfo
                {
                    GroupKind = groupKind,
                    GroupPath = groupPath,
                    SourceStateNodeIndex = sourceStateNodeIndex,
                    TargetStateIndex = (int)transition.GetInt64Property("m_nTargetStateIdx"),
                    TargetStateNodeIndex = (int)stateDefinitions[(int)transition.GetInt64Property("m_nTargetStateIdx")].GetInt64Property("m_nStateNodeIdx"),
                    ConditionNodeIndex = conditionNodeIndex,
                    TransitionNodeIndex = transitionNodeIndex,
                    CompiledTransitionNode = GetCompiledNode(transitionNodeIndex)
                        ?? throw new InvalidDataException($"Missing transition node {transitionNodeIndex}."),
                    StateMachineTransition = transition,
                    CanBeForced = transition.GetBooleanProperty("m_bCanBeForced"),
                };
            }
        }
    }

    private string GetTransitionGroupPath(string conditionPath, string transitionPath, int sourceStateNodeIndex, int targetStateIndex)
    {
        var preferredPath = !string.IsNullOrEmpty(transitionPath) ? transitionPath : conditionPath;
        var prefix = GetPathParent(preferredPath);
        if (!string.IsNullOrEmpty(prefix))
        {
            return prefix;
        }

        return $"transition:{sourceStateNodeIndex}->{targetStateIndex}";
    }

    private KVObject BuildBlendTreeGraph(string graphKey, int rootNodeIndex)
    {
        var graphBuilder = new FlowGraphBuilder(this, graphKey, "BlendTree");
        var rootNode = BuildFlowNode(rootNodeIndex, graphBuilder);
        var resultNode = CreatePoseResultNode($"{graphKey}/result");

        graphBuilder.Nodes.Add(resultNode);
        graphBuilder.Connect(rootNode.GetStringProperty("m_ID"), GetOutputPinId(rootNode, 0), resultNode.GetStringProperty("m_ID"), GetInputPinId(resultNode, 0));
        return graphBuilder.ToGraph();
    }

    private KVObject BuildFlowNode(int nodeIndex, FlowGraphBuilder graphBuilder)
    {
        if (graphBuilder.NodeIdsByCompiledIndex.TryGetValue(nodeIndex, out _)
            && graphBuilder.Nodes.FirstOrDefault(node => node.GetStringProperty("m_ID") == graphBuilder.NodeIdsByCompiledIndex[nodeIndex]) is { } existingNode)
        {
            return existingNode;
        }

        var compiledNode = GetCompiledNode(nodeIndex)
            ?? throw new InvalidDataException($"Missing compiled node {nodeIndex}.");

        if (graphBuilder.GraphKey != "root"
            && persistentNodeIndices.Contains(nodeIndex)
            && (IsControlParameterNode(compiledNode) || IsVirtualParameterNode(compiledNode)))
        {
            var parameterRefNode = CreateParameterReferenceNode(nodeIndex, graphBuilder);
            graphBuilder.Nodes.Add(parameterRefNode);
            graphBuilder.NodeIdsByCompiledIndex[nodeIndex] = parameterRefNode.GetStringProperty("m_ID");
            return parameterRefNode;
        }

        var node = BuildFlowNodeInternal(nodeIndex, compiledNode, graphBuilder);
        graphBuilder.NodeIdsByCompiledIndex[nodeIndex] = node.GetStringProperty("m_ID");
        graphBuilder.Nodes.Add(node);
        WireNodeInputs(nodeIndex, compiledNode, node, graphBuilder);
        return node;
    }

    private KVObject BuildFlowNodeInternal(int nodeIndex, KVObject compiledNode, FlowGraphBuilder graphBuilder)
    {
        var compiledClass = GetCompiledClass(compiledNode);

        return compiledClass.Stem switch
        {
            "StateMachine" => BuildStateMachineNode(nodeIndex),
            "ReferencedGraph" => CreateReferencedGraphNode(nodeIndex, compiledNode),
            "Selector" => CreateSelectorNode("CNmGraphDocSelectorNode", nodeIndex, compiledNode, graphBuilder),
            "ClipSelector" => CreateSelectorNode("CNmGraphDocClipSelectorNode", nodeIndex, compiledNode, graphBuilder),
            "AnimationClipSelector" => CreateSelectorNode("CNmGraphDocClipSelectorNode", nodeIndex, compiledNode, graphBuilder),
            "IDBasedSelector" => CreateIdBasedSelectorNode("CNmGraphDocIDBasedSelectorNode", nodeIndex, compiledNode),
            "IDBasedClipSelector" => CreateIdBasedSelectorNode("CNmGraphDocIDBasedClipSelectorNode", nodeIndex, compiledNode),
            "ParameterizedSelector" => CreateParameterizedSelectorNode("CNmGraphDocParameterizedSelectorNode", "CNmGraphDocParameterizedSelectorNode::CData", nodeIndex, compiledNode),
            "ParameterizedClipSelector" => CreateParameterizedClipSelectorNode(nodeIndex, compiledNode),
            "ParameterizedAnimationClipSelector" => CreateParameterizedSelectorNode("CNmGraphDocParameterizedClipSelectorNode", "CNmGraphDocParameterizedClipSelectorNode::CData", nodeIndex, compiledNode),
            "Clip" => CreateClipNode(nodeIndex, compiledNode),
            "AnimationPose" => CreateAnimationPoseNode(nodeIndex, compiledNode),
            "Not" => CreateSimpleNode(GetSimpleDocNodeClassName(compiledClass.Stem), nodeIndex, [new PinDef("Not", "Bool")], [new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]),
            "And" => CreateSimpleNode(GetSimpleDocNodeClassName(compiledClass.Stem), nodeIndex, CreateRepeatedPins("And", "Bool", GetDynamicInputCount(compiledNode, "m_conditionNodeIndices", 2)), [new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]),
            "Or" => CreateSimpleNode(GetSimpleDocNodeClassName(compiledClass.Stem), nodeIndex, CreateRepeatedPins("Or", "Bool", GetDynamicInputCount(compiledNode, "m_conditionNodeIndices", 2)), [new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]),
            "FloatComparison" => CreateFloatComparisonNode(nodeIndex, compiledNode),
            "FloatRangeComparison" => CreateFloatRangeComparisonNode(nodeIndex, compiledNode),
            "FloatRemap" => CreateFloatRemapNode(nodeIndex, compiledNode),
            "FloatClamp" => CreateFloatClampNode(nodeIndex, compiledNode),
            "FloatEase" => CreateFloatEaseNode(nodeIndex, compiledNode),
            "FloatSpring" => CreateFloatSpringNode(nodeIndex, compiledNode),
            "FloatCurve" => CreateFloatCurveNode(nodeIndex, compiledNode),
            "FloatMath" => CreateFloatMathNode(nodeIndex, compiledNode),
            "FloatSwitch" => CreateFloatSwitchNode(nodeIndex, compiledNode),
            "FloatAngleMath" => CreateFloatAngleMathNode(nodeIndex, compiledNode),
            "IDComparison" => CreateIDComparisonNode(nodeIndex, compiledNode),
            "IDSwitch" => CreateIdSwitchNode(nodeIndex, compiledNode),
            "IDToFloat" => CreateIdToFloatNode(nodeIndex, compiledNode),
            "IDEventCondition" => CreateIDEventConditionNode(nodeIndex, compiledNode),
            "IDEvent" => CreateIdEventNode(nodeIndex, compiledNode),
            "IDEventPercentageThrough" => CreateIdEventPercentageThroughNode(nodeIndex, compiledNode),
            "GraphEventCondition" => CreateGraphEventConditionNode(nodeIndex, compiledNode),
            "FootEventCondition" => CreateFootEventConditionNode(nodeIndex, compiledNode),
            "FootstepEventID" => CreateFootstepEventIdNode(nodeIndex, compiledNode),
            "FootstepEventPercentageThrough" => CreateFootstepEventPercentageThroughNode(nodeIndex, compiledNode),
            "CurrentSyncEventID" => CreateCurrentSyncEventIdNode(nodeIndex),
            "CurrentSyncEventIndex" => CreateCurrentSyncEventNode(nodeIndex, "Index"),
            "CurrentSyncEventPercentageThrough" => CreateCurrentSyncEventNode(nodeIndex, "PercentageThrough"),
            "FloatSelector" => CreateFloatSelectorNode(nodeIndex, compiledNode),
            "CachedBool" => CreateCachedValueNode("CNmGraphDocCachedBoolNode", nodeIndex, "Bool"),
            "CachedFloat" => CreateCachedValueNode("CNmGraphDocCachedFloatNode", nodeIndex, "Float"),
            "CachedID" => CreateCachedValueNode("CNmGraphDocCachedIDNode", nodeIndex, "ID"),
            "CachedTarget" => CreateCachedValueNode("CNmGraphDocCachedTargetNode", nodeIndex, "Target"),
            "CachedVector" => CreateCachedValueNode("CNmGraphDocCachedVectorNode", nodeIndex, "Vector"),
            "VectorInfo" => CreateVectorInfoNode(nodeIndex, compiledNode),
            "VectorCreate" => CreateVectorCreateNode(nodeIndex),
            "VectorNegate" => CreateSimpleNode("CNmGraphDocVectorNegateNode", nodeIndex, [new PinDef("Vector", "Vector")], [new PinDef("Result", "Vector", AllowMultipleOutConnections: true)]),
            "IsTargetSet" => CreateSimpleNode("CNmGraphDocIsTargetSetNode", nodeIndex, [new PinDef("Target", "Target")], [new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]),
            "TargetInfo" => CreateTargetInfoNode(nodeIndex, compiledNode),
            "TargetPoint" => CreateTargetPointNode(nodeIndex, compiledNode),
            "TargetOffset" => CreateTargetOffsetNode(nodeIndex, compiledNode),
            "BoneMask" => CreateBoneMaskNode(nodeIndex, compiledNode),
            "BoneMaskBlend" => CreateBoneMaskBlendNode(nodeIndex),
            "BoneMaskSwitch" => CreateBoneMaskSwitchNode(nodeIndex, compiledNode),
            "BoneMaskSelector" => CreateBoneMaskSelectorNode(nodeIndex, compiledNode),
            "SpeedScale" => CreateScaleNode("CnmGraphDocSpeedScaleNode", nodeIndex, compiledNode, "Scale", "m_flMultiplier"),
            "DurationScale" => CreateScaleNode("CnmGraphDocDurationScaleNode", nodeIndex, compiledNode, "New Duration", "m_flDesiredDuration"),
            "VelocityBasedSpeedScale" => CreateScaleNode("CnmGraphDocVelocityBasedSpeedScaleNode", nodeIndex, compiledNode, "Desired Velocity", "m_flDesiredVelocity"),
            "VelocityBlend" => CreateVelocityBlendNode(nodeIndex),
            "Blend1D" => CreateBlend1DNode(nodeIndex, compiledNode),
            "Blend2D" => CreateBlend2DNode(nodeIndex, compiledNode),
            "SyncEventIndexCondition" => CreateSyncEventIndexConditionNode(nodeIndex, compiledNode),
            "TimeCondition" => CreateTimeConditionNode(nodeIndex, compiledNode),
            "TransitionEventCondition" => CreateTransitionEventConditionNode(nodeIndex, compiledNode),
            "AimCS" => CreateAimCsNode(nodeIndex, compiledNode),
            "FootIK" => CreateFootIkNode(nodeIndex, compiledNode),
            "TwoBoneIK" => CreateTwoBoneIkNode(nodeIndex, compiledNode),
            "OrientationWarp" => CreateOrientationWarpNode(nodeIndex, compiledNode),
            "SnapWeapon" => CreateSnapWeaponNode(nodeIndex),
            "LayerBlend" => CreateLayerBlendNode(nodeIndex, compiledNode),
            "Scale" => CreateScaleMaskNode(nodeIndex),
            "ZeroPose" => CreateSimpleNode("CNmGraphDocZeroPoseNode", nodeIndex, [], [new PinDef("Pose", "Pose")]),
            "ReferencePose" => CreateSimpleNode("CNmGraphDocReferencePoseNode", nodeIndex, [], [new PinDef("Pose", "Pose")]),
            "IsInactiveBranchCondition" => CreateSimpleNode(GetSimpleDocNodeClassName(compiledClass.Stem), nodeIndex, [], [new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]),
            "StateCompletedCondition" => CreateSimpleNode(GetSimpleDocNodeClassName(compiledClass.Stem), nodeIndex, [], [new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]),
            "ConstVector" => CreateConstVectorNode(nodeIndex, compiledNode),
            "ConstTarget" => CreateConstTargetNode(nodeIndex, compiledNode),
            "ConstBoneTarget" => CreateConstBoneTargetNode(nodeIndex, compiledNode),
            var stem when compiledClass.TryGetTypedSuffix("Const", out var valueType)
                => CreateConstValueNode(GetTypedDocNodeClassName(valueType, "Const"), nodeIndex, valueType, GetConstValueKey(valueType), GetConstValue(compiledNode, valueType)),
            var stem when compiledClass.TryGetTypedSuffix("ControlParameter", out _)
                => CreateParameterReferenceNode(nodeIndex, graphBuilder),
            _ => throw new InvalidDataException($"Unsupported NmGraph node: {compiledClass.Name}"),
        };
    }

    private void WireNodeInputs(int nodeIndex, KVObject compiledNode, KVObject node, FlowGraphBuilder graphBuilder)
    {
        var className = GetCompiledClassName(compiledNode);

        switch (className)
        {
            case "CNmParameterizedClipSelectorNode::CDefinition":
            case "CNmParameterizedSelectorNode::CDefinition":
            case "CNmParameterizedAnimationClipSelectorNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_parameterNodeIdx"), node, 0, graphBuilder);
                var optionIndices = compiledNode.GetIntegerArray("m_optionNodeIndices")?.Select(value => (int)value).ToArray() ?? [];
                for (var i = 0; i < optionIndices.Length; i++)
                {
                    ConnectIfValid(optionIndices[i], node, i + 1, graphBuilder);
                }
                break;

            case "CNmSelectorNode::CDefinition":
            case "CNmClipSelectorNode::CDefinition":
            case "CNmAnimationClipSelectorNode::CDefinition":
                var selectorOptionIndices = compiledNode.GetIntegerArray("m_optionNodeIndices")?.Select(value => (int)value).ToArray() ?? [];
                for (var i = 0; i < selectorOptionIndices.Length; i++)
                {
                    ConnectIfValid(selectorOptionIndices[i], node, i, graphBuilder);
                }
                break;

            case "CNmIDBasedSelectorNode::CDefinition":
            case "CNmIDBasedClipSelectorNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nParameterNodeIdx"), node, 0, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nFallbackNodeIdx", -1), node, 1, graphBuilder);
                var idBasedOptionIndices = compiledNode.GetIntegerArray("m_optionNodeIndices")?.Select(value => (int)value).ToArray() ?? [];
                for (var i = 0; i < idBasedOptionIndices.Length; i++)
                {
                    ConnectIfValid(idBasedOptionIndices[i], node, i + 2, graphBuilder);
                }
                break;

            case "CNmClipNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nPlayInReverseValueNodeIdx"), node, 0, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nResetTimeValueNodeIdx"), node, 1, graphBuilder);
                break;

            case "CNmAnimationPoseNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nPoseTimeValueNodeIdx"), node, 0, graphBuilder);
                break;

            case "CNmNotNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nInputValueNodeIdx"), node, 0, graphBuilder);
                break;

            case "CNmAndNode::CDefinition":
            case "CNmOrNode::CDefinition":
                var conditionIndices = compiledNode.GetIntegerArray("m_conditionNodeIndices")?.Select(value => (int)value).ToArray() ?? [];
                for (var i = 0; i < conditionIndices.Length; i++)
                {
                    ConnectIfValid(conditionIndices[i], node, i, graphBuilder);
                }
                break;

            case "CNmIDComparisonNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nInputValueNodeIdx"), node, 0, graphBuilder);
                break;

            case "CNmCachedBoolNode::CDefinition":
            case "CNmCachedFloatNode::CDefinition":
            case "CNmCachedIDNode::CDefinition":
            case "CNmCachedVectorNode::CDefinition":
            case "CNmCachedTargetNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nInputValueNodeIdx"), node, 0, graphBuilder);
                break;

            case "CNmFloatComparisonNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nInputValueNodeIdx"), node, 0, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nComparandValueNodeIdx"), node, 1, graphBuilder);
                break;

            case "CNmFloatRangeComparisonNode::CDefinition":
            case "CNmTimeConditionNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nInputValueNodeIdx"), node, 0, graphBuilder);
                break;

            case "CNmFloatRemapNode::CDefinition":
            case "CNmFloatClampNode::CDefinition":
            case "CNmFloatEaseNode::CDefinition":
            case "CNmFloatSpringNode::CDefinition":
            case "CNmFloatCurveNode::CDefinition":
            case "CNmFloatAngleMathNode::CDefinition":
            case "CNmIDToFloatNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nInputValueNodeIdx"), node, 0, graphBuilder);
                break;

            case "CNmFloatMathNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nInputValueNodeIdxA"), node, 0, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nInputValueNodeIdxB", -1), node, 1, graphBuilder);
                break;

            case "CNmFloatSwitchNode::CDefinition":
            case "CNmIDSwitchNode::CDefinition":
            case "CNmBoneMaskSwitchNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nSwitchValueNodeIdx"), node, 0, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nTrueValueNodeIdx", -1), node, 1, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nFalseValueNodeIdx", -1), node, 2, graphBuilder);
                break;

            case "CNmVectorInfoNode::CDefinition":
            case "CNmVectorNegateNode::CDefinition":
            case "CNmIsTargetSetNode::CDefinition":
            case "CNmTargetInfoNode::CDefinition":
            case "CNmTargetPointNode::CDefinition":
            case "CNmTargetOffsetNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nInputValueNodeIdx"), node, 0, graphBuilder);
                break;

            case "CNmReferencedGraphNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nFallbackNodeIdx", -1), node, 0, graphBuilder);
                break;

            case "CNmSpeedScaleNode::CDefinition":
            case "CNmDurationScaleNode::CDefinition":
            case "CNmVelocityBasedSpeedScaleNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nChildNodeIdx"), node, 0, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nInputValueNodeIdx", -1), node, 1, graphBuilder);
                break;

            case "CNmVectorCreateNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_inputVectorValueNodeIdx", -1), node, 0, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_inputValueXNodeIdx", -1), node, 1, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_inputValueYNodeIdx", -1), node, 2, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_inputValueZNodeIdx", -1), node, 3, graphBuilder);
                break;

            case "CNmFloatSelectorNode::CDefinition":
                var floatSelectorConditionIndices = compiledNode.GetIntegerArray("m_conditionNodeIndices")?.Select(value => (int)value).ToArray() ?? [];
                for (var i = 0; i < floatSelectorConditionIndices.Length; i++)
                {
                    ConnectIfValid(floatSelectorConditionIndices[i], node, i, graphBuilder);
                }
                break;

            case "CNmBoneMaskSelectorNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_parameterValueNodeIdx"), node, 0, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_defaultMaskNodeIdx", -1), node, 1, graphBuilder);
                var maskNodeIndices = compiledNode.GetIntegerArray("m_maskNodeIndices")?.Select(value => (int)value).ToArray() ?? [];
                for (var i = 0; i < maskNodeIndices.Length; i++)
                {
                    ConnectIfValid(maskNodeIndices[i], node, i + 2, graphBuilder);
                }
                break;

            case "CNmBoneMaskBlendNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nBlendWeightValueNodeIdx"), node, 0, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nSourceMaskNodeIdx"), node, 1, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nTargetMaskNodeIdx"), node, 2, graphBuilder);
                break;

            case "CNmVelocityBlendNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nInputParameterValueNodeIdx"), node, 0, graphBuilder);
                var velocitySourceIndices = compiledNode.GetIntegerArray("m_sourceNodeIndices")?.Select(value => (int)value).ToArray() ?? [];
                for (var i = 0; i < velocitySourceIndices.Length; i++)
                {
                    ConnectIfValid(velocitySourceIndices[i], node, i + 1, graphBuilder);
                }
                break;

            case "CNmBlend1DNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nInputParameterValueNodeIdx"), node, 0, graphBuilder);
                var blend1DSourceIndices = compiledNode.GetIntegerArray("m_sourceNodeIndices")?.Select(value => (int)value).ToArray() ?? [];
                for (var i = 0; i < blend1DSourceIndices.Length; i++)
                {
                    ConnectIfValid(blend1DSourceIndices[i], node, i + 1, graphBuilder);
                }
                break;

            case "CNmBlend2DNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nInputParameterNodeIdx0"), node, 0, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nInputParameterNodeIdx1"), node, 1, graphBuilder);
                var blend2DSourceIndices = compiledNode.GetIntegerArray("m_sourceNodeIndices")?.Select(value => (int)value).ToArray() ?? [];
                for (var i = 0; i < blend2DSourceIndices.Length; i++)
                {
                    ConnectIfValid(blend2DSourceIndices[i], node, i + 2, graphBuilder);
                }
                break;

            case "CNmLayerBlendNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nBaseNodeIdx"), node, 0, graphBuilder);
                var layerDefinitions = compiledNode.GetArray("m_layerDefinition")?.Select(value => value as KVObject).Where(value => value is not null).Cast<KVObject>().ToArray() ?? [];
                for (var i = 0; i < layerDefinitions.Length; i++)
                {
                    var layerNode = CreateLayerBlendInputNode(nodeIndex, i, layerDefinitions[i]);
                    graphBuilder.Nodes.Add(layerNode);
                    graphBuilder.Connect(layerNode.GetStringProperty("m_ID"), GetOutputPinId(layerNode, 0), node.GetStringProperty("m_ID"), GetInputPinId(node, i + 1));

                    ConnectIfValid((int)layerDefinitions[i].GetInt64Property("m_nInputNodeIdx"), layerNode, 0, graphBuilder);

                    if (layerDefinitions[i].GetBooleanProperty("m_bIsStateMachineLayer"))
                    {
                        continue;
                    }

                    ConnectIfValid((int)layerDefinitions[i].GetInt64Property("m_nWeightValueNodeIdx", -1), layerNode, 1, graphBuilder);
                    ConnectIfValid((int)layerDefinitions[i].GetInt64Property("m_nRootMotionWeightValueNodeIdx", -1), layerNode, 2, graphBuilder);
                    ConnectIfValid((int)layerDefinitions[i].GetInt64Property("m_nBoneMaskValueNodeIdx", -1), layerNode, 3, graphBuilder);
                }
                break;

            case "CNmAimCSNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nChildNodeIdx"), node, 0, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nHorizontalAngleNodeIdx", -1), node, 1, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nVerticalAngleNodeIdx", -1), node, 2, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nWeaponCategoryNodeIdx", -1), node, 3, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nWeaponTypeNodeIdx", -1), node, 4, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nWeaponActionNodeIdx", -1), node, 5, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nWeaponDropNodeIdx", -1), node, 6, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nCrouchWeightNodeIdx", -1), node, 7, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nIsDefusingNodeIdx", -1), node, 8, graphBuilder);
                break;

            case "CNmFootIKNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nChildNodeIdx"), node, 0, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nLeftTargetNodeIdx", -1), node, 1, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nRightTargetNodeIdx", -1), node, 2, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nEnabledNodeIdx", -1), node, 3, graphBuilder);
                break;

            case "CNmTwoBoneIKNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nChildNodeIdx"), node, 0, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nEffectorTargetNodeIdx", -1), node, 1, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nEnabledNodeIdx", -1), node, 2, graphBuilder);
                break;

            case "CNmOrientationWarpNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nClipReferenceNodeIdx"), node, 0, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nTargetValueNodeIdx"), node, compiledNode.GetBooleanProperty("m_bIsOffsetNode") ? 2 : 1, graphBuilder);
                break;

            case "CNmSnapWeaponNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nChildNodeIdx"), node, 0, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nFlashedAmountNodeIdx", -1), node, 1, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nWeaponCategoryNodeIdx", -1), node, 2, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nWeaponTypeNodeIdx", -1), node, 3, graphBuilder);
                break;

            case "CNmScaleNode::CDefinition":
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nChildNodeIdx"), node, 0, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nMaskNodeIdx", -1), node, 1, graphBuilder);
                ConnectIfValid((int)compiledNode.GetInt64Property("m_nEnableNodeIdx", -1), node, 2, graphBuilder);
                break;
        }
    }

    private void ConnectIfValid(int sourceIndex, KVObject targetNode, int inputIndex, FlowGraphBuilder graphBuilder)
    {
        if (sourceIndex < 0)
        {
            return;
        }

        var sourceNode = BuildFlowNode(sourceIndex, graphBuilder);
        graphBuilder.Connect(sourceNode.GetStringProperty("m_ID"), GetOutputPinId(sourceNode, 0), targetNode.GetStringProperty("m_ID"), GetInputPinId(targetNode, inputIndex));
    }

    private KVObject CreateParameterReferenceNode(int nodeIndex, FlowGraphBuilder graphBuilder)
    {
        var compiledNode = GetCompiledNode(nodeIndex)
            ?? throw new InvalidDataException($"Missing parameter node {nodeIndex}.");
        var compiledClass = GetCompiledClass(compiledNode);
        if (!compiledClass.TryGetTypedSuffix("ControlParameter", out var valueType)
            && !compiledClass.TryGetTypedSuffix("VirtualParameter", out valueType))
        {
            throw new InvalidDataException($"Unsupported parameter reference node: {compiledClass.Name}");
        }

        var parameterNodeId = rootParameterNodeIds.GetValueOrDefault(nodeIndex, MakeGuid($"root-control:{nodeIndex}"));
        rootParameterNodeIds[nodeIndex] = parameterNodeId;

        var node = CreateBaseNode(GetTypedDocNodeClassName(valueType, "ParameterReference"), MakeGuid($"paramref:{graphBuilder.GraphKey}:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Value", valueType, AllowMultipleOutConnections: true)]));
        node.Add("m_parameterUUID", parameterNodeId);
        node.Add("m_parameterValueType", valueType);
        node.Add("m_parameterName", GetNodeName(nodeIndex));
        node.Add("m_parameterGroupName", string.Empty);
        return node;
    }

    private KVObject CreateReferencedGraphNode(int nodeIndex, KVObject compiledNode)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocReferencedGraphNode::CData");
        variationData.Add("m_variation", GetResourcePath((int)compiledNode.GetInt64Property("m_nReferencedGraphIdx")));

        var node = CreateBaseNode("CNmGraphDocReferencedGraphNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Fallback", "Pose")]));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", KVObject.Array());
        node.Add("m_defaultResourceName", string.Empty);
        return node;
    }

    private KVObject CreateSelectorNode(string className, int nodeIndex, KVObject compiledNode, FlowGraphBuilder graphBuilder)
    {
        var optionIndices = compiledNode.GetIntegerArray("m_optionNodeIndices")?.Select(value => (int)value).ToArray() ?? [];
        var conditionIndices = compiledNode.GetIntegerArray("m_conditionNodeIndices")?.Select(value => (int)value).ToArray() ?? [];
        var optionLabels = optionIndices.Select(GetNodeName).ToArray();

        var node = CreateBaseNode(className, MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins(optionLabels.Select(label => new PinDef(label, "Pose", IsDynamicPin: true))));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_optionLabels", CloneStringArray(optionLabels));
        node["m_pSecondaryGraph"] = BuildSelectorConditionGraph(nodeIndex, conditionIndices, optionLabels, graphBuilder);
        return node;
    }

    private KVObject CreateIdBasedSelectorNode(string className, int nodeIndex, KVObject compiledNode)
    {
        var optionIds = compiledNode.GetArray<string>("m_optionIDs")?.ToArray() ?? [];

        var inputPins = new List<PinDef>
        {
            new("ID", "ID"),
            new("Optional Fallback", "Pose"),
        };
        inputPins.AddRange(optionIds.Select(value => new PinDef(string.IsNullOrEmpty(value) ? "Option" : value, "Pose", IsDynamicPin: true)));

        var node = CreateBaseNode(className, MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins(inputPins));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_optionLabels", CloneStringArray(optionIds));
        node.Add("m_bIgnoreInvalidOptions", compiledNode.GetBooleanProperty("m_bIgnoreInvalidOptions"));
        return node;
    }

    private KVObject CreateParameterizedClipSelectorNode(int nodeIndex, KVObject compiledNode)
    {
        return CreateParameterizedSelectorNode("CNmGraphDocParameterizedClipSelectorNode", "CNmGraphDocParameterizedClipSelectorNode::CData", nodeIndex, compiledNode);
    }

    private KVObject CreateParameterizedSelectorNode(string className, string dataClassName, int nodeIndex, KVObject compiledNode)
    {
        var optionNodeIndices = compiledNode.GetIntegerArray("m_optionNodeIndices")?.Select(value => (int)value).ToArray() ?? [];
        var optionLabels = optionNodeIndices.Select(GetNodeName).ToArray();

        var variationData = KVObject.Collection();
        variationData.Add("_class", dataClassName);
        variationData.Add("m_optionWeights", CloneArray("m_optionWeights", compiledNode));

        var node = CreateBaseNode(className, MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        var inputPins = new List<PinDef> { new("Parameter", "Float") };
        inputPins.AddRange(optionLabels.Select(label => new PinDef(label, "Pose", IsDynamicPin: true)));

        node.Add("m_inputPins", MakePins(inputPins));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", KVObject.Array());
        node.Add("m_defaultResourceName", string.Empty);
        node.Add("m_optionLabels", CloneStringArray(optionLabels));
        node.Add("m_bIgnoreInvalidOptions", compiledNode.GetBooleanProperty("m_bIgnoreInvalidOptions"));
        return node;
    }

    private KVObject CreateClipNode(int nodeIndex, KVObject compiledNode)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocClipNode::CData");
        variationData.Add("m_clip", GetResourcePath((int)compiledNode.GetInt64Property("m_nDataSlotIdx")));
        variationData.Add("m_flSpeedMultiplier", compiledNode.GetFloatProperty("m_flSpeedMultiplier"));
        variationData.Add("m_nStartSyncEventOffset", compiledNode.GetInt64Property("m_nStartSyncEventOffset"));

        var node = CreateBaseNode("CNmGraphDocClipNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Play In Reverse", "Bool"), new PinDef("Reset Time", "Bool")]));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", KVObject.Array());
        node.Add("m_defaultResourceName", string.Empty);
        node.Add("m_bSampleRootMotion", compiledNode.GetBooleanProperty("m_bSampleRootMotion"));
        node.Add("m_bAllowLooping", compiledNode.GetBooleanProperty("m_bAllowLooping"));
        node.Add("m_graphEvents", CloneArray("m_graphEvents", compiledNode));
        return node;
    }

    private KVObject CreateAnimationPoseNode(int nodeIndex, KVObject compiledNode)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocAnimationPoseNode::CData");
        variationData.Add("m_clip", GetResourcePath((int)compiledNode.GetInt64Property("m_nDataSlotIdx")));
        variationData.Add("m_variationTimeValue", -1.0f);

        var node = CreateBaseNode("CNmGraphDocAnimationPoseNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Time", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", KVObject.Array());
        node.Add("m_defaultResourceName", string.Empty);
        node.Add("m_inputTimeRemapRange", CloneRange(compiledNode.GetSubCollection("m_inputTimeRemapRange"), float.MaxValue, float.MinValue));
        node.Add("m_fixedTimeValue", compiledNode.GetFloatProperty("m_flUserSpecifiedTime"));
        node.Add("m_useFramesAsInput", compiledNode.GetBooleanProperty("m_bUseFramesAsInput"));
        return node;
    }

    private KVObject CreateIDComparisonNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocIDComparisonNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("ID", "ID")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));
        node.Add("m_comparison", compiledNode.GetStringProperty("m_comparison"));
        node.Add("m_values", CloneArray("m_comparisionIDs", compiledNode));
        return node;
    }

    private KVObject CreateIdToFloatNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocIDToFloatNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("ID", "ID")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_defaultValue", compiledNode.GetFloatProperty("m_defaultValue"));

        var ids = compiledNode.GetArray<string>("m_IDs")?.ToArray() ?? [];
        var values = compiledNode.GetFloatArray("m_values")?.ToArray() ?? [];
        var mappings = KVObject.Array();

        for (var i = 0; i < Math.Min(ids.Length, values.Length); i++)
        {
            var mapping = KVObject.Collection();
            mapping.Add("m_ID", ids[i]);
            mapping.Add("m_value", values[i]);
            mappings.Add(mapping);
        }

        node.Add("m_mappings", mappings);
        return node;
    }

    private KVObject CreateIdSwitchNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocIDSwitchNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Bool", "Bool"),
            new PinDef("If True", "ID"),
            new PinDef("If False", "ID"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "ID", AllowMultipleOutConnections: true)]));
        node.Add("m_falseValue", compiledNode["m_falseValue"] ?? string.Empty);
        node.Add("m_trueValue", compiledNode["m_trueValue"] ?? string.Empty);
        return node;
    }

    private KVObject CreateCachedValueNode(string className, int nodeIndex, string valueType)
    {
        var compiledNode = GetCompiledNode(nodeIndex)
            ?? throw new InvalidDataException($"Missing cached value node {nodeIndex}.");

        var node = CreateBaseNode(className, MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Value", valueType)]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", valueType, AllowMultipleOutConnections: true)]));
        node.Add("m_mode", compiledNode["m_mode"] ?? "OnEntry");
        return node;
    }

    private KVObject CreateFloatComparisonNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocFloatComparisonNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Float", "Float"), new PinDef("Comparand (Optional)", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));
        node.Add("m_comparison", compiledNode.GetStringProperty("m_comparison"));
        node.Add("m_flComparisonValue", compiledNode.GetFloatProperty("m_flComparisonValue"));
        node.Add("m_flEpsilon", compiledNode.GetFloatProperty("m_flEpsilon"));
        return node;
    }

    private KVObject CreateFloatRangeComparisonNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocFloatRangeComparisonNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Float", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));
        node.Add("m_range", CloneRange(compiledNode.GetSubCollection("m_range"), 0.0f, 1.0f));
        node.Add("m_isInclusiveCheck", compiledNode.GetBooleanProperty("m_bIsInclusiveCheck", true));
        return node;
    }

    private KVObject CreateFloatRemapNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocFloatRemapNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Float", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_inputRange", CloneRemapRange(compiledNode.GetSubCollection("m_inputRange")));
        node.Add("m_outputRange", CloneRemapRange(compiledNode.GetSubCollection("m_outputRange")));
        return node;
    }

    private KVObject CreateFloatClampNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocFloatClampNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Value", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_clampRange", CloneRange(compiledNode.GetSubCollection("m_clampRange"), 0.0f, 0.0f));
        return node;
    }

    private KVObject CreateFloatEaseNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocFloatEaseNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Value", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_easing", compiledNode["m_easingOp"] ?? "Linear");
        node.Add("m_flEaseTime", compiledNode.GetFloatProperty("m_flEaseTime", 1.0f));
        node.Add("m_bUseStartValue", compiledNode.GetBooleanProperty("m_bUseStartValue", true));
        node.Add("m_flStartValue", compiledNode.GetFloatProperty("m_flStartValue"));
        return node;
    }

    private KVObject CreateFloatSpringNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocFloatSpringNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Value", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_flHertz", compiledNode.GetFloatProperty("m_flHertz", 4.0f));
        node.Add("m_flDampingRatio", compiledNode.GetFloatProperty("m_flDampingRatio", 0.7f));
        node.Add("m_bUseStartValue", compiledNode.GetBooleanProperty("m_bUseStartValue"));
        node.Add("m_flStartValue", compiledNode.GetFloatProperty("m_flStartValue"));
        return node;
    }

    private KVObject CreateFloatCurveNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocFloatCurveNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Float", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_curve", compiledNode["m_curve"] ?? KVObject.Collection());
        return node;
    }

    private KVObject CreateFloatMathNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocFloatMathNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("A", "Float"), new PinDef("B (Optional)", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_bReturnAbsoluteResult", compiledNode.GetBooleanProperty("m_bReturnAbsoluteResult"));
        node.Add("m_bReturnNegatedResult", compiledNode.GetBooleanProperty("m_bReturnNegatedResult"));
        node.Add("m_operator", compiledNode["m_operator"] ?? "Add");
        node.Add("m_flValueB", compiledNode.GetFloatProperty("m_flValueB"));
        return node;
    }

    private KVObject CreateFloatSwitchNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocFloatSwitchNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Bool", "Bool"),
            new PinDef("If True", "Float"),
            new PinDef("If False", "Float"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_flFalseValue", compiledNode.GetFloatProperty("m_flFalseValue"));
        node.Add("m_flTrueValue", compiledNode.GetFloatProperty("m_flTrueValue", 1.0f));
        return node;
    }

    private KVObject CreateFloatAngleMathNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocFloatAngleMathNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Angle (deg)", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_operation", compiledNode["m_operation"] ?? "ClampTo180");
        return node;
    }

    private KVObject CreateFloatSelectorNode(int nodeIndex, KVObject compiledNode)
    {
        var values = compiledNode.GetFloatArray("m_values")?.ToArray() ?? [];
        var inputPins = values.Select(value => new PinDef($"Option ({value:0.00})", "Bool", IsDynamicPin: true)).ToArray();

        var options = KVObject.Array();
        for (var i = 0; i < values.Length; i++)
        {
            var option = KVObject.Collection();
            option.Add("m_name", $"Option {i}");
            option.Add("m_flValue", values[i]);
            options.Add(option);
        }

        var node = CreateBaseNode("CNmGraphDocFloatSelectorNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins(inputPins));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_options", options);
        node.Add("m_flDefaultValue", compiledNode.GetFloatProperty("m_flDefaultValue"));
        node.Add("m_easing", compiledNode["m_easingOp"] ?? "None");
        node.Add("m_easeTime", compiledNode.GetFloatProperty("m_flEaseTime", 0.3f));
        return node;
    }

    private KVObject CreateIDEventConditionNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocIDEventConditionNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));

        var eventConditionRules = GetEventConditionRules(compiledNode);
        node.Add("m_operator", eventConditionRules.Operator);
        node.Add("m_searchRule", eventConditionRules.SearchRule);
        node.Add("m_bLimitSearchToSourceState", eventConditionRules.LimitSearchToSourceState);
        node.Add("m_bIgnoreInactiveBranchEvents", eventConditionRules.IgnoreInactiveBranchEvents);
        node.Add("m_eventIDs", CloneArray("m_eventIDs", compiledNode));
        return node;
    }

    private KVObject CreateIdEventNode(int nodeIndex, KVObject compiledNode)
    {
        var eventConditionRules = GetEventConditionRules(compiledNode);

        var node = CreateBaseNode("CNmGraphDocIDEventNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "ID", AllowMultipleOutConnections: true)]));
        node.Add("m_defaultValue", compiledNode["m_defaultValue"] ?? string.Empty);
        node.Add("m_bLimitSearchToSourceState", eventConditionRules.LimitSearchToSourceState);
        node.Add("m_priorityRule", eventConditionRules.PriorityRule);
        node.Add("m_bIgnoreInactiveBranchEvents", eventConditionRules.IgnoreInactiveBranchEvents);
        return node;
    }

    private KVObject CreateIdEventPercentageThroughNode(int nodeIndex, KVObject compiledNode)
    {
        var eventConditionRules = GetEventConditionRules(compiledNode);

        var node = CreateBaseNode("CNmGraphDocIDEventPercentageThroughNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_priorityRule", eventConditionRules.PriorityRule);
        node.Add("m_bLimitSearchToSourceState", eventConditionRules.LimitSearchToSourceState);
        node.Add("m_bIgnoreInactiveBranchEvents", eventConditionRules.IgnoreInactiveBranchEvents);
        node.Add("m_eventID", compiledNode["m_eventID"] ?? string.Empty);
        return node;
    }

    private KVObject CreateGraphEventConditionNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocGraphEventConditionNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));

        var eventConditionRules = GetEventConditionRules(compiledNode);
        node.Add("m_operator", eventConditionRules.Operator);
        node.Add("m_bLimitSearchToSourceState", eventConditionRules.LimitSearchToSourceState);
        node.Add("m_bIgnoreInactiveBranchEvents", eventConditionRules.IgnoreInactiveBranchEvents);

        var conditions = KVObject.Array();
        foreach (var value in compiledNode.GetArray("m_conditions") ?? [])
        {
            if (value is not KVObject condition)
            {
                continue;
            }

            var output = KVObject.Collection();
            output.Add("m_eventID", condition["m_eventID"] ?? string.Empty);
            output.Add("m_type", condition["m_eventTypeCondition"] ?? condition["m_type"] ?? "Any");
            conditions.Add(output);
        }

        node.Add("m_conditions", conditions);
        return node;
    }

    private KVObject CreateFootEventConditionNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocFootEventConditionNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));

        var eventConditionRules = GetEventConditionRules(compiledNode);
        node.Add("m_phaseCondition", compiledNode["m_phaseCondition"] ?? "LeftFootDown");
        node.Add("m_bLimitSearchToSourceState", eventConditionRules.LimitSearchToSourceState);
        node.Add("m_bIgnoreInactiveBranchEvents", eventConditionRules.IgnoreInactiveBranchEvents);
        return node;
    }

    private KVObject CreateFootstepEventIdNode(int nodeIndex, KVObject compiledNode)
    {
        var eventConditionRules = GetEventConditionRules(compiledNode);

        var node = CreateBaseNode("CNmGraphDocFootstepEventIDNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("ID", "ID", AllowMultipleOutConnections: true)]));
        node.Add("m_priorityRule", eventConditionRules.PriorityRule);
        node.Add("m_bLimitSearchToSourceState", eventConditionRules.LimitSearchToSourceState);
        node.Add("m_bIgnoreInactiveBranchEvents", eventConditionRules.IgnoreInactiveBranchEvents);
        return node;
    }

    private KVObject CreateFootstepEventPercentageThroughNode(int nodeIndex, KVObject compiledNode)
    {
        var eventConditionRules = GetEventConditionRules(compiledNode);

        var node = CreateBaseNode("CNmGraphDocFootstepEventPercentageThroughNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_phaseCondition", compiledNode["m_phaseCondition"] ?? "LeftFootDown");
        node.Add("m_priorityRule", eventConditionRules.PriorityRule);
        node.Add("m_bLimitSearchToSourceState", eventConditionRules.LimitSearchToSourceState);
        node.Add("m_bIgnoreInactiveBranchEvents", eventConditionRules.IgnoreInactiveBranchEvents);
        return node;
    }

    private KVObject CreateVectorInfoNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocVectorInfoNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Vector", "Vector")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_desiredInfo", compiledNode["m_desiredInfo"] ?? "X");
        return node;
    }

    private KVObject CreateVectorCreateNode(int nodeIndex)
    {
        var node = CreateBaseNode("CNmGraphDocVectorCreateNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Vector", "Vector"),
            new PinDef("X", "Float"),
            new PinDef("Y", "Float"),
            new PinDef("Z", "Float"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Vector", AllowMultipleOutConnections: true)]));
        return node;
    }

    private KVObject CreateTargetInfoNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocTargetInfoNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Target", "Target")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_infoType", compiledNode["m_infoType"] ?? "Distance");
        node.Add("m_bIsWorldSpaceTarget", compiledNode.GetBooleanProperty("m_bIsWorldSpaceTarget", true));
        return node;
    }

    private KVObject CreateTargetPointNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocTargetPointNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Target", "Target")]));
        node.Add("m_outputPins", MakePins([new PinDef("Point", "Vector", AllowMultipleOutConnections: true)]));
        node.Add("m_bIsWorldSpaceTarget", compiledNode.GetBooleanProperty("m_bIsWorldSpaceTarget", true));
        return node;
    }

    private KVObject CreateTargetOffsetNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocTargetOffsetNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Target", "Target")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Target", AllowMultipleOutConnections: true)]));
        node.Add("m_bIsBoneSpaceOffset", compiledNode.GetBooleanProperty("m_bIsBoneSpaceOffset", true));
        node.Add("m_rotationOffset", CloneVector3(compiledNode.GetArray("m_rotationOffset")));
        node.Add("m_translationOffset", CloneVector3(compiledNode.GetArray("m_translationOffset")));
        return node;
    }

    private KVObject CreateBoneMaskNode(int nodeIndex, KVObject compiledNode)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocBoneMaskNode::CData");
        variationData.Add("m_overrideMaskID", string.Empty);

        var node = CreateBaseNode("CNmGraphDocBoneMaskNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Bone Mask", "BoneMask", AllowMultipleOutConnections: true)]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", KVObject.Array());
        node.Add("m_defaultResourceName", string.Empty);
        node.Add("m_maskID", compiledNode.GetStringProperty("m_boneMaskID"));
        return node;
    }

    private KVObject CreateBoneMaskBlendNode(int nodeIndex)
    {
        var node = CreateBaseNode("CNmGraphDocBoneMaskBlendNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Blend Weight", "Float"),
            new PinDef("Source", "BoneMask"),
            new PinDef("Target", "BoneMask"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "BoneMask", AllowMultipleOutConnections: true)]));
        return node;
    }

    private KVObject CreateBoneMaskSelectorNode(int nodeIndex, KVObject compiledNode)
    {
        var parameterValues = compiledNode.GetArray<string>("m_parameterValues")?.ToArray() ?? [];
        var inputPins = new List<PinDef>
        {
            new("ID", "ID"),
            new("Default Mask", "BoneMask"),
        };
        inputPins.AddRange(parameterValues.Select((value, index) => new PinDef(string.IsNullOrEmpty(value) ? $"Mask {index}" : value, "BoneMask")));

        var node = CreateBaseNode("CNmGraphDocBoneMaskSelectorNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins(inputPins));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "BoneMask", AllowMultipleOutConnections: true)]));
        node.Add("m_switchDynamically", compiledNode.GetBooleanProperty("m_bSwitchDynamically"));
        node.Add("m_options", CloneStringArray(parameterValues));
        node.Add("m_flBlendTimeSeconds", compiledNode.GetFloatProperty("m_flBlendTimeSeconds", 0.1f));
        return node;
    }

    private KVObject CreateBoneMaskSwitchNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocBoneMaskSwitchNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Bool", "Bool"),
            new PinDef("If True", "BoneMask"),
            new PinDef("If False", "BoneMask"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "BoneMask", AllowMultipleOutConnections: true)]));
        node.Add("m_bSwitchDynamically", compiledNode.GetBooleanProperty("m_bSwitchDynamically"));
        node.Add("m_flBlendTimeSeconds", compiledNode.GetFloatProperty("m_flBlendTimeSeconds", 0.1f));
        return node;
    }

    private KVObject CreateScaleNode(string className, int nodeIndex, KVObject compiledNode, string valueInputName, string valueFieldName)
    {
        var node = CreateBaseNode(className, MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Input", "Pose"),
            new PinDef(valueInputName, "Float"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Pose", AllowMultipleOutConnections: true)]));
        node.Add(valueFieldName, compiledNode.GetFloatProperty("m_flDefaultInputValue", 1.0f));
        return node;
    }

    private KVObject CreateVelocityBlendNode(int nodeIndex)
    {
        var compiledNode = GetCompiledNode(nodeIndex)
            ?? throw new InvalidDataException($"Missing velocity blend node {nodeIndex}.");
        var sourceCount = compiledNode.GetIntegerArray("m_sourceNodeIndices")?.Length ?? 0;

        var inputPins = new List<PinDef> { new("Parameter", "Float") };
        inputPins.AddRange(Enumerable.Range(0, sourceCount).Select(i => new PinDef($"Input {i}", "Pose")));

        var node = CreateBaseNode("CNmGraphDocVelocityBlendNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins(inputPins));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_bAllowLooping", compiledNode.GetBooleanProperty("m_bAllowLooping", true));
        return node;
    }

    private KVObject CreateBlend1DNode(int nodeIndex, KVObject compiledNode)
    {
        var sourceNodeIndices = compiledNode.GetIntegerArray("m_sourceNodeIndices")?.Select(value => (int)value).ToArray() ?? [];
        var blendRanges = compiledNode.GetSubCollection("m_parameterization")?.GetArray("m_blendRanges")
            ?.Select(value => value as KVObject)
            .Where(value => value is not null)
            .Cast<KVObject>()
            .ToArray() ?? [];

        var inputPins = new List<PinDef> { new("Parameter", "Float") };
        var blendPoints = KVObject.Array();

        for (var i = 0; i < sourceNodeIndices.Length; i++)
        {
            var label = GetNodeName(sourceNodeIndices[i]);
            var value = i < blendRanges.Length
                ? blendRanges[i].GetFloatProperty("m_flBegin")
                : 0.0f;
            var pin = CreatePin($"{label} ({value:0.##})", "Pose", isDynamicPin: true);
            inputPins.Add(new PinDef(pin.GetStringProperty("m_name"), "Pose", IsDynamicPin: true));

            var point = KVObject.Collection();
            point.Add("m_name", label);
            point.Add("m_flValue", value);
            point.Add("m_pinID", pin.GetStringProperty("m_ID"));
            blendPoints.Add(point);
        }

        var node = CreateBaseNode("CNmGraphDocBlend1DNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePinsWithOverrides(inputPins, blendPoints));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));

        var blendSpace = KVObject.Collection();
        blendSpace.Add("m_points", blendPoints);
        node.Add("m_blendSpace", blendSpace);
        node.Add("m_bAllowLooping", compiledNode.GetBooleanProperty("m_bAllowLooping", true));
        return node;
    }

    private KVObject CreateBlend2DNode(int nodeIndex, KVObject compiledNode)
    {
        var values = compiledNode.GetArray("m_values")?.Select(value => value as IReadOnlyList<KVObject>).Where(value => value is not null).Cast<IReadOnlyList<KVObject>>().ToArray() ?? [];
        var sourceNodeIndices = compiledNode.GetIntegerArray("m_sourceNodeIndices")?.Select(value => (int)value).ToArray() ?? [];

        var inputPins = new List<PinDef> { new("X", "Float"), new("Y", "Float") };
        for (var i = 0; i < sourceNodeIndices.Length; i++)
        {
            var label = GetNodeName(sourceNodeIndices[i]);
            var x = i < values.Length && values[i].Count > 0 ? values[i][0] : 0.0f;
            var y = i < values.Length && values[i].Count > 1 ? values[i][1] : 0.0f;
            inputPins.Add(new PinDef($"{label} ({x}, {y})", "Pose", IsDynamicPin: true));
        }

        var blendSpace = KVObject.Collection();
        blendSpace.Add("m_pointNames", CloneStringArray(sourceNodeIndices.Select(GetNodeName)));
        blendSpace.Add("m_points", CloneNestedVectorArray(compiledNode.GetArray("m_values")));
        blendSpace.Add("m_indices", CloneArray("m_indices", compiledNode));
        blendSpace.Add("m_hullIndices", CloneArray("m_hullIndices", compiledNode));

        var node = CreateBaseNode("CNmGraphDocBlend2DNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins(inputPins));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_blendSpace", blendSpace);
        node.Add("m_bAllowLooping", compiledNode.GetBooleanProperty("m_bAllowLooping", true));
        return node;
    }

    private KVObject CreateScaleMaskNode(int nodeIndex)
    {
        var node = CreateBaseNode("CNmGraphDocScaleNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Input", "Pose"),
            new PinDef("Mask", "BoneMask"),
            new PinDef("Enable", "Bool"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Pose", AllowMultipleOutConnections: true)]));
        return node;
    }

    private KVObject CreateCurrentSyncEventIdNode(int nodeIndex)
    {
        var node = CreateBaseNode("CNmGraphDocCurrentSyncEventIDNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "ID", AllowMultipleOutConnections: true)]));
        return node;
    }

    private KVObject CreateCurrentSyncEventNode(int nodeIndex, string infoType)
    {
        var node = CreateBaseNode("CNmGraphDocCurrentSyncEventNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_infoType", infoType);
        return node;
    }

    private KVObject CreateSyncEventIndexConditionNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocSyncEventIndexConditionNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));
        node.Add("m_triggerMode", compiledNode["m_triggerMode"] ?? "ExactlyAtEventIndex");
        node.Add("m_nSyncEventIdx", compiledNode.GetInt64Property("m_syncEventIdx", -1));
        return node;
    }

    private KVObject CreateTimeConditionNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocTimeConditionNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Time Value (optional)", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));
        node.Add("m_flComparand", compiledNode.GetFloatProperty("m_flComparand"));
        node.Add("m_type", compiledNode["m_type"] ?? "ElapsedTime");
        node.Add("m_operator", compiledNode["m_operator"] ?? "LessThan");
        return node;
    }

    private KVObject CreateTransitionEventConditionNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocTransitionEventConditionNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));

        var eventConditionRules = GetEventConditionRules(compiledNode);
        node.Add("m_ruleCondition", compiledNode["m_ruleCondition"] ?? "AnyAllowed");
        node.Add("m_bMatchOnlySpecificMarkerID", !string.IsNullOrEmpty(compiledNode.GetStringProperty("m_requireRuleID")));
        node.Add("m_markerIDToMatch", compiledNode["m_requireRuleID"] ?? string.Empty);
        node.Add("m_bLimitSearchToSourceState", eventConditionRules.LimitSearchToSourceState);
        node.Add("m_bIgnoreInactiveBranchEvents", eventConditionRules.IgnoreInactiveBranchEvents);
        return node;
    }

    private KVObject CreateLayerBlendNode(int nodeIndex, KVObject compiledNode)
    {
        var layerDefinitions = compiledNode.GetArray("m_layerDefinition")?.Select(value => value as KVObject).Where(value => value is not null).Cast<KVObject>().ToArray() ?? [];

        var inputPins = new List<PinDef> { new("Base Node", "Pose") };
        inputPins.AddRange(Enumerable.Range(0, layerDefinitions.Length).Select(i => new PinDef($"Layer {i}", "Special")));

        var node = CreateBaseNode("CNmGraphDocLayerBlendNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins(inputPins));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_onlySampleBaseRootMotion", compiledNode.GetBooleanProperty("m_bOnlySampleBaseRootMotion", true));
        return node;
    }

    private KVObject CreateLayerBlendInputNode(int parentNodeIndex, int layerIndex, KVObject layerDefinition)
    {
        var isStateMachineLayer = layerDefinition.GetBooleanProperty("m_bIsStateMachineLayer");
        var node = CreateBaseNode(
            isStateMachineLayer ? "CNmGraphDocStateMachineLayerNode" : "CNmGraphDocLocalLayerNode",
            MakeGuid($"node:{parentNodeIndex}:layer:{layerIndex}"),
            $"Layer {layerIndex}");

        node.Add("m_inputPins", isStateMachineLayer
            ? MakePins([new PinDef("State Machine", "Pose")])
            : MakePins([
                new PinDef("Input", "Pose"),
                new PinDef("Weight", "Float"),
                new PinDef("Root Motion Weight", "Float"),
                new PinDef("BoneMask", "BoneMask"),
            ]));
        node.Add("m_outputPins", MakePins([new PinDef("Layer", "Special")]));
        node.Add("m_isSynchronized", layerDefinition.GetBooleanProperty("m_bIsSynchronized"));
        node.Add("m_ignoreEvents", layerDefinition.GetBooleanProperty("m_bIgnoreEvents"));
        node.Add("m_blendMode", layerDefinition["m_blendMode"] ?? "Overlay");
        return node;
    }

    private KVObject CreateAimCsNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocAimCSNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Input", "Pose"),
            new PinDef("Horizontal Aim Angle", "Float"),
            new PinDef("Vertical Aim Angle", "Float"),
            new PinDef("Weapon Category", "ID"),
            new PinDef("Weapon Type", "ID"),
            new PinDef("Weapon Action", "ID"),
            new PinDef("Weapon Drop", "Float"),
            new PinDef("Crouch Weight", "Float"),
            new PinDef("Is Defusing", "Bool"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Pose", AllowMultipleOutConnections: true)]));
        node.Add("m_flActionBlendTimeSeconds", compiledNode.GetFloatProperty("m_flActionBlendTimeSeconds"));
        node.Add("m_flHandIKBlendInTimeSeconds", compiledNode.GetFloatProperty("m_flHandIKBlendInTimeSeconds"));
        node.Add("m_flPlantingBlendTimeSeconds", compiledNode.GetFloatProperty("m_flPlantingBlendTimeSeconds"));
        return node;
    }

    private KVObject CreateFootIkNode(int nodeIndex, KVObject compiledNode)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CnmGraphDocFootIKNode::CData");
        variationData.Add("m_leftEffectorBoneName", compiledNode["m_leftEffectorBoneID"] ?? string.Empty);
        variationData.Add("m_rightEffectorBoneName", compiledNode["m_rightEffectorBoneID"] ?? string.Empty);
        variationData.Add("m_flBlendTimeSeconds", compiledNode.GetFloatProperty("m_flBlendTimeSeconds"));

        var node = CreateBaseNode("CnmGraphDocFootIKNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Input", "Pose"),
            new PinDef("Left Foot Target", "Target"),
            new PinDef("Right Foot Target", "Target"),
            new PinDef("Enabled", "Bool"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Pose", AllowMultipleOutConnections: true)]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", KVObject.Array());
        node.Add("m_defaultResourceName", string.Empty);
        node.Add("m_bIsTargetInWorldSpace", compiledNode.GetBooleanProperty("m_bIsTargetInWorldSpace"));
        node.Add("m_blendMode", compiledNode["m_blendMode"] ?? "Effector");
        return node;
    }

    private KVObject CreateTwoBoneIkNode(int nodeIndex, KVObject compiledNode)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CnmGraphDocTwoBoneIKNode::CData");
        variationData.Add("m_effectorBoneName", compiledNode["m_effectorBoneID"] ?? string.Empty);
        variationData.Add("m_flBlendTimeSeconds", compiledNode.GetFloatProperty("m_flBlendTimeSeconds"));

        var node = CreateBaseNode("CnmGraphDocTwoBoneIKNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Input", "Pose"),
            new PinDef("Target", "Target"),
            new PinDef("Enabled", "Bool"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Pose", AllowMultipleOutConnections: true)]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", KVObject.Array());
        node.Add("m_defaultResourceName", string.Empty);
        node.Add("m_bIsTargetInWorldSpace", compiledNode.GetBooleanProperty("m_bIsTargetInWorldSpace"));
        node.Add("m_blendMode", compiledNode["m_blendMode"] ?? "Effector");
        node.Add("m_flChainRotationWeight", compiledNode.GetFloatProperty("m_flChainRotationWeight"));
        return node;
    }

    private KVObject CreateOrientationWarpNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocOrientationWarpNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Input", "Pose"),
            new PinDef("Direction (Character)", "Vector"),
            new PinDef("Angle Offset (Deg)", "Float"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Pose", AllowMultipleOutConnections: true)]));
        node.Add("m_offsetType", compiledNode.GetBooleanProperty("m_bIsOffsetNode")
            ? (compiledNode.GetBooleanProperty("m_bIsOffsetRelativeToCharacter") ? "RelativeToCharacter" : "Absolute")
            : "None");
        node.Add("m_samplingMode", compiledNode["m_samplingMode"] ?? "WorldSpace");
        node.Add("m_bWarpTranslation", compiledNode.GetBooleanProperty("m_bWarpTranslation"));
        return node;
    }

    private KVObject CreateConstVectorNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CnmGraphDocConstVectorNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Value", "Vector", AllowMultipleOutConnections: true)]));
        node.Add("m_value", CloneVector3(compiledNode.GetArray("m_value")));
        return node;
    }

    private KVObject CreateConstTargetNode(int nodeIndex, KVObject compiledNode)
    {
        var targetValue = compiledNode.GetSubCollection("m_value");

        var node = CreateBaseNode("CnmGraphDocConstTargetNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Value", "Target", AllowMultipleOutConnections: true)]));
        node.Add("m_rotation", CloneVector3(targetValue?.GetArray("m_rotation")?.OfType<KVObject>().ToArray()));
        node.Add("m_translation", CloneVector3(targetValue?.GetArray("m_translation")?.OfType<KVObject>().ToArray()));
        return node;
    }

    private KVObject CreateConstBoneTargetNode(int nodeIndex, KVObject compiledNode)
    {
        var targetValue = compiledNode.GetSubCollection("m_value");

        var node = CreateBaseNode("CnmGraphDocConstBoneTargetNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Value", "Target", AllowMultipleOutConnections: true)]));
        node.Add("m_boneName", targetValue?["m_boneID"] ?? compiledNode["m_boneName"] ?? string.Empty);
        return node;
    }

    private KVObject CreateSnapWeaponNode(int nodeIndex)
    {
        var node = CreateBaseNode("CnmGraphDocSnapWeaponNode", MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Input", "Pose"),
            new PinDef("Flashed Amount", "Float"),
            new PinDef("Weapon Category", "ID"),
            new PinDef("Weapon Type", "ID"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Pose", AllowMultipleOutConnections: true)]));
        return node;
    }

    private KVObject CreateTransitionResultNode(string className, int transitionNodeIndex, bool canBeForced, string? stateId, int row)
    {
        var compiledNode = GetCompiledNode(transitionNodeIndex)
            ?? throw new InvalidDataException($"Missing transition node {transitionNodeIndex}.");

        var node = CreateBaseNode(className, MakeGuid($"transition:{transitionNodeIndex}:{className}"), GetNodeName(transitionNodeIndex));
        node["m_position"] = MakeVector2(0.0f, row * 220.0f);
        node.Add("m_inputPins", MakePins([
            new PinDef("Condition", "Bool"),
            new PinDef("Duration Override", "Float"),
            new PinDef("Time Offset Override", "Float"),
            new PinDef("Start Bone Mask", "BoneMask"),
            new PinDef("Target Sync ID", "ID"),
        ]));
        node.Add("m_outputPins", KVObject.Array());
        node.Add("m_resultType", "Special");
        node.Add("m_flDurationSeconds", compiledNode.GetFloatProperty("m_flDuration"));
        node.Add("m_bClampDurationToSource", false);
        node.Add("m_rootMotionBlend", compiledNode.GetStringProperty("m_rootMotionBlend"));
        node.Add("m_blendWeightEasing", compiledNode.GetStringProperty("m_blendWeightEasing"));
        node.Add("m_flBoneMaskBlendInTimePercentage", compiledNode.GetSubCollection("m_boneMaskBlendInTimePercentage")?.GetFloatProperty("m_flValue") ?? 0.33f);
        node.Add("m_timeMatchMode", "None");
        node.Add("m_flTimeOffset", compiledNode.GetFloatProperty("m_flTimeOffset"));
        node.Add("m_bCanBeForced", canBeForced);

        if (stateId is not null)
        {
            node.Add("m_stateID", stateId);
        }

        return node;
    }

    private KVObject CreateSimpleNode(string className, int nodeIndex, IReadOnlyList<PinDef> inputPins, IReadOnlyList<PinDef> outputPins)
    {
        var node = CreateBaseNode(className, MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins(inputPins));
        node.Add("m_outputPins", MakePins(outputPins));
        return node;
    }

    private KVObject CreateConstValueNode(string className, int nodeIndex, string outputType, string valueKey, object? value)
    {
        var node = CreateBaseNode(className, MakeGuid($"node:{nodeIndex}"), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Value", outputType, AllowMultipleOutConnections: true)]));
        AddValue(node, valueKey, value ?? string.Empty);
        return node;
    }

    private KVObject CreatePoseResultNode(string key)
        => CreateResultNode("CNmGraphDocPoseResultNode", key, "Out", "Pose");

    private KVObject CreateStateLayerResultNode(string key)
    {
        var node = CreateBaseNode("CNmGraphDocStateLayerDataNode", MakeGuid($"result:{key}"), string.Empty);
        node.Add("m_inputPins", MakePins([
            new PinDef("Layer Weight", "Float"),
            new PinDef("Root Motion Weight", "Float"),
            new PinDef("Layer Mask", "BoneMask"),
        ]));
        node.Add("m_outputPins", KVObject.Array());
        node.Add("m_resultType", "Special");
        return node;
    }

    private KVObject CreateEntryOverrideConditionsNode(int stateMachineNodeIndex)
    {
        var node = CreateBaseNode("CNmGraphDocEntryStateOverrideConditionsNode", MakeGuid($"entry-conditions:{stateMachineNodeIndex}"), string.Empty);
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", KVObject.Array());
        node.Add("m_resultType", "Special");
        node.Add("m_pinToStateMapping", KVObject.Array());
        return node;
    }

    private KVObject CreateEntryOverrideNode(int stateNodeIndex, string stateId, int row)
    {
        var node = CreateBaseNode("CNmGraphDocEntryOverrideNode", MakeGuid($"entry-override:{stateNodeIndex}"), GetNodeName(stateNodeIndex));
        node["m_position"] = MakeVector2(0.0f, 200.0f + row * 220.0f);
        node.Add("m_inputPins", MakePins([new PinDef("Condition", "Bool")]));
        node.Add("m_outputPins", KVObject.Array());
        node.Add("m_resultType", "Special");
        node.Add("m_stateID", stateId);
        return node;
    }

    private KVObject CreateResultNode(string className, string key, string inputName, string inputType)
    {
        var node = CreateBaseNode(className, MakeGuid($"result:{key}"), string.Empty);
        node.Add("m_inputPins", MakePins([new PinDef(inputName, inputType)]));
        node.Add("m_outputPins", KVObject.Array());
        node.Add("m_resultType", inputType);
        return node;
    }

    private KVObject BuildSelectorConditionGraph(int nodeIndex, IReadOnlyList<int> conditionNodeIndices, IReadOnlyList<string> optionLabels, FlowGraphBuilder parentGraphBuilder)
    {
        var graphBuilder = new FlowGraphBuilder(this, $"selector:{nodeIndex}:conditions", "ValueTree");
        var conditionNode = CreateSelectorConditionNode(nodeIndex, optionLabels);
        graphBuilder.Nodes.Add(conditionNode);

        for (var i = 0; i < Math.Min(conditionNodeIndices.Count, optionLabels.Count); i++)
        {
            if (conditionNodeIndices[i] < 0)
            {
                continue;
            }

            var sourceNode = BuildFlowNode(conditionNodeIndices[i], graphBuilder);
            graphBuilder.Connect(sourceNode.GetStringProperty("m_ID"), GetOutputPinId(sourceNode, 0), conditionNode.GetStringProperty("m_ID"), GetInputPinId(conditionNode, i));
        }

        return graphBuilder.ToGraph();
    }

    private KVObject CreateSelectorConditionNode(int nodeIndex, IReadOnlyList<string> optionLabels)
    {
        var node = CreateBaseNode("CNmGraphDocSelectorConditionNode", MakeGuid($"selector-conditions:{nodeIndex}"), string.Empty);
        node.Add("m_inputPins", MakePins(optionLabels.Select(label => new PinDef(label, "Bool", IsDynamicPin: true))));
        node.Add("m_outputPins", KVObject.Array());
        node.Add("m_resultType", "Special");
        return node;
    }

    private static KVObject CloneArray(string key, KVObject source)
    {
        var array = source.GetArray(key);
        if (array is null)
        {
            return KVObject.Array();
        }

        var clone = KVObject.Array();
        foreach (var value in array)
        {
            clone.Add(value);
        }

        return clone;
    }

    private static KVObject CloneStringArray(IEnumerable<string> values)
    {
        var array = KVObject.Array();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static KVObject CloneNestedVectorArray(IReadOnlyList<KVObject>? source)
    {
        var array = KVObject.Array();
        foreach (var value in source ?? [])
        {
            if (value is IReadOnlyList<KVObject> vector)
            {
                array.Add(CloneVector3(vector));
            }
        }

        return array;
    }

    private static KVObject ConvertTimedStateEvents(string type, string key, KVObject source)
    {
        var input = source.GetArray(key);
        var result = KVObject.Array();

        if (input is null)
        {
            return result;
        }

        foreach (var value in input)
        {
            if (value is not KVObject timedEvent)
            {
                continue;
            }

            var output = KVObject.Collection();
            output.Add("m_ID", timedEvent["m_ID"] ?? string.Empty);
            output.Add("m_type", type);
            output.Add("m_comparisonOperator", timedEvent["m_comparisionOperator"] ?? "LessThanEqual");
            output.Add("m_flTimeValueSeconds", timedEvent.GetFloatProperty("m_flTimeValueSeconds"));
            result.Add(output);
        }

        return result;
    }

    private static IReadOnlyList<PinDef> CreateRepeatedPins(string name, string type, int count)
        => Enumerable.Range(0, count).Select(_ => new PinDef(name, type)).ToArray();

    private static int GetDynamicInputCount(KVObject node, string key, int fallback)
        => node.GetIntegerArray(key)?.Length ?? fallback;

    private static bool IsControlParameterNode(KVObject compiledNode)
        => GetCompiledClass(compiledNode).TryGetTypedSuffix("ControlParameter", out _);

    private static bool IsVirtualParameterNode(KVObject compiledNode)
        => GetCompiledClass(compiledNode).TryGetTypedSuffix("VirtualParameter", out _);

    private static string GetCompiledClassName(KVObject node)
        => node.GetStringProperty("_class");

    private static CompiledNodeClass GetCompiledClass(KVObject node)
        => new(GetCompiledClassName(node));

    private static string GetSimpleDocNodeClassName(string stem)
        => $"CNmGraphDoc{stem}Node";

    private static string GetTypedDocNodeClassName(string valueType, string suffix)
        => $"CNmGraphDoc{valueType}{suffix}Node";

    private static IReadOnlyList<(string Key, object Value)> GetControlParameterExtraFields(string valueType)
        => valueType switch
        {
            "Float" => [("m_previewStartValue", 0.0f), ("m_previewMin", 0.0f), ("m_previewMax", 1.0f)],
            "Bool" => [("m_previewStartValue", false)],
            "ID" => [("m_previewStartValue", string.Empty), ("m_expectedValues", KVObject.Array())],
            "Vector" or "Target" => [],
            _ => throw new InvalidDataException($"Unsupported control parameter value type: {valueType}"),
        };

    private static string GetConstValueKey(string valueType)
        => valueType switch
        {
            "ID" => "m_value",
            "Float" => "m_flValue",
            "Bool" => "m_bValue",
            _ => throw new InvalidDataException($"Unsupported const value type: {valueType}"),
        };

    private static object? GetConstValue(KVObject compiledNode, string valueType)
        => valueType switch
        {
            "ID" => compiledNode.ContainsKey("m_value") ? compiledNode["m_value"] : string.Empty,
            "Float" => compiledNode.ContainsKey("m_value") ? compiledNode["m_value"] : compiledNode["m_flValue"],
            "Bool" => compiledNode.ContainsKey("m_value") ? compiledNode["m_value"] : compiledNode["m_bValue"],
            _ => throw new InvalidDataException($"Unsupported const value type: {valueType}"),
        };

    private static KVObject CloneRange(KVObject? source, float defaultMin, float defaultMax)
    {
        var range = KVObject.Collection();
        range.Add("m_flMin", source?.GetFloatProperty("m_flMin", defaultMin) ?? defaultMin);
        range.Add("m_flMax", source?.GetFloatProperty("m_flMax", defaultMax) ?? defaultMax);
        return range;
    }

    private static KVObject CloneRemapRange(KVObject? source)
    {
        var range = KVObject.Collection();
        range.Add("m_flBegin", source?.GetFloatProperty("m_flBegin") ?? 0.0f);
        range.Add("m_flEnd", source?.GetFloatProperty("m_flEnd") ?? 0.0f);
        return range;
    }

    private static KVObject CloneVector3(IReadOnlyList<KVObject>? source)
    {
        var vector = KVObject.Array();
        vector.Add(source is not null && source.Count > 0 ? source[0] : 0.0f);
        vector.Add(source is not null && source.Count > 1 ? source[1] : 0.0f);
        vector.Add(source is not null && source.Count > 2 ? source[2] : 0.0f);
        return vector;
    }

    private static EventConditionRulesData GetEventConditionRules(KVObject compiledNode)
    {
        var flags = (uint)(compiledNode.GetSubCollection("m_eventConditionRules")?.GetInt64Property("m_flags")
            ?? compiledNode.GetSubCollection("m_rules")?.GetInt64Property("m_flags")
            ?? 0);

        return new EventConditionRulesData(
            Operator: (flags & 0x2) != 0 ? "And" : "Or",
            SearchRule: (flags & 0x40) != 0
                ? "OnlySearchGraphEvents"
                : (flags & 0x80) != 0 ? "SearchAll" : "OnlySearchAnimEvents",
            PriorityRule: (flags & 0x10) != 0 ? "HighestPercentageThrough" : "HighestWeight",
            LimitSearchToSourceState: (flags & 0x100) != 0,
            IgnoreInactiveBranchEvents: (flags & 0x20) != 0);
    }

    private static string GetPathParent(string path)
    {
        var separatorIndex = path.LastIndexOf('/');
        return separatorIndex < 0 ? string.Empty : path[..separatorIndex];
    }

    private static string GetPathLeaf(string path)
    {
        var separatorIndex = path.LastIndexOf('/');
        return separatorIndex < 0 ? path : path[(separatorIndex + 1)..];
    }

    private string GetNodeName(int nodeIndex)
    {
        if (nodeIndex < 0 || nodeIndex >= nodePaths.Length)
        {
            return $"Node {nodeIndex}";
        }

        return GetPathLeaf(nodePaths[nodeIndex]);
    }

    private string GetResourcePath(int dataSlotIndex)
        => dataSlotIndex >= 0 && dataSlotIndex < resources.Length ? resources[dataSlotIndex] : string.Empty;

    private KVObject? GetCompiledNode(int nodeIndex)
        => nodeIndex >= 0 && nodeIndex < compiledNodes.Count ? compiledNodes[nodeIndex] : null;

    private static KVObject CreateBaseNode(string className, string nodeId, string name)
    {
        var node = KVObject.Collection();
        node.Add("_class", className);
        node.Add("m_ID", nodeId);
        node.Add("m_name", name);
        node.Add("m_floatingComment", string.Empty);
        node.Add("m_position", MakeVector2(0.0f, 0.0f));
        node.Add("m_pChildGraph", null);
        node.Add("m_pSecondaryGraph", null);
        return node;
    }

    private static KVObject MakePins(IEnumerable<PinDef> pins)
    {
        var array = KVObject.Array();
        foreach (var pin in pins)
        {
            array.Add(CreatePin(pin.Name, pin.Type, pin.IsDynamicPin, pin.AllowMultipleOutConnections));
        }

        return array;
    }

    private static KVObject MakePinsWithOverrides(IReadOnlyList<PinDef> pins, KVObject overridePoints)
    {
        var array = KVObject.Array();
        var overrideIndex = 0;

        foreach (var pin in pins)
        {
            if (pin.IsDynamicPin && pin.Type == "Pose" && overrideIndex < overridePoints.Count)
            {
                var point = (KVObject)overridePoints[overrideIndex++]!;
                array.Add(CreatePinWithId(pin.Name, pin.Type, point.GetStringProperty("m_pinID"), pin.IsDynamicPin, pin.AllowMultipleOutConnections));
                continue;
            }

            array.Add(CreatePin(pin.Name, pin.Type, pin.IsDynamicPin, pin.AllowMultipleOutConnections));
        }

        return array;
    }

    private static KVObject CreatePin(string name, string type, bool isDynamicPin = false, bool allowMultipleOutConnections = false)
        => CreatePinWithId(name, type, MakeGuid($"pin:{type}:{name}:{Guid.NewGuid():N}"), isDynamicPin, allowMultipleOutConnections);

    private static KVObject CreatePinWithId(string name, string type, string id, bool isDynamicPin = false, bool allowMultipleOutConnections = false)
    {
        var pin = KVObject.Collection();
        pin.Add("m_ID", id);
        pin.Add("m_name", name);
        pin.Add("m_type", type);
        pin.Add("m_bIsDynamicPin", isDynamicPin);
        pin.Add("m_bAllowMultipleOutConnections", allowMultipleOutConnections);
        return pin;
    }

    private static string GetInputPinId(KVObject node, int inputIndex)
        => node.GetArray("m_inputPins")![inputIndex]!.GetStringProperty("m_ID");

    private static string GetOutputPinId(KVObject node, int outputIndex)
        => node.GetArray("m_outputPins")![outputIndex]!.GetStringProperty("m_ID");

    private static KVObject MakeVector2(float x, float y)
    {
        var vector = KVObject.Array();
        vector.Add(x);
        vector.Add(y);
        return vector;
    }

    private static string MakeGuid(string seed)
    {
        var bytes = Encoding.UTF8.GetBytes(seed);
        var hash = MD5.HashData(bytes);
        return new Guid(hash).ToString();
    }

    private static void AddValue(KVObject node, string key, object? value)
    {
        switch (value)
        {
            case null:
                node.Add(key, null);
                break;
            case KVObject kv:
                node.Add(key, kv);
                break;
            case string s:
                node.Add(key, s);
                break;
            case bool b:
                node.Add(key, b);
                break;
            case int i:
                node.Add(key, i);
                break;
            case long l:
                node.Add(key, l);
                break;
            case float f:
                node.Add(key, f);
                break;
            case double d:
                node.Add(key, d);
                break;
            default:
                throw new InvalidDataException($"Unsupported KV3 value type {value.GetType()} for key '{key}'.");
        }
    }

    private readonly record struct PinDef(string Name, string Type, bool IsDynamicPin = false, bool AllowMultipleOutConnections = false);

    private readonly record struct EventConditionRulesData(string Operator, string SearchRule, string PriorityRule, bool LimitSearchToSourceState, bool IgnoreInactiveBranchEvents);

    private readonly record struct CompiledNodeClass(string Name)
    {
        private const string Prefix = "CNm";
        private const string Suffix = "Node::CDefinition";

        public string Stem
        {
            get
            {
                if (!Name.StartsWith(Prefix, StringComparison.Ordinal) || !Name.EndsWith(Suffix, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Unsupported compiled NmGraph class name: {Name}");
                }

                return Name[Prefix.Length..^Suffix.Length];
            }
        }

        public bool TryGetTypedSuffix(string prefix, out string valueType)
        {
            if (Stem.StartsWith(prefix, StringComparison.Ordinal))
            {
                valueType = Stem[prefix.Length..];
                return !string.IsNullOrEmpty(valueType);
            }

            valueType = string.Empty;
            return false;
        }
    }

    private sealed class FlowGraphBuilder
    {
        private readonly NmGraphExtract owner;

        public string GraphKey { get; }
        public string GraphType { get; }
        public Dictionary<int, string> NodeIdsByCompiledIndex { get; } = [];
        public List<KVObject> Nodes { get; } = [];
        public List<KVObject> Connections { get; } = [];

        public FlowGraphBuilder(NmGraphExtract owner, string graphKey, string graphType)
        {
            this.owner = owner;
            GraphKey = graphKey;
            GraphType = graphType;
        }

        public void Connect(string fromNodeId, string outputPinId, string toNodeId, string inputPinId)
        {
            var connection = KVObject.Collection();
            connection.Add("m_ID", MakeGuid($"connection:{GraphKey}:{fromNodeId}:{outputPinId}:{toNodeId}:{inputPinId}"));
            connection.Add("m_fromNodeID", fromNodeId);
            connection.Add("m_outputPinID", outputPinId);
            connection.Add("m_toNodeID", toNodeId);
            connection.Add("m_inputPinID", inputPinId);
            Connections.Add(connection);
        }

        public KVObject ToGraph()
        {
            var graph = KVObject.Collection();
            graph.Add("_class", "CNmGraphDocFlowGraph");
            graph.Add("m_ID", MakeGuid($"graph:{GraphKey}"));

            var nodesArray = KVObject.Array();
            foreach (var node in Nodes)
            {
                nodesArray.Add(node);
            }

            var connectionsArray = KVObject.Array();
            foreach (var connection in Connections)
            {
                connectionsArray.Add(connection);
            }

            graph.Add("m_nodes", nodesArray);
            graph.Add("m_graphType", GraphType);
            graph.Add("m_viewOffset", MakeVector2(0.0f, 0.0f));
            graph.Add("m_flViewZoom", 1.0f);
            graph.Add("m_connections", connectionsArray);
            return graph;
        }
    }

    private enum StateMachineTransitionGroup
    {
        Standard,
        Global,
    }

    private sealed class TransitionInfo
    {
        public StateMachineTransitionGroup GroupKind { get; init; }
        public string GroupPath { get; init; } = string.Empty;
        public int SourceStateNodeIndex { get; init; }
        public int TargetStateIndex { get; init; }
        public int TargetStateNodeIndex { get; init; }
        public int ConditionNodeIndex { get; init; }
        public int TransitionNodeIndex { get; init; }
        public KVObject CompiledTransitionNode { get; init; } = null!;
        public KVObject StateMachineTransition { get; init; } = null!;
        public bool CanBeForced { get; init; }
    }
}
