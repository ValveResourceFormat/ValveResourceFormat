using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.Animation;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

internal static class NmGraphExtractExtensions
{
    public static string GetRequiredStringProperty(this KVObject node, string key)
    {
        if (!node.TryGetValue(key, out var value) || value.IsNull)
        {
            throw new InvalidDataException($"Missing required KV3 property '{key}' on '{node.GetStringProperty("_class", "<unknown>")}'.");
        }

        return node.GetStringProperty(key);
    }

    public static long GetInt64Property(this KVObject node, string key, long defaultValue)
        => node.TryGetValue(key, out var value) && !value.IsNull
            ? node.GetIntegerProperty(key, defaultValue)
            : defaultValue;

    public static float GetRequiredFloatProperty(this KVObject node, string key)
    {
        if (!node.TryGetValue(key, out var value) || value.IsNull)
        {
            throw new InvalidDataException($"Missing required KV3 property '{key}' on '{node.GetStringProperty("_class", "<unknown>")}'.");
        }

        return node.GetFloatProperty(key);
    }
}

/// <summary>
/// Extracts Source 2 AnimGraph 2 graphs to editable document format.
/// </summary>
public sealed partial class NmGraphExtract : IDisposable
{
    private readonly Resource _resource;
    private readonly IFileLoader _fileLoader;
    private readonly KVObject _graph;
    private readonly GraphDefinition _definition;
    private readonly KVObject?[] _compiledNodes;
    private readonly Animation.GraphNode?[] _definitions;
    private readonly bool[] _definitionCreated;
    private readonly string[] _nodePaths;
    private readonly Dictionary<int, string> _virtualParameterIdsByNodeIndex = [];
    private readonly HashSet<int> _persistentNodeIndices;
    private readonly Dictionary<int, string> _rootParameterNodeIds = [];
    private readonly Dictionary<string, float?> _clipAverageLinearVelocityCache = [];
    private readonly List<VariationGraph> _variationGraphs = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="NmGraphExtract"/> class.
    /// </summary>
    public NmGraphExtract(Resource resource, IFileLoader? fileLoader = null)
    {
        _resource = resource;
        _fileLoader = fileLoader ?? new NullFileLoader();
        var resourceData = resource.DataBlock as BinaryKV3
            ?? throw new InvalidDataException("Resource DataBlock is not a BinaryKV3.");
        _graph = resourceData.Data;
        var compiledNodes = _graph.GetArray("m_nodes")
            ?? throw new InvalidDataException("NmGraph is missing m_nodes.");
        _compiledNodes = [.. compiledNodes];
        _definitions = new Animation.GraphNode?[_compiledNodes.Length];
        _definitionCreated = new bool[_compiledNodes.Length];
        _definition = new GraphDefinition(_graph);
        _nodePaths = _definition.NodePaths;
        for (var i = 0; i < Math.Min(_definition.VirtualParameterIDs.Length, _definition.VirtualParameterNodeIndices.Length); i++)
        {
            _virtualParameterIdsByNodeIndex[_definition.VirtualParameterNodeIndices[i]] = _definition.VirtualParameterIDs[i].Name;
        }
        _persistentNodeIndices = [.. _definition.PersistentNodeIndices.Select(static value => (int)value)];
        LoadVariationGraphs();
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
            FileName = _resource.FileName is not null && _resource.FileName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal)
                ? _resource.FileName[..^2]
                : _resource.FileName ?? "graph.vnmgraph",
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var variationGraph in _variationGraphs)
        {
            variationGraph.Dispose();
        }

        _variationGraphs.Clear();
    }

    private KVObject BuildRootGraph()
    {
        var graphBuilder = new FlowGraphBuilder("root", "BlendTree");

        foreach (var nodeIndex in _persistentNodeIndices.Order())
        {
            var compiledNode = GetCompiledNode(nodeIndex);
            if (compiledNode is null)
            {
                continue;
            }

            if (IsControlParameterNode(compiledNode) || IsVirtualParameterNode(nodeIndex))
            {
                var node = BuildPersistentRootNode(nodeIndex);
                graphBuilder.Nodes.Add(node);
            }
        }

        var rootNode = BuildFlowNode(_definition.RootNodeIdx, graphBuilder);
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
            _ when compiledClass.TryGetTypedSuffix("ControlParameter", out var valueType)
                => CreateControlParameterNode(nodeIndex, GetTypedDocNodeClassName(valueType, "ControlParameter"), valueType, "Value", GetControlParameterExtraFields(valueType)),
            _ when IsVirtualParameterNode(nodeIndex)
                => CreateVirtualParameterNode(nodeIndex),
            _ => throw new InvalidDataException($"Unsupported persistent NmGraph node: {compiledClass.Name}"),
        };
    }

    private KVObject CreateControlParameterNode(int nodeIndex, string className, string outputType, string outputName, IReadOnlyList<(string Key, object Value)> extraFields)
    {
        var nodeId = GetOrCreateRootParameterNodeId(nodeIndex);

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

    private KVObject CreateVirtualParameterNode(int nodeIndex)
    {
        var valueType = GetVirtualParameterValueType(nodeIndex);

        var nodeId = GetOrCreateRootParameterNodeId(nodeIndex);

        var node = CreateBaseNode(GetTypedDocNodeClassName(valueType, "VirtualParameter"), nodeId, GetVirtualParameterName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Value", valueType, AllowMultipleOutConnections: true)]));
        node.Add("m_groupName", string.Empty);
        node["m_pChildGraph"] = BuildVirtualParameterGraph(nodeIndex, GetTypedDocNodeClassName(valueType, "Result"), valueType);
        return node;
    }

    private KVObject BuildVirtualParameterGraph(int nodeIndex, string resultClassName, string resultType)
    {
        var compiledNode = GetCompiledNode(nodeIndex)!;
        var childNodeIndex = GetDefinition(nodeIndex) switch
        {
            VirtualParameterBoolNode parameter => parameter.ChildNodeIdx,
            VirtualParameterFloatNode parameter => parameter.ChildNodeIdx,
            VirtualParameterIDNode parameter => parameter.ChildNodeIdx,
            VirtualParameterVectorNode parameter => parameter.ChildNodeIdx,
            VirtualParameterTargetNode parameter => parameter.ChildNodeIdx,
            VirtualParameterBoneMaskNode parameter => parameter.ChildNodeIdx,
            _ => nodeIndex,
        };
        var graphBuilder = new FlowGraphBuilder($"virtual:{nodeIndex}", "VirtualParameterValueTree");

        KVObject childNode;
        if (childNodeIndex == nodeIndex)
        {
            childNode = BuildFlowNodeInternal(childNodeIndex, compiledNode, graphBuilder);
            graphBuilder.NodeIdsByCompiledIndex[childNodeIndex] = childNode.GetStringProperty("m_ID");
            graphBuilder.Nodes.Add(childNode);
            WireNodeInputs(childNodeIndex, compiledNode, childNode, graphBuilder);
        }
        else
        {
            childNode = BuildFlowNode(childNodeIndex, graphBuilder);
        }
        var resultNode = CreateResultNode(resultClassName, $"virtual:{nodeIndex}/result", "Out", resultType);

        graphBuilder.Nodes.Add(resultNode);
        graphBuilder.Connect(childNode.GetStringProperty("m_ID"), GetOutputPinId(childNode, 0), resultNode.GetStringProperty("m_ID"), GetInputPinId(resultNode, 0));

        return graphBuilder.ToGraph();
    }

    private KVObject BuildStateMachineNode(int nodeIndex)
    {
        var node = CreateBaseNode("CNmGraphDocStateMachineNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node["m_pChildGraph"] = BuildStateMachineGraph(nodeIndex);
        return node;
    }

    private KVObject BuildStateMachineGraph(int nodeIndex)
    {
        var stateMachine = GetDefinition<StateMachineNode>(nodeIndex);
        var stateDefinitions = stateMachine.StateDefinitions;

        var graphNodeId = MakeGuid();
        var graphNode = KVObject.Collection();
        graphNode.Add("_class", "CNmGraphDocStateMachineGraph");
        graphNode.Add("m_ID", graphNodeId);

        var nodes = KVObject.Array();
        var stateNodeIds = new Dictionary<int, string>();
        var stateNodes = new Dictionary<int, KVObject>();

        for (var stateIndex = 0; stateIndex < stateDefinitions.Length; stateIndex++)
        {
            var stateNodeIndex = stateDefinitions[stateIndex].StateNodeIdx;
            var stateNode = BuildStateNode(stateNodeIndex);
            var stateNodeId = stateNode.GetStringProperty("m_ID");
            stateNodeIds[stateIndex] = stateNodeId;
            stateNodes[stateNodeIndex] = stateNode;
            nodes.Add(stateNode);
        }

        nodes.Add(BuildEntryOverrideConduit(nodeIndex, stateDefinitions, stateNodes));

        var transitionInfos = EnumerateTransitionInfos(stateDefinitions).ToArray();
        var globalTransitions = transitionInfos.Where(info => info.GroupKind == StateMachineTransitionGroup.Global).ToArray();
        nodes.Add(BuildGlobalTransitionConduit(nodeIndex, stateDefinitions, globalTransitions, stateNodes));

        foreach (var conduitGroup in transitionInfos.Where(info => info.GroupKind == StateMachineTransitionGroup.Standard).GroupBy(info => (info.SourceStateNodeIndex, info.TargetStateNodeIndex)))
        {
            nodes.Add(BuildTransitionConduit(nodeIndex, conduitGroup.ToArray(), stateNodes));
        }

        graphNode.Add("m_nodes", nodes);
        graphNode.Add("m_graphType", "StateMachine");
        graphNode.Add("m_viewOffset", MakeVector2(0.0f, 0.0f));
        graphNode.Add("m_flViewZoom", 1.0f);
        graphNode.Add("m_entryStateID", stateNodeIds.GetValueOrDefault(stateMachine.DefaultStateIndex, Guid.Empty.ToString()));
        return graphNode;
    }

    private KVObject BuildStateNode(int nodeIndex)
    {
        var state = GetDefinition<StateNode>(nodeIndex);

        var node = CreateBaseNode("CNmGraphDocStateNode", MakeGuid(), GetNodeName(nodeIndex));
        var childNodeIndex = state.ChildNodeIdx;
        var isStateMachineState = GetDefinition(childNodeIndex) is StateMachineNode;

        var childGraph = GetCompiledNode(childNodeIndex) switch
        {
            not null when isStateMachineState => BuildNestedStateMachineChildGraph(childNodeIndex),
            not null => BuildBlendTreeGraph($"state:{nodeIndex}", childNodeIndex),
            _ => null,
        };

        if (childGraph is not null)
        {
            node["m_pChildGraph"] = childGraph;
        }

        node["m_pSecondaryGraph"] = BuildStateLayerGraph(nodeIndex, state);
        node.Add("m_type", state.IsOffState
            ? "OffState"
            : isStateMachineState ? "StateMachineState" : "BlendTreeState");
        node.Add("m_cloneSourceStateID", Guid.Empty.ToString());
        node.Add("m_stateEvents", ConvertStateEvents(state));
        var timedStateEvents = KVObject.Array();
        AddTimedStateEvents(timedStateEvents, "TimeRemaining", state.TimedRemainingEvents);
        AddTimedStateEvents(timedStateEvents, "TimeElapsed", state.TimedElapsedEvents);

        node.Add("m_timedStateEvents", timedStateEvents);
        node.Add("m_events", KVObject.Array());
        node.Add("m_entryEvents", KVObject.Array());
        node.Add("m_executeEvents", KVObject.Array());
        node.Add("m_exitEvents", KVObject.Array());
        node.Add("m_timeRemainingEvents", KVObject.Array());
        node.Add("m_timeElapsedEvents", KVObject.Array());
        node.Add("m_bUseActualElapsedTimeInStateForTimedEvents", state.UseActualElapsedTimeInStateForTimedEvents);
        return node;
    }

    private KVObject BuildNestedStateMachineChildGraph(int childStateMachineNodeIndex)
    {
        var graphBuilder = new FlowGraphBuilder($"nested-sm:{childStateMachineNodeIndex}", "BlendTree");
        var childNode = BuildStateMachineNode(childStateMachineNodeIndex);
        var resultNode = CreatePoseResultNode($"nested-sm:{childStateMachineNodeIndex}/result");

        graphBuilder.Nodes.Add(childNode);
        graphBuilder.Nodes.Add(resultNode);
        graphBuilder.Connect(childNode.GetStringProperty("m_ID"), GetOutputPinId(childNode, 0), resultNode.GetStringProperty("m_ID"), GetInputPinId(resultNode, 0));
        return graphBuilder.ToGraph();
    }

    private KVObject BuildStateLayerGraph(int stateNodeIndex, StateNode state)
    {
        var graphBuilder = new FlowGraphBuilder($"state-layer:{stateNodeIndex}", "ValueTree");
        var resultNode = CreateStateLayerResultNode($"state-layer:{stateNodeIndex}/result");
        graphBuilder.Nodes.Add(resultNode);

        short[] layerInputs = [state.LayerWeightNodeIdx, state.LayerRootMotionWeightNodeIdx, state.LayerBoneMaskNodeIdx];

        for (var inputIndex = 0; inputIndex < layerInputs.Length; inputIndex++)
        {
            if (layerInputs[inputIndex] is var inputNodeIndex && inputNodeIndex >= 0)
            {
                var sourceNode = BuildFlowNode(inputNodeIndex, graphBuilder);
                graphBuilder.Connect(sourceNode.GetStringProperty("m_ID"), GetOutputPinId(sourceNode, 0), resultNode.GetStringProperty("m_ID"), GetInputPinId(resultNode, inputIndex));
            }
        }

        return graphBuilder.ToGraph();
    }

    private KVObject BuildEntryOverrideConduit(int stateMachineNodeIndex, StateMachineNode.StateDefinition[] stateDefinitions, IReadOnlyDictionary<int, KVObject> stateNodes)
    {
        var conduitNode = CreateBaseNode("CNmGraphDocEntryStateOverrideConduitNode", MakeGuid(), string.Empty);
        conduitNode["m_pSecondaryGraph"] = BuildEntryOverrideGraph(stateMachineNodeIndex, stateDefinitions, stateNodes);
        return conduitNode;
    }

    private KVObject BuildEntryOverrideGraph(int stateMachineNodeIndex, StateMachineNode.StateDefinition[] stateDefinitions, IReadOnlyDictionary<int, KVObject> stateNodes)
    {
        var graphBuilder = new FlowGraphBuilder($"entry-override:{stateMachineNodeIndex}", "EntryOverrideTree");
        var resultNode = CreateEntryOverrideConditionsNode(stateMachineNodeIndex);
        graphBuilder.Nodes.Add(resultNode);

        var pinToStateMapping = KVObject.Array();
        foreach (var stateDefinition in stateDefinitions)
        {
            var stateNodeIndex = stateDefinition.StateNodeIdx;
            var stateName = stateNodes[stateNodeIndex].GetStringProperty("m_name");
            var stateNodeId = stateNodes[stateNodeIndex].GetStringProperty("m_ID");

            var pin = CreatePin(stateName, "Bool", isDynamicPin: true);
            var resultPins = resultNode["m_inputPins"]!;
            resultPins.Add(pin);
            pinToStateMapping.Add(stateNodeId);

            var entryConditionNodeIdx = stateDefinition.EntryConditionNodeIdx;
            if (entryConditionNodeIdx < 0)
            {
                continue;
            }

            var conditionNode = BuildFlowNode(entryConditionNodeIdx, graphBuilder);
            graphBuilder.Connect(conditionNode.GetStringProperty("m_ID"), GetOutputPinId(conditionNode, 0), resultNode.GetStringProperty("m_ID"), pin.GetStringProperty("m_ID"));
        }

        resultNode["m_pinToStateMapping"] = pinToStateMapping;
        return graphBuilder.ToGraph();
    }

    private KVObject BuildGlobalTransitionConduit(int stateMachineNodeIndex, StateMachineNode.StateDefinition[] stateDefinitions, IReadOnlyList<TransitionInfo> transitions, Dictionary<int, KVObject> stateNodes)
    {
        var conduitNode = CreateBaseNode("CNmGraphDocGlobalTransitionConduitNode", MakeGuid(), string.Empty);
        var graphBuilder = new FlowGraphBuilder($"global-conduit:{stateMachineNodeIndex}", "GlobalTransitionConduit");

        var transitionsByTargetState = transitions
            .GroupBy(transition => transition.TargetStateNodeIndex)
            .ToDictionary(group => group.Key, group => group.First());

        var orderedStateDefinitions = GetOrderedGlobalTransitionStateDefinitions(stateDefinitions, transitions);

        foreach (var stateDefinition in orderedStateDefinitions)
        {
            var targetStateNodeIndex = stateDefinition.StateNodeIdx;
            var stateId = stateNodes[targetStateNodeIndex].GetStringProperty("m_ID");
            var transition = transitionsByTargetState.GetValueOrDefault(targetStateNodeIndex);

            var transitionNode = transition is { }
                ? CreateTransitionResultNode("CNmGraphDocGlobalTransitionNode", transition.TransitionNodeIndex, transition.CanBeForced, stateId)
                : CreateDefaultGlobalTransitionNode(targetStateNodeIndex, stateId);
            graphBuilder.Nodes.Add(transitionNode);

            if (transition is null)
            {
                continue;
            }

            foreach (var (sourceIndex, inputIndex) in EnumerateTransitionInputs(transition.TransitionNode, transition.ConditionNodeIndex))
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

    private static StateMachineNode.StateDefinition[] GetOrderedGlobalTransitionStateDefinitions(StateMachineNode.StateDefinition[] stateDefinitions, IReadOnlyList<TransitionInfo> transitions)
    {
        if (transitions.Count <= 1)
        {
            return stateDefinitions;
        }

        var stateDefinitionsByNodeIndex = stateDefinitions.ToDictionary(definition => (int)definition.StateNodeIdx);
        var stateOrder = stateDefinitions
            .Select((definition, index) => new { NodeIndex = (int)definition.StateNodeIdx, index })
            .ToDictionary(entry => entry.NodeIndex, entry => entry.index);
        var activeTargets = transitions
            .Select(transition => transition.TargetStateNodeIndex)
            .Distinct()
            .ToHashSet();

        var incomingEdges = activeTargets.ToDictionary(target => target, _ => 0);
        var outgoingEdges = activeTargets.ToDictionary(target => target, _ => new HashSet<int>());

        foreach (var sourceGroup in transitions.GroupBy(transition => transition.SourceStateNodeIndex))
        {
            var orderedTargets = sourceGroup
                .Select(transition => transition.TargetStateNodeIndex)
                .Distinct()
                .ToArray();

            for (var i = 0; i < orderedTargets.Length - 1; i++)
            {
                var fromTarget = orderedTargets[i];
                var toTarget = orderedTargets[i + 1];

                if (outgoingEdges[fromTarget].Add(toTarget))
                {
                    incomingEdges[toTarget]++;
                }
            }
        }

        var remainingTargets = activeTargets.ToList();
        remainingTargets.Sort((a, b) => stateOrder[a].CompareTo(stateOrder[b]));

        var sortedActiveTargets = new List<int>(activeTargets.Count);

        while (remainingTargets.Count > 0)
        {
            var nextTarget = remainingTargets.FirstOrDefault(target => incomingEdges[target] == 0);
            if (incomingEdges[nextTarget] != 0)
            {
                return stateDefinitions;
            }

            remainingTargets.Remove(nextTarget);
            sortedActiveTargets.Add(nextTarget);

            foreach (var target in outgoingEdges[nextTarget])
            {
                incomingEdges[target]--;
            }
        }

        var nextActiveTargetIndex = 0;
        var orderedStateDefinitions = new List<StateMachineNode.StateDefinition>(stateDefinitions.Length);

        foreach (var stateDefinition in stateDefinitions)
        {
            var targetStateNodeIndex = stateDefinition.StateNodeIdx;
            if (!activeTargets.Contains(targetStateNodeIndex))
            {
                orderedStateDefinitions.Add(stateDefinition);
                continue;
            }

            var orderedTargetStateNodeIndex = sortedActiveTargets[nextActiveTargetIndex++];
            orderedStateDefinitions.Add(stateDefinitionsByNodeIndex[orderedTargetStateNodeIndex]);
        }

        return [.. orderedStateDefinitions];
    }

    private KVObject BuildTransitionConduit(int stateMachineNodeIndex, IReadOnlyList<TransitionInfo> transitions, Dictionary<int, KVObject> stateNodes)
    {
        var firstTransition = transitions[0];
        var conduitKey = GetTransitionConduitKey(stateMachineNodeIndex, firstTransition);
        var conduitNode = CreateBaseNode("CNmGraphDocTransitionConduitNode", MakeGuid(), GetPathLeaf(firstTransition.GroupPath));
        conduitNode.Add("m_startStateID", stateNodes[firstTransition.SourceStateNodeIndex].GetStringProperty("m_ID"));
        conduitNode.Add("m_endStateID", stateNodes[firstTransition.TargetStateNodeIndex].GetStringProperty("m_ID"));

        var graphBuilder = new FlowGraphBuilder(conduitKey, "TransitionConduit");

        foreach (var transition in transitions)
        {
            var transitionNode = CreateTransitionResultNode("CNmGraphDocTransitionNode", transition.TransitionNodeIndex, transition.CanBeForced, null);
            graphBuilder.Nodes.Add(transitionNode);

            foreach (var (sourceIndex, inputIndex) in EnumerateTransitionInputs(transition.TransitionNode, transition.ConditionNodeIndex))
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

    private static string GetTransitionConduitKey(int stateMachineNodeIndex, TransitionInfo transition)
        => $"transition-conduit:{stateMachineNodeIndex}:{transition.SourceStateNodeIndex}->{transition.TargetStateNodeIndex}:{transition.GroupPath}";

    private static IEnumerable<(int SourceIndex, int InputIndex)> EnumerateTransitionInputs(TransitionNode transition, int conditionNodeIndex)
    {
        yield return (conditionNodeIndex, 0);
        yield return (transition.DurationOverrideNodeIdx, 1);
        yield return (transition.TimeOffsetOverrideNodeIdx, 2);
        yield return (transition.StartBoneMaskNodeIdx, 3);
        yield return (transition.TargetSyncIDNodeIdx, 4);
    }

    private IEnumerable<TransitionInfo> EnumerateTransitionInfos(StateMachineNode.StateDefinition[] stateDefinitions)
    {
        var stateNodeIndices = stateDefinitions
            .Select(stateDefinition => (int)stateDefinition.StateNodeIdx)
            .ToArray();

        var rawTransitions = stateDefinitions
            .SelectMany(stateDefinition =>
            {
                var sourceStateNodeIndex = (int)stateDefinition.StateNodeIdx;

                return stateDefinition.TransitionDefinitions.Select(transition =>
                {
                    var conditionNodeIndex = (int)transition.ConditionNodeIdx;
                    var transitionNodeIndex = (int)transition.TransitionNodeIdx;
                    var targetStateIndex = (int)transition.TargetStateIdx;
                    var conditionPath = conditionNodeIndex >= 0 && conditionNodeIndex < _nodePaths.Length ? _nodePaths[conditionNodeIndex] : string.Empty;
                    var transitionPath = transitionNodeIndex >= 0 && transitionNodeIndex < _nodePaths.Length ? _nodePaths[transitionNodeIndex] : string.Empty;

                    return new
                    {
                        SourceStateNodeIndex = sourceStateNodeIndex,
                        TargetStateIndex = targetStateIndex,
                        TargetStateNodeIndex = (int)stateDefinitions[targetStateIndex].StateNodeIdx,
                        ConditionNodeIndex = conditionNodeIndex,
                        TransitionNodeIndex = transitionNodeIndex,
                        GroupPath = GetTransitionGroupPath(conditionPath, transitionPath, sourceStateNodeIndex, targetStateIndex),
                        StateMachineTransition = transition,
                    };
                });
            })
            .ToArray();

        var explicitTargetPairs = rawTransitions
            .Where(transition => !transition.GroupPath.Contains("Global Transitions", StringComparison.Ordinal))
            .Select(transition => (transition.SourceStateNodeIndex, transition.TargetStateNodeIndex))
            .ToHashSet();

        foreach (var transition in rawTransitions)
        {
            var isGlobalTransition = transition.GroupPath.Contains("Global Transitions", StringComparison.Ordinal)
                && stateNodeIndices
                    .Where(stateNodeIndex => stateNodeIndex != transition.TargetStateNodeIndex)
                    .All(stateNodeIndex =>
                        rawTransitions.Any(rawTransition =>
                            rawTransition.SourceStateNodeIndex == stateNodeIndex
                            && rawTransition.TargetStateNodeIndex == transition.TargetStateNodeIndex
                            && rawTransition.GroupPath.Contains("Global Transitions", StringComparison.Ordinal))
                        || explicitTargetPairs.Contains((stateNodeIndex, transition.TargetStateNodeIndex)));

            var groupKind = isGlobalTransition
                ? StateMachineTransitionGroup.Global
                : StateMachineTransitionGroup.Standard;

            yield return new TransitionInfo
            {
                GroupKind = groupKind,
                GroupPath = transition.GroupPath,
                SourceStateNodeIndex = transition.SourceStateNodeIndex,
                TargetStateIndex = transition.TargetStateIndex,
                TargetStateNodeIndex = transition.TargetStateNodeIndex,
                ConditionNodeIndex = transition.ConditionNodeIndex,
                TransitionNodeIndex = transition.TransitionNodeIndex,
                TransitionNode = GetDefinition<TransitionNode>(transition.TransitionNodeIndex),
                CanBeForced = transition.StateMachineTransition.CanBeForced,
            };
        }
    }

    private static string GetTransitionGroupPath(string conditionPath, string transitionPath, int sourceStateNodeIndex, int targetStateIndex)
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
        var graphBuilder = new FlowGraphBuilder(graphKey, "BlendTree");
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
            && _persistentNodeIndices.Contains(nodeIndex)
            && (IsControlParameterNode(compiledNode) || IsVirtualParameterNode(nodeIndex)))
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
        if (TryCreateFlowNodeFromSpec(compiledClass.Stem, nodeIndex, out var specNode))
        {
            return specNode;
        }

        return compiledClass.Stem switch
        {
            "StateMachine" => BuildStateMachineNode(nodeIndex),
            "ReferencedGraph" => CreateReferencedGraphNode(nodeIndex, GetDefinition<ReferencedGraphNode>(nodeIndex)),
            "Selector" when GetDefinition<SelectorNode>(nodeIndex) is var selector
                => CreateSelectorNode("CNmGraphDocSelectorNode", nodeIndex, selector.OptionNodeIndices, selector.ConditionNodeIndices),
            "ClipSelector" or "AnimationClipSelector" when GetDefinition<ClipSelectorNode>(nodeIndex) is var selector
                => CreateSelectorNode("CNmGraphDocClipSelectorNode", nodeIndex, selector.OptionNodeIndices, selector.ConditionNodeIndices),
            "IDBasedSelector" when GetDefinition<IDBasedSelectorNode>(nodeIndex) is var selector
                => CreateIdBasedSelectorNode("CNmGraphDocIDBasedSelectorNode", nodeIndex, selector.OptionIDs, selector.IgnoreInvalidOptions),
            "IDBasedClipSelector" when GetDefinition<IDBasedClipSelectorNode>(nodeIndex) is var selector
                => CreateIdBasedSelectorNode("CNmGraphDocIDBasedClipSelectorNode", nodeIndex, selector.OptionIDs, selector.IgnoreInvalidOptions),
            "ParameterizedSelector" when GetDefinition<ParameterizedSelectorNode>(nodeIndex) is var selector
                => CreateParameterizedSelectorNode("CNmGraphDocParameterizedSelectorNode", "CNmGraphDocParameterizedSelectorNode::CData", nodeIndex, selector.OptionNodeIndices, selector.OptionWeights, selector.IgnoreInvalidOptions),
            "ParameterizedClipSelector" or "ParameterizedAnimationClipSelector" when GetDefinition<ParameterizedClipSelectorNode>(nodeIndex) is var selector
                => CreateParameterizedSelectorNode("CNmGraphDocParameterizedClipSelectorNode", "CNmGraphDocParameterizedClipSelectorNode::CData", nodeIndex, selector.OptionNodeIndices, selector.OptionWeights, selector.IgnoreInvalidOptions),
            "Clip" => CreateClipNode(nodeIndex, GetDefinition<ClipNode>(nodeIndex)),
            "TimeControlledClip" => CreateTimeControlledClipNode(nodeIndex, GetDefinition<TimeControlledClipNode>(nodeIndex)),
            "AnimationPose" => CreateAnimationPoseNode(nodeIndex, GetDefinition<AnimationPoseNode>(nodeIndex)),
            "FloatComparison" => CreateFloatComparisonNode(nodeIndex, GetDefinition<FloatComparisonNode>(nodeIndex)),
            "FloatRangeComparison" => CreateFloatRangeComparisonNode(nodeIndex, GetDefinition<FloatRangeComparisonNode>(nodeIndex)),
            "FloatRemap" => CreateFloatRemapNode(nodeIndex, GetDefinition<FloatRemapNode>(nodeIndex)),
            "FloatClamp" => CreateFloatClampNode(nodeIndex, GetDefinition<FloatClampNode>(nodeIndex)),
            "FloatEase" => CreateFloatEaseNode(nodeIndex, GetDefinition<FloatEaseNode>(nodeIndex)),
            "FloatSpring" => CreateFloatSpringNode(nodeIndex, GetDefinition<FloatSpringNode>(nodeIndex)),
            "FloatCurve" => CreateFloatCurveNode(nodeIndex, GetDefinition<FloatCurveNode>(nodeIndex)),
            "FloatMath" => CreateFloatMathNode(nodeIndex, GetDefinition<FloatMathNode>(nodeIndex)),
            "FloatSwitch" => CreateFloatSwitchNode(nodeIndex, GetDefinition<FloatSwitchNode>(nodeIndex)),
            "FloatAngleMath" => CreateFloatAngleMathNode(nodeIndex, GetDefinition<FloatAngleMathNode>(nodeIndex)),
            "IDComparison" => CreateIDComparisonNode(nodeIndex, GetDefinition<IDComparisonNode>(nodeIndex)),
            "IDSwitch" => CreateIdSwitchNode(nodeIndex, GetDefinition<IDSwitchNode>(nodeIndex)),
            "IDToFloat" => CreateIdToFloatNode(nodeIndex, GetDefinition<IDToFloatNode>(nodeIndex)),
            "IDEventCondition" => CreateIDEventConditionNode(nodeIndex, GetDefinition<IDEventConditionNode>(nodeIndex)),
            "IDEvent" => CreateIdEventNode(nodeIndex, GetDefinition<IDEventNode>(nodeIndex)),
            "IDEventPercentageThrough" => CreateIdEventPercentageThroughNode(nodeIndex, GetDefinition<IDEventPercentageThroughNode>(nodeIndex)),
            "GraphEventCondition" => CreateGraphEventConditionNode(nodeIndex, GetDefinition<GraphEventConditionNode>(nodeIndex)),
            "FootEventCondition" => CreateFootEventConditionNode(nodeIndex, GetDefinition<FootEventConditionNode>(nodeIndex)),
            "FootstepEventID" => CreateFootstepEventIdNode(nodeIndex, GetDefinition<FootstepEventIDNode>(nodeIndex)),
            "FootstepEventPercentageThrough" => CreateFootstepEventPercentageThroughNode(nodeIndex, GetDefinition<FootstepEventPercentageThroughNode>(nodeIndex)),
            "FloatSelector" => CreateFloatSelectorNode(nodeIndex, GetDefinition<FloatSelectorNode>(nodeIndex)),
            "FloatCurveEvent" => CreateFloatCurveEventNode(nodeIndex, GetDefinition<FloatCurveEventNode>(nodeIndex)),
            "VectorInfo" => CreateVectorInfoNode(nodeIndex, GetDefinition<VectorInfoNode>(nodeIndex)),
            "TargetInfo" => CreateTargetInfoNode(nodeIndex, GetDefinition<TargetInfoNode>(nodeIndex)),
            "TargetPoint" => CreateTargetPointNode(nodeIndex, GetDefinition<TargetPointNode>(nodeIndex)),
            "TargetOffset" => CreateTargetOffsetNode(nodeIndex, GetDefinition<TargetOffsetNode>(nodeIndex)),
            "TargetSelector" => CreateTargetSelectorNode(nodeIndex, GetDefinition<TargetSelectorNode>(nodeIndex)),
            "BoneMask" => CreateBoneMaskNode(nodeIndex, GetDefinition<BoneMaskNode>(nodeIndex)),
            "FixedWeightBoneMask" => CreateFixedWeightBoneMaskNode(nodeIndex, GetDefinition<FixedWeightBoneMaskNode>(nodeIndex)),
            "BoneMaskSwitch" => CreateBoneMaskSwitchNode(nodeIndex, GetDefinition<BoneMaskSwitchNode>(nodeIndex)),
            "BoneMaskSelector" => CreateBoneMaskSelectorNode(nodeIndex, GetDefinition<BoneMaskSelectorNode>(nodeIndex)),
            "SpeedScale" => CreateScaleNode("CnmGraphDocSpeedScaleNode", nodeIndex, "Scale", "m_flMultiplier"),
            "DurationScale" => CreateScaleNode("CnmGraphDocDurationScaleNode", nodeIndex, "New Duration", "m_flDesiredDuration"),
            "VelocityBasedSpeedScale" => CreateScaleNode("CnmGraphDocVelocityBasedSpeedScaleNode", nodeIndex, "Desired Velocity", "m_flDesiredVelocity"),
            "VelocityBlend" => CreateVelocityBlendNode(nodeIndex, GetDefinition<VelocityBlendNode>(nodeIndex)),
            "Blend1D" => CreateBlend1DNode(nodeIndex, GetDefinition<Blend1DNode>(nodeIndex)),
            "Blend2D" => CreateBlend2DNode(nodeIndex, GetDefinition<Blend2DNode>(nodeIndex)),
            "SyncEventIndexCondition" => CreateSyncEventIndexConditionNode(nodeIndex, GetDefinition<SyncEventIndexConditionNode>(nodeIndex)),
            "TimeCondition" => CreateTimeConditionNode(nodeIndex, GetDefinition<TimeConditionNode>(nodeIndex)),
            "TransitionEventCondition" => CreateTransitionEventConditionNode(nodeIndex, GetDefinition<TransitionEventConditionNode>(nodeIndex)),
            "AimCS" => CreateAimCsNode(nodeIndex, compiledNode),
            "FollowBone" => CreateFollowBoneNode(nodeIndex, GetDefinition<FollowBoneNode>(nodeIndex)),
            "BodyGroup" => CreateBodyGroupNode(nodeIndex, GetDefinition<BodyGroupNode>(nodeIndex)),
            "FootIK" => CreateFootIkNode(nodeIndex, GetDefinition<FootIKNode>(nodeIndex)),
            "TwoBoneIK" => CreateTwoBoneIkNode(nodeIndex, GetDefinition<TwoBoneIKNode>(nodeIndex)),
            "ChainLookat" => CreateChainLookatNode(nodeIndex, GetDefinition<ChainLookatNode>(nodeIndex)),
            "OrientationWarp" => CreateOrientationWarpNode(nodeIndex, GetDefinition<OrientationWarpNode>(nodeIndex)),
            "TargetWarp" => CreateTargetWarpNode(nodeIndex, GetDefinition<TargetWarpNode>(nodeIndex)),
            "RootMotionOverride" => CreateRootMotionOverrideNode(nodeIndex, GetDefinition<RootMotionOverrideNode>(nodeIndex)),
            "SnapWeapon" => CreateSnapWeaponNode(nodeIndex),
            "LayerBlend" => CreateLayerBlendNode(nodeIndex, GetDefinition<LayerBlendNode>(nodeIndex)),
            "ExternalPose" => CreateExternalPoseNode(nodeIndex, GetDefinition<ExternalPoseNode>(nodeIndex)),
            "IsExternalPoseSet" => CreateIsExternalPoseSetNode(nodeIndex, GetDefinition<IsExternalPoseSetNode>(nodeIndex)),
            "IsExternalGraphSlotFilled" => CreateIsExternalGraphSlotFilledNode(nodeIndex, GetDefinition<IsExternalGraphSlotFilledNode>(nodeIndex)),
            "IDSelector" => CreateIdSelectorNode(nodeIndex, GetDefinition<IDSelectorNode>(nodeIndex)),
            "ConstVector" => CreateConstVectorNode(nodeIndex, GetDefinition<ConstVectorNode>(nodeIndex)),
            "ConstTarget" when GetDefinition<ConstTargetNode>(nodeIndex).Value is var target
                => target.IsBoneTarget || target.BoneID.Name.Length > 0
                    ? CreateConstBoneTargetNode(nodeIndex, target.BoneID.Name)
                    : CreateConstTargetNode(nodeIndex, compiledNode),
            // An older class that carries the bone name directly
            "ConstBoneTarget" => CreateConstBoneTargetNode(nodeIndex, GetOptionalString(compiledNode, "m_boneName")),
            "ConstFloat" => CreateConstFloatNode(nodeIndex, GetDefinition<ConstFloatNode>(nodeIndex)),
            "ConstBool" => CreateConstValueNode(GetConstDocNodeClassName("Bool"), nodeIndex, "Bool", "m_bValue", GetDefinition<ConstBoolNode>(nodeIndex).Value),
            "ConstID" => CreateConstValueNode(GetConstDocNodeClassName("ID"), nodeIndex, "ID", "m_value", GetDefinition<ConstIDNode>(nodeIndex).Value.Name),
            _ when compiledClass.TryGetTypedSuffix("ControlParameter", out _)
                => CreateParameterReferenceNode(nodeIndex, graphBuilder),
            _ => throw new InvalidDataException($"Unsupported NmGraph node: {compiledClass.Name}"),
        };
    }

    private void WireNodeInputs(int nodeIndex, KVObject compiledNode, KVObject node, FlowGraphBuilder graphBuilder)
    {
        var compiledClass = GetCompiledClass(compiledNode);
        if (TryWireFlowNodeFromSpec(compiledClass.Stem, nodeIndex, node, graphBuilder))
        {
            return;
        }

        void Connect(int sourceIndex, int inputIndex)
            => ConnectIfValid(sourceIndex, node, inputIndex, graphBuilder);

        void ConnectAll(short[] sourceIndices, int firstInputIndex)
        {
            for (var i = 0; i < sourceIndices.Length; i++)
            {
                Connect(sourceIndices[i], firstInputIndex + i);
            }
        }

        switch (GetDefinition(nodeIndex))
        {
            case ParameterizedSelectorNode selector:
                Connect(selector.ParameterNodeIdx, 0);
                ConnectAll(selector.OptionNodeIndices, 1);
                break;

            case ParameterizedClipSelectorNode selector:
                Connect(selector.ParameterNodeIdx, 0);
                ConnectAll(selector.OptionNodeIndices, 1);
                break;

            case SelectorNode selector:
                ConnectAll(selector.OptionNodeIndices, 0);
                break;

            case ClipSelectorNode selector:
                ConnectAll(selector.OptionNodeIndices, 0);
                break;

            case IDBasedSelectorNode selector:
                Connect(selector.ParameterNodeIdx, 0);
                Connect(selector.FallbackNodeIdx, 1);
                ConnectAll(selector.OptionNodeIndices, 2);
                break;

            case IDBasedClipSelectorNode selector:
                Connect(selector.ParameterNodeIdx, 0);
                Connect(selector.FallbackNodeIdx, 1);
                ConnectAll(selector.OptionNodeIndices, 2);
                break;

            case TargetSelectorNode selector:
                Connect(selector.ParameterNodeIdx, 0);
                ConnectAll(selector.OptionNodeIndices, 1);
                break;

            case ClipNode clip:
                Connect(clip.PlayInReverseValueNodeIdx, 0);
                Connect(clip.ResetTimeValueNodeIdx, 1);
                break;

            case TimeControlledClipNode clip:
                Connect(clip.TimeValueNodeIdx, 0);
                Connect(clip.PlayInReverseValueNodeIdx, 1);
                break;

            case AnimationPoseNode pose:
                Connect(pose.PoseTimeValueNodeIdx, 0);
                break;

            case ReferencedGraphNode referencedGraph:
                Connect(referencedGraph.FallbackNodeIdx, 0);
                break;

            case IDComparisonNode comparison:
                Connect(comparison.InputValueNodeIdx, 0);
                break;

            case FloatComparisonNode comparison:
                Connect(comparison.InputValueNodeIdx, 0);
                Connect(comparison.ComparandValueNodeIdx, 1);
                break;

            case FloatRangeComparisonNode comparison:
                Connect(comparison.InputValueNodeIdx, 0);
                break;

            case TimeConditionNode timeCondition:
                Connect(timeCondition.InputValueNodeIdx, 0);
                break;

            case FloatRemapNode remap:
                Connect(remap.InputValueNodeIdx, 0);
                break;

            case FloatClampNode clamp:
                Connect(clamp.InputValueNodeIdx, 0);
                break;

            case FloatEaseNode ease:
                Connect(ease.InputValueNodeIdx, 0);
                break;

            case FloatSpringNode spring:
                Connect(spring.InputValueNodeIdx, 0);
                break;

            case FloatCurveNode curve:
                Connect(curve.InputValueNodeIdx, 0);
                break;

            case FloatAngleMathNode angleMath:
                Connect(angleMath.InputValueNodeIdx, 0);
                break;

            case IDToFloatNode idToFloat:
                Connect(idToFloat.InputValueNodeIdx, 0);
                break;

            case FloatMathNode math:
                Connect(math.InputValueNodeIdxA, 0);
                Connect(math.InputValueNodeIdxB, 1);
                break;

            case FloatSwitchNode floatSwitch:
                Connect(floatSwitch.SwitchValueNodeIdx, 0);
                Connect(floatSwitch.TrueValueNodeIdx, 1);
                Connect(floatSwitch.FalseValueNodeIdx, 2);
                break;

            case IDSwitchNode idSwitch:
                Connect(idSwitch.SwitchValueNodeIdx, 0);
                Connect(idSwitch.TrueValueNodeIdx, 1);
                Connect(idSwitch.FalseValueNodeIdx, 2);
                break;

            case BoneMaskSwitchNode maskSwitch:
                Connect(maskSwitch.SwitchValueNodeIdx, 0);
                Connect(maskSwitch.TrueValueNodeIdx, 1);
                Connect(maskSwitch.FalseValueNodeIdx, 2);
                break;

            case VectorInfoNode vectorInfo:
                Connect(vectorInfo.InputValueNodeIdx, 0);
                break;

            case TargetInfoNode targetInfo:
                Connect(targetInfo.InputValueNodeIdx, 0);
                break;

            case TargetPointNode targetPoint:
                Connect(targetPoint.InputValueNodeIdx, 0);
                break;

            case TargetOffsetNode targetOffset:
                Connect(targetOffset.InputValueNodeIdx, 0);
                break;

            case SpeedScaleBaseNode speedScale:
                Connect(speedScale.ChildNodeIdx, 0);
                Connect(speedScale.InputValueNodeIdx, 1);
                break;

            case FloatSelectorNode selector:
                ConnectAll(selector.ConditionNodeIndices, 0);
                break;

            case IDSelectorNode selector:
                ConnectAll(selector.ConditionNodeIndices, 0);
                break;

            case FloatCurveEventNode curveEvent:
                Connect(curveEvent.DefaultNodeIdx, 0);
                break;

            case BoneMaskSelectorNode maskSelector:
                Connect(maskSelector.ParameterValueNodeIdx, 0);
                Connect(maskSelector.DefaultMaskNodeIdx, 1);
                ConnectAll(maskSelector.MaskNodeIndices, 2);
                break;

            case Blend2DNode blend:
                Connect(blend.InputParameterNodeIdx0, 0);
                Connect(blend.InputParameterNodeIdx1, 1);
                ConnectAll(blend.SourceNodeIndices, 2);
                break;

            case ParameterizedBlendNode blend:
                Connect(blend.InputParameterValueNodeIdx, 0);
                ConnectAll(blend.SourceNodeIndices, 1);
                break;

            case LayerBlendNode layerBlend:
            {
                Connect(layerBlend.BaseNodeIdx, 0);

                for (var i = 0; i < layerBlend.LayerDefinition.Length; i++)
                {
                    var layer = layerBlend.LayerDefinition[i];
                    var layerNode = CreateLayerBlendInputNode(nodeIndex, i, layer);
                    graphBuilder.Nodes.Add(layerNode);
                    graphBuilder.Connect(layerNode.GetStringProperty("m_ID"), GetOutputPinId(layerNode, 0), node.GetStringProperty("m_ID"), GetInputPinId(node, i + 1));

                    ConnectIfValid(layer.InputNodeIdx, layerNode, 0, graphBuilder);

                    if (layer.IsStateMachineLayer)
                    {
                        continue;
                    }

                    ConnectIfValid(layer.WeightValueNodeIdx, layerNode, 1, graphBuilder);
                    ConnectIfValid(layer.RootMotionWeightValueNodeIdx, layerNode, 2, graphBuilder);
                    ConnectIfValid(layer.BoneMaskValueNodeIdx, layerNode, 3, graphBuilder);
                }

                break;
            }

            case FollowBoneNode follow:
                Connect(follow.ChildNodeIdx, 0);
                Connect(follow.EnabledNodeIdx, 1);
                break;

            case BodyGroupNode bodyGroup:
                Connect(bodyGroup.ChildNodeIdx, 0);
                Connect(bodyGroup.EnabledNodeIdx, 1);
                break;

            case FootIKNode ik:
                Connect(ik.ChildNodeIdx, 0);
                Connect(ik.LeftTargetNodeIdx, 1);
                Connect(ik.RightTargetNodeIdx, 2);
                Connect(ik.EnabledNodeIdx, 3);
                break;

            case TwoBoneIKNode ik:
                Connect(ik.ChildNodeIdx, 0);
                Connect(ik.EffectorTargetNodeIdx, 1);
                Connect(ik.EnabledNodeIdx, 2);
                break;

            case ChainLookatNode lookat:
                Connect(lookat.ChildNodeIdx, 0);
                Connect(lookat.LookatTargetNodeIdx, 1);
                Connect(lookat.EnabledNodeIdx, 2);
                break;

            case OrientationWarpNode warp:
                Connect(warp.ClipReferenceNodeIdx, 0);
                Connect(warp.TargetValueNodeIdx, warp.IsOffsetNode ? 2 : 1);
                break;

            case TargetWarpNode warp:
                Connect(warp.ClipReferenceNodeIdx, 0);
                Connect(warp.TargetValueNodeIdx, 1);
                break;

            case RootMotionOverrideNode rootMotion:
                Connect(rootMotion.ChildNodeIdx, 0);
                Connect(rootMotion.DesiredMovingVelocityNodeIdx, 1);
                Connect(rootMotion.DesiredFacingDirectionNodeIdx, 2);
                Connect(rootMotion.LinearVelocityLimitNodeIdx, 3);
                Connect(rootMotion.AngularVelocityLimitNodeIdx, 4);
                break;

            default:
                WireClientNodeInputs(compiledClass.Stem, compiledNode, node, graphBuilder);
                break;
        }
    }

    // Client nodes are not part of the graph library, so they are read by key
    private void WireClientNodeInputs(string stem, KVObject compiledNode, KVObject node, FlowGraphBuilder graphBuilder)
    {
        string[] inputKeys = stem switch
        {
            "AimCS" =>
            [
                "m_nChildNodeIdx", "m_nHorizontalAngleNodeIdx", "m_nVerticalAngleNodeIdx", "m_nWeaponCategoryNodeIdx", "m_nWeaponTypeNodeIdx",
                "m_nWeaponActionNodeIdx", "m_nWeaponDropNodeIdx", "m_nCrouchWeightNodeIdx", "m_nIsDefusingNodeIdx",
            ],
            "SnapWeapon" => ["m_nChildNodeIdx", "m_nFlashedAmountNodeIdx", "m_nWeaponCategoryNodeIdx", "m_nWeaponTypeNodeIdx"],
            _ => [],
        };

        for (var i = 0; i < inputKeys.Length; i++)
        {
            ConnectIfValid((int)compiledNode.GetInt64Property(inputKeys[i], -1), node, i, graphBuilder);
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

        if (compiledClass.TryGetTypedSuffix("ControlParameter", out var valueType))
        {
        }
        else if (IsVirtualParameterNode(nodeIndex))
        {
            valueType = GetVirtualParameterValueType(nodeIndex);
        }
        else
        {
            throw new InvalidDataException($"Unsupported parameter reference node: {compiledClass.Name}");
        }

        var parameterNodeId = GetOrCreateRootParameterNodeId(nodeIndex);

        var node = CreateBaseNode(GetTypedDocNodeClassName(valueType, "ParameterReference"), MakeGuid(), IsVirtualParameterNode(nodeIndex) ? GetVirtualParameterName(nodeIndex) : GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Value", valueType, AllowMultipleOutConnections: true)]));
        node.Add("m_parameterUUID", parameterNodeId);
        node.Add("m_parameterValueType", valueType);
        node.Add("m_parameterName", IsVirtualParameterNode(nodeIndex) ? GetVirtualParameterName(nodeIndex) : GetNodeName(nodeIndex));
        node.Add("m_parameterGroupName", string.Empty);
        return node;
    }

    private KVObject CreateReferencedGraphNode(int nodeIndex, ReferencedGraphNode definition)
    {
        var variationData = CreateReferencedGraphVariationData(definition, GetReferencedGraphPath);

        var node = CreateBaseNode("CNmGraphDocReferencedGraphNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Fallback", "Pose")]));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", CreateVariationOverrides(nodeIndex, variationData, variationGraph
            => variationGraph.GetDefinition(nodeIndex) is ReferencedGraphNode variationNode ? CreateReferencedGraphVariationData(variationNode, variationGraph.GetReferencedGraphPath) : null));
        node.Add("m_defaultResourceName", string.Empty);
        return node;
    }

    private KVObject CreateSelectorNode(string className, int nodeIndex, short[] optionNodeIndices, short[] conditionNodeIndices)
    {
        var optionLabels = optionNodeIndices.Select(index => GetNodeName(index)).ToArray();

        var node = CreateBaseNode(className, MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins(optionLabels.Select(label => new PinDef(label, "Pose", IsDynamicPin: true))));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_optionLabels", CloneStringArray(optionLabels));
        node["m_pSecondaryGraph"] = BuildSelectorConditionGraph(nodeIndex, conditionNodeIndices, optionLabels);
        return node;
    }

    private KVObject CreateIdBasedSelectorNode(string className, int nodeIndex, StringToken[] optionSymbols, bool ignoreInvalidOptions)
    {
        var optionIds = GetNames(optionSymbols);

        var inputPins = new List<PinDef>
        {
            new("ID", "ID"),
            new("Optional Fallback", "Pose"),
        };
        inputPins.AddRange(optionIds.Select(value => new PinDef(string.IsNullOrEmpty(value) ? "Option" : value, "Pose", IsDynamicPin: true)));

        var node = CreateBaseNode(className, MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins(inputPins));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_optionLabels", CloneStringArray(optionIds));
        node.Add("m_bIgnoreInvalidOptions", ignoreInvalidOptions);
        return node;
    }

    private KVObject CreateParameterizedSelectorNode(string className, string dataClassName, int nodeIndex, short[] optionNodeIndices, byte[] optionWeights, bool ignoreInvalidOptions)
    {
        var optionLabels = optionNodeIndices.Select(index => GetNodeName(index)).ToArray();

        var variationData = CreateSelectorVariationData(dataClassName, optionWeights);

        var node = CreateBaseNode(className, MakeGuid(), GetNodeName(nodeIndex));
        var inputPins = new List<PinDef> { new("Parameter", "Float") };
        inputPins.AddRange(optionLabels.Select(label => new PinDef(label, "Pose", IsDynamicPin: true)));

        node.Add("m_inputPins", MakePins(inputPins));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", CreateVariationOverrides(nodeIndex, variationData, variationGraph => variationGraph.GetDefinition(nodeIndex) switch
        {
            ParameterizedSelectorNode variationNode when className == "CNmGraphDocParameterizedSelectorNode" => CreateSelectorVariationData(dataClassName, variationNode.OptionWeights),
            ParameterizedClipSelectorNode variationNode when className == "CNmGraphDocParameterizedClipSelectorNode" => CreateSelectorVariationData(dataClassName, variationNode.OptionWeights),
            _ => null,
        }));
        node.Add("m_defaultResourceName", string.Empty);
        node.Add("m_optionLabels", CloneStringArray(optionLabels));
        node.Add("m_bIgnoreInvalidOptions", ignoreInvalidOptions);
        return node;
    }

    private KVObject CreateClipNode(int nodeIndex, ClipNode definition)
    {
        var variationData = CreateClipVariationData(definition, GetResourcePath);

        var node = CreateBaseNode("CNmGraphDocClipNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Play In Reverse", "Bool"), new PinDef("Reset Time", "Bool")]));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", CreateVariationOverrides(nodeIndex, variationData, variationGraph
            => variationGraph.GetDefinition(nodeIndex) is ClipNode variationNode ? CreateClipVariationData(variationNode, variationGraph.GetResourcePath) : null));
        node.Add("m_defaultResourceName", string.Empty);
        node.Add("m_bSampleRootMotion", definition.SampleRootMotion);
        node.Add("m_bAllowLooping", definition.AllowLooping);
        node.Add("m_graphEvents", CloneStringArray(GetNames(definition.GraphEvents)));
        return node;
    }

    private KVObject CreateTimeControlledClipNode(int nodeIndex, TimeControlledClipNode definition)
    {
        var variationData = CreateTimeControlledClipVariationData(definition, GetResourcePath);

        var node = CreateBaseNode("CNmGraphDocTimeControlledClipNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Time", "Float"), new PinDef("Play In Reverse", "Bool")]));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", CreateVariationOverrides(nodeIndex, variationData, variationGraph
            => variationGraph.GetDefinition(nodeIndex) is TimeControlledClipNode variationNode ? CreateTimeControlledClipVariationData(variationNode, variationGraph.GetResourcePath) : null));
        node.Add("m_defaultResourceName", string.Empty);
        node.Add("m_bSampleRootMotion", definition.SampleRootMotion);
        node.Add("m_graphEvents", CloneStringArray(GetNames(definition.GraphEvents)));
        return node;
    }

    private KVObject CreateAnimationPoseNode(int nodeIndex, AnimationPoseNode definition)
    {
        var variationData = CreateAnimationPoseVariationData(definition, GetResourcePath);

        var node = CreateBaseNode("CNmGraphDocAnimationPoseNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Time", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", CreateVariationOverrides(nodeIndex, variationData, variationGraph
            => variationGraph.GetDefinition(nodeIndex) is AnimationPoseNode variationNode ? CreateAnimationPoseVariationData(variationNode, variationGraph.GetResourcePath) : null));
        node.Add("m_defaultResourceName", string.Empty);
        node.Add("m_inputTimeRemapRange", CloneRange(definition.InputTimeRemapRange));
        node.Add("m_fixedTimeValue", definition.UserSpecifiedTime);
        node.Add("m_useFramesAsInput", definition.UseFramesAsInput);
        return node;
    }

    private KVObject CreateIDComparisonNode(int nodeIndex, IDComparisonNode definition)
    {
        var variationData = CreateIDComparisonVariationData(definition);
        var overrides = CreateVariationOverrides(nodeIndex, variationData, variationGraph
            => variationGraph.GetDefinition(nodeIndex) is IDComparisonNode variationNode ? CreateIDComparisonVariationData(variationNode) : null);

        var hasOverrides = overrides.Count > 0;
        var node = CreateBaseNode(hasOverrides ? "CNmGraphDocVariationIDComparisonNode" : "CNmGraphDocIDComparisonNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("ID", "ID")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));
        if (hasOverrides)
        {
            node.Add("m_pDefaultVariationData", variationData);
            node.Add("m_overrides", overrides);
            node.Add("m_defaultResourceName", string.Empty);
            node.Add("m_comparison", definition.Comparison.ToString());
        }
        else
        {
            node.Add("m_comparison", definition.Comparison.ToString());
            node.Add("m_values", CloneStringArray(GetNames(definition.ComparisionIDs)));
        }
        return node;
    }

    private KVObject CreateIdToFloatNode(int nodeIndex, IDToFloatNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocIDToFloatNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("ID", "ID")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_defaultValue", definition.DefaultValue);

        var ids = GetNames(definition.IDs);
        var values = definition.Values;
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

    private KVObject CreateIdSwitchNode(int nodeIndex, IDSwitchNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocIDSwitchNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Bool", "Bool"),
            new PinDef("If True", "ID"),
            new PinDef("If False", "ID"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "ID", AllowMultipleOutConnections: true)]));
        node.Add("m_falseValue", definition.FalseValue.Name);
        node.Add("m_trueValue", definition.TrueValue.Name);
        return node;
    }

    private KVObject CreateFloatComparisonNode(int nodeIndex, FloatComparisonNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocFloatComparisonNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Float", "Float"), new PinDef("Comparand (Optional)", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));
        node.Add("m_comparison", definition.Comparison.ToString());
        node.Add("m_flComparisonValue", definition.ComparisonValue);
        node.Add("m_flEpsilon", definition.Epsilon);
        return node;
    }

    private KVObject CreateFloatRangeComparisonNode(int nodeIndex, FloatRangeComparisonNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocFloatRangeComparisonNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Float", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));
        node.Add("m_range", CloneRange(definition.Range));
        node.Add("m_isInclusiveCheck", definition.IsInclusiveCheck);
        return node;
    }

    private KVObject CreateFloatRemapNode(int nodeIndex, FloatRemapNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocFloatRemapNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Float", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_inputRange", CloneRemapRange(definition.InputRange));
        node.Add("m_outputRange", CloneRemapRange(definition.OutputRange));
        return node;
    }

    private KVObject CreateFloatClampNode(int nodeIndex, FloatClampNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocFloatClampNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Value", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_clampRange", CloneRange(definition.ClampRange));
        return node;
    }

    private KVObject CreateFloatEaseNode(int nodeIndex, FloatEaseNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocFloatEaseNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Value", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_easing", definition.EasingOp.ToString());
        node.Add("m_flEaseTime", definition.EaseTime);
        node.Add("m_bUseStartValue", definition.UseStartValue);
        node.Add("m_flStartValue", definition.StartValue);
        return node;
    }

    private KVObject CreateFloatSpringNode(int nodeIndex, FloatSpringNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocFloatSpringNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Value", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_flHertz", definition.Hertz);
        node.Add("m_flDampingRatio", definition.DampingRatio);
        node.Add("m_bUseStartValue", definition.UseStartValue);
        node.Add("m_flStartValue", definition.StartValue);
        return node;
    }

    private KVObject CreateFloatCurveNode(int nodeIndex, FloatCurveNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocFloatCurveNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Float", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        // The curve is copied as it is stored, the node class only holds it in evaluated form
        node.Add("m_curve", GetOptionalObject(GetCompiledNode(nodeIndex)!, "m_curve") ?? KVObject.Collection());
        return node;
    }

    private KVObject CreateFloatMathNode(int nodeIndex, FloatMathNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocFloatMathNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("A", "Float"), new PinDef("B (Optional)", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_bReturnAbsoluteResult", definition.ReturnAbsoluteResult);
        node.Add("m_bReturnNegatedResult", definition.ReturnNegatedResult);
        node.Add("m_operator", definition.Operator.ToString());
        node.Add("m_flValueB", definition.ValueB);
        return node;
    }

    private KVObject CreateFloatSwitchNode(int nodeIndex, FloatSwitchNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocFloatSwitchNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Bool", "Bool"),
            new PinDef("If True", "Float"),
            new PinDef("If False", "Float"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_flFalseValue", definition.FalseValue);
        node.Add("m_flTrueValue", definition.TrueValue);
        return node;
    }

    private KVObject CreateFloatAngleMathNode(int nodeIndex, FloatAngleMathNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocFloatAngleMathNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Angle (deg)", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_operation", definition.Operation.ToString());
        return node;
    }

    private KVObject CreateFloatSelectorNode(int nodeIndex, FloatSelectorNode definition)
    {
        var values = definition.Values;
        var inputPins = values.Select(value => new PinDef(string.Create(CultureInfo.InvariantCulture, $"Option ({value:0.00})"), "Bool", IsDynamicPin: true)).ToArray();

        var options = KVObject.Array();
        for (var i = 0; i < values.Length; i++)
        {
            var option = KVObject.Collection();
            option.Add("m_name", $"Option {i}");
            option.Add("m_flValue", values[i]);
            options.Add(option);
        }

        var node = CreateBaseNode("CNmGraphDocFloatSelectorNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins(inputPins));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_options", options);
        node.Add("m_flDefaultValue", definition.DefaultValue);
        node.Add("m_easing", definition.EasingOp.ToString());
        node.Add("m_easeTime", definition.EaseTime);
        return node;
    }

    private KVObject CreateFloatCurveEventNode(int nodeIndex, FloatCurveEventNode definition)
    {
        var eventConditionRules = GetEventConditionRules(definition.EventConditionRules);

        var node = CreateBaseNode("CNmGraphDocFloatCurveEventNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Default", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Value", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_matchID", definition.EventID.Name);
        node.Add("m_flDefaultValue", definition.DefaultValue);
        node.Add("m_priorityRule", eventConditionRules.PriorityRule);
        node.Add("m_bLimitSearchToSourceState", eventConditionRules.LimitSearchToSourceState);
        node.Add("m_bIgnoreInactiveBranchEvents", eventConditionRules.IgnoreInactiveBranchEvents);
        return node;
    }

    private KVObject CreateIDEventConditionNode(int nodeIndex, IDEventConditionNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocIDEventConditionNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));

        var eventConditionRules = GetEventConditionRules(definition.EventConditionRules);
        node.Add("m_operator", eventConditionRules.Operator);
        node.Add("m_searchRule", eventConditionRules.SearchRule);
        node.Add("m_bLimitSearchToSourceState", eventConditionRules.LimitSearchToSourceState);
        node.Add("m_bIgnoreInactiveBranchEvents", eventConditionRules.IgnoreInactiveBranchEvents);
        node.Add("m_eventIDs", CloneStringArray(GetNames(definition.EventIDs)));
        return node;
    }

    private KVObject CreateIdEventNode(int nodeIndex, IDEventNode definition)
    {
        var eventConditionRules = GetEventConditionRules(definition.EventConditionRules);

        var node = CreateBaseNode("CNmGraphDocIDEventNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "ID", AllowMultipleOutConnections: true)]));
        node.Add("m_defaultValue", definition.DefaultValue.Name);
        node.Add("m_bLimitSearchToSourceState", eventConditionRules.LimitSearchToSourceState);
        node.Add("m_priorityRule", eventConditionRules.PriorityRule);
        node.Add("m_bIgnoreInactiveBranchEvents", eventConditionRules.IgnoreInactiveBranchEvents);
        return node;
    }

    private KVObject CreateIdEventPercentageThroughNode(int nodeIndex, IDEventPercentageThroughNode definition)
    {
        var eventConditionRules = GetEventConditionRules(definition.EventConditionRules);

        var node = CreateBaseNode("CNmGraphDocIDEventPercentageThroughNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_priorityRule", eventConditionRules.PriorityRule);
        node.Add("m_bLimitSearchToSourceState", eventConditionRules.LimitSearchToSourceState);
        node.Add("m_bIgnoreInactiveBranchEvents", eventConditionRules.IgnoreInactiveBranchEvents);
        node.Add("m_eventID", definition.EventID.Name);
        return node;
    }

    private KVObject CreateGraphEventConditionNode(int nodeIndex, GraphEventConditionNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocGraphEventConditionNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));

        var eventConditionRules = GetEventConditionRules(definition.EventConditionRules);
        node.Add("m_operator", eventConditionRules.Operator);
        node.Add("m_bLimitSearchToSourceState", eventConditionRules.LimitSearchToSourceState);
        node.Add("m_bIgnoreInactiveBranchEvents", eventConditionRules.IgnoreInactiveBranchEvents);

        var conditions = KVObject.Array();
        foreach (var condition in definition.Conditions)
        {
            var output = KVObject.Collection();
            output.Add("m_eventID", condition.EventID.Name);
            output.Add("m_type", condition.EventTypeCondition.ToString());
            conditions.Add(output);
        }

        node.Add("m_conditions", conditions);
        return node;
    }

    private KVObject CreateFootEventConditionNode(int nodeIndex, FootEventConditionNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocFootEventConditionNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));

        var eventConditionRules = GetEventConditionRules(definition.EventConditionRules);
        node.Add("m_phaseCondition", definition.PhaseCondition.ToString());
        node.Add("m_bLimitSearchToSourceState", eventConditionRules.LimitSearchToSourceState);
        node.Add("m_bIgnoreInactiveBranchEvents", eventConditionRules.IgnoreInactiveBranchEvents);
        return node;
    }

    private KVObject CreateFootstepEventIdNode(int nodeIndex, FootstepEventIDNode definition)
    {
        var eventConditionRules = GetEventConditionRules(definition.EventConditionRules);

        var node = CreateBaseNode("CNmGraphDocFootstepEventIDNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("ID", "ID", AllowMultipleOutConnections: true)]));
        node.Add("m_priorityRule", eventConditionRules.PriorityRule);
        node.Add("m_bLimitSearchToSourceState", eventConditionRules.LimitSearchToSourceState);
        node.Add("m_bIgnoreInactiveBranchEvents", eventConditionRules.IgnoreInactiveBranchEvents);
        return node;
    }

    private KVObject CreateFootstepEventPercentageThroughNode(int nodeIndex, FootstepEventPercentageThroughNode definition)
    {
        var eventConditionRules = GetEventConditionRules(definition.EventConditionRules);

        var node = CreateBaseNode("CNmGraphDocFootstepEventPercentageThroughNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_phaseCondition", definition.PhaseCondition.ToString());
        node.Add("m_priorityRule", eventConditionRules.PriorityRule);
        node.Add("m_bLimitSearchToSourceState", eventConditionRules.LimitSearchToSourceState);
        node.Add("m_bIgnoreInactiveBranchEvents", eventConditionRules.IgnoreInactiveBranchEvents);
        return node;
    }

    private KVObject CreateVectorInfoNode(int nodeIndex, VectorInfoNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocVectorInfoNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Vector", "Vector")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_desiredInfo", definition.DesiredInfo.ToString());
        return node;
    }

    private KVObject CreateTargetInfoNode(int nodeIndex, TargetInfoNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocTargetInfoNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Target", "Target")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_infoType", definition.InfoType.ToString());
        node.Add("m_bIsWorldSpaceTarget", definition.IsWorldSpaceTarget);
        return node;
    }

    private KVObject CreateTargetPointNode(int nodeIndex, TargetPointNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocTargetPointNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Target", "Target")]));
        node.Add("m_outputPins", MakePins([new PinDef("Point", "Vector", AllowMultipleOutConnections: true)]));
        node.Add("m_bIsWorldSpaceTarget", definition.IsWorldSpaceTarget);
        return node;
    }

    private KVObject CreateTargetOffsetNode(int nodeIndex, TargetOffsetNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocTargetOffsetNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Target", "Target")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Target", AllowMultipleOutConnections: true)]));
        node.Add("m_bIsBoneSpaceOffset", definition.IsBoneSpaceOffset);
        node.Add("m_rotationOffset", CloneVector3(new Vector3(definition.RotationOffset.X, definition.RotationOffset.Y, definition.RotationOffset.Z)));
        node.Add("m_translationOffset", CloneVector3(definition.TranslationOffset));
        return node;
    }

    private KVObject CreateTargetSelectorNode(int nodeIndex, TargetSelectorNode definition)
    {
        var optionNodeIndices = definition.OptionNodeIndices.Select(static value => (int)value).ToArray();
        var optionLabels = optionNodeIndices.Select(GetNodeName).ToArray();

        var inputPins = new List<PinDef> { new("Target", "Target") };
        inputPins.AddRange(optionLabels.Select(label => new PinDef(label, "Pose", IsDynamicPin: true)));

        var variationData = CreateTargetSelectorVariationData(definition);

        var node = CreateBaseNode("CNmGraphDocTargetSelectorNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins(inputPins));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", CreateVariationOverrides(nodeIndex, variationData, variationGraph
            => variationGraph.GetDefinition(nodeIndex) is TargetSelectorNode variationNode ? CreateTargetSelectorVariationData(variationNode) : null));
        node.Add("m_defaultResourceName", string.Empty);
        node.Add("m_optionLabels", CloneStringArray(optionLabels));
        node.Add("m_flOrientationScoreWeight", definition.OrientationScoreWeight);
        node.Add("m_flPositionScoreWeight", definition.PositionScoreWeight);
        node.Add("m_bIsWorldSpaceTarget", definition.IsWorldSpaceTarget);
        node.Add("m_bIgnoreInvalidOptions", definition.IgnoreInvalidOptions);
        return node;
    }

    private KVObject CreateBoneMaskNode(int nodeIndex, BoneMaskNode definition)
    {
        var maskId = definition.BoneMaskID.Name;
        var variationData = CreateBoneMaskVariationData(string.Empty);

        var node = CreateBaseNode("CNmGraphDocBoneMaskNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Bone Mask", "BoneMask", AllowMultipleOutConnections: true)]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", CreateVariationOverrides(nodeIndex, variationData, variationGraph =>
        {
            if (variationGraph.GetDefinition(nodeIndex) is not BoneMaskNode variationNode)
            {
                return null;
            }

            var variationMaskId = variationNode.BoneMaskID.Name;
            return CreateBoneMaskVariationData(variationMaskId.Equals(maskId, StringComparison.Ordinal) ? string.Empty : variationMaskId);
        }));
        node.Add("m_defaultResourceName", string.Empty);
        node.Add("m_maskID", maskId);
        return node;
    }

    private KVObject CreateFixedWeightBoneMaskNode(int nodeIndex, FixedWeightBoneMaskNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocFixedWeightBoneMaskNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Bone Mask", "BoneMask", AllowMultipleOutConnections: true)]));
        node.Add("m_flBoneWeight", definition.BoneWeight);
        return node;
    }

    private KVObject CreateBoneMaskSelectorNode(int nodeIndex, BoneMaskSelectorNode definition)
    {
        var parameterValues = GetNames(definition.ParameterValues);
        var inputPins = new List<PinDef>
        {
            new("ID", "ID"),
            new("Default Mask", "BoneMask"),
        };
        inputPins.AddRange(parameterValues.Select((value, index) => new PinDef(string.IsNullOrEmpty(value) ? $"Mask {index}" : value, "BoneMask")));

        var node = CreateBaseNode("CNmGraphDocBoneMaskSelectorNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins(inputPins));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "BoneMask", AllowMultipleOutConnections: true)]));
        node.Add("m_switchDynamically", definition.SwitchDynamically);
        node.Add("m_options", CloneStringArray(parameterValues));
        node.Add("m_flBlendTimeSeconds", definition.BlendTimeSeconds);
        return node;
    }

    private KVObject CreateBoneMaskSwitchNode(int nodeIndex, BoneMaskSwitchNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocBoneMaskSwitchNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Bool", "Bool"),
            new PinDef("If True", "BoneMask"),
            new PinDef("If False", "BoneMask"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "BoneMask", AllowMultipleOutConnections: true)]));
        node.Add("m_bSwitchDynamically", definition.SwitchDynamically);
        node.Add("m_flBlendTimeSeconds", definition.BlendTimeSeconds);
        return node;
    }

    private KVObject CreateScaleNode(string className, int nodeIndex, string valueInputName, string valueFieldName)
    {
        var node = CreateBaseNode(className, MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Input", "Pose"),
            new PinDef(valueInputName, "Float"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Pose", AllowMultipleOutConnections: true)]));
        node.Add(valueFieldName, GetDefinition<SpeedScaleBaseNode>(nodeIndex).DefaultInputValue);
        return node;
    }

    private KVObject CreateVelocityBlendNode(int nodeIndex, VelocityBlendNode definition)
    {
        var sourceNodeIndices = definition.SourceNodeIndices;

        var inputPins = new List<PinDef> { new("Parameter", "Float") };
        inputPins.AddRange(Enumerable.Range(0, sourceNodeIndices.Length)
            .Select(i => new PinDef(GetVelocityBlendInputLabel(sourceNodeIndices[i], i), "Pose")));

        var node = CreateBaseNode("CNmGraphDocVelocityBlendNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins(inputPins));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_bAllowLooping", definition.AllowLooping);
        return node;
    }

    private KVObject CreateBlend1DNode(int nodeIndex, Blend1DNode definition)
    {
        var sourceNodeIndices = definition.SourceNodeIndices.Select(static value => (int)value).ToArray();
        var pointValues = GetBlend1DPointValues(definition.Parameterization, sourceNodeIndices.Length);

        var inputPins = new List<PinDef> { new("Parameter", "Float") };
        var blendPoints = KVObject.Array();

        for (var i = 0; i < sourceNodeIndices.Length; i++)
        {
            var label = GetNodeName(sourceNodeIndices[i]);
            var value = i < pointValues.Length ? pointValues[i] : 0.0f;
            var pin = CreatePin(string.Create(CultureInfo.InvariantCulture, $"{label} ({value:0.##})"), "Pose", isDynamicPin: true);
            inputPins.Add(new PinDef(pin.GetStringProperty("m_name"), "Pose", IsDynamicPin: true));

            var point = KVObject.Collection();
            point.Add("m_name", label);
            point.Add("m_flValue", value);
            point.Add("m_pinID", pin.GetStringProperty("m_ID"));
            blendPoints.Add(point);
        }

        var node = CreateBaseNode("CNmGraphDocBlend1DNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePinsWithOverrides(inputPins, blendPoints));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));

        var blendSpace = KVObject.Collection();
        blendSpace.Add("m_points", blendPoints);
        node.Add("m_blendSpace", blendSpace);
        node.Add("m_bAllowLooping", definition.AllowLooping);
        return node;
    }

    private KVObject CreateBlend2DNode(int nodeIndex, Blend2DNode definition)
    {
        var sourceNodeIndices = definition.SourceNodeIndices.Select(static value => (int)value).ToArray();

        var inputPins = new List<PinDef> { new("X", "Float"), new("Y", "Float") };
        for (var i = 0; i < sourceNodeIndices.Length; i++)
        {
            var label = GetNodeName(sourceNodeIndices[i]);
            var point = i < definition.Values.Length ? definition.Values[i] : Vector2.Zero;
            var x = point.X;
            var y = point.Y;
            inputPins.Add(new PinDef(string.Create(CultureInfo.InvariantCulture, $"{label} ({x}, {y})"), "Pose", IsDynamicPin: true));
        }

        var blendSpace = KVObject.Collection();
        blendSpace.Add("m_pointNames", CloneStringArray(sourceNodeIndices.Select(GetNodeName)));
        blendSpace.Add("m_points", CloneValueArray(definition.Values));
        blendSpace.Add("m_indices", CloneValueArray(definition.Indices));
        blendSpace.Add("m_hullIndices", CloneValueArray(definition.HullIndices));

        var node = CreateBaseNode("CNmGraphDocBlend2DNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins(inputPins));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_blendSpace", blendSpace);
        node.Add("m_bAllowLooping", definition.AllowLooping);
        return node;
    }

    private KVObject CreateSyncEventIndexConditionNode(int nodeIndex, SyncEventIndexConditionNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocSyncEventIndexConditionNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));
        node.Add("m_triggerMode", definition.TriggerMode.ToString());
        node.Add("m_nSyncEventIdx", definition.SyncEventIdx);
        return node;
    }

    private KVObject CreateTimeConditionNode(int nodeIndex, TimeConditionNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocTimeConditionNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([new PinDef("Time Value (optional)", "Float")]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));
        node.Add("m_flComparand", definition.Comparand);
        node.Add("m_type", definition.Type.ToString());
        node.Add("m_operator", definition.Operator.ToString());
        return node;
    }

    private KVObject CreateTransitionEventConditionNode(int nodeIndex, TransitionEventConditionNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocTransitionEventConditionNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));

        var eventConditionRules = GetEventConditionRules(definition.EventConditionRules);
        node.Add("m_ruleCondition", definition.RuleCondition.ToString());
        node.Add("m_bMatchOnlySpecificMarkerID", !string.IsNullOrEmpty(definition.RequireRuleID.Name));
        node.Add("m_markerIDToMatch", definition.RequireRuleID.Name);
        node.Add("m_bLimitSearchToSourceState", eventConditionRules.LimitSearchToSourceState);
        node.Add("m_bIgnoreInactiveBranchEvents", eventConditionRules.IgnoreInactiveBranchEvents);
        return node;
    }

    private KVObject CreateLayerBlendNode(int nodeIndex, LayerBlendNode definition)
    {
        var inputPins = new List<PinDef> { new("Base Node", "Pose") };
        inputPins.AddRange(Enumerable.Range(0, definition.LayerDefinition.Length).Select(i => new PinDef($"Layer {i}", "Special")));

        var node = CreateBaseNode("CNmGraphDocLayerBlendNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins(inputPins));
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_onlySampleBaseRootMotion", definition.OnlySampleBaseRootMotion);
        return node;
    }

    private static KVObject CreateLayerBlendInputNode(int parentNodeIndex, int layerIndex, LayerBlendNode.LayerDefinitionType layerDefinition)
    {
        var isStateMachineLayer = layerDefinition.IsStateMachineLayer;
        var node = CreateBaseNode(
            isStateMachineLayer ? "CNmGraphDocStateMachineLayerNode" : "CNmGraphDocLocalLayerNode",
            MakeGuid(),
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
        node.Add("m_isSynchronized", layerDefinition.IsSynchronized);
        node.Add("m_ignoreEvents", layerDefinition.IgnoreEvents);
        node.Add("m_blendMode", layerDefinition.BlendMode.ToString());
        return node;
    }

    private KVObject CreateAimCsNode(int nodeIndex, KVObject compiledNode)
    {
        var node = CreateBaseNode("CNmGraphDocAimCSNode", MakeGuid(), GetNodeName(nodeIndex));
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
        node.Add("m_flActionBlendTimeSeconds", compiledNode.GetRequiredFloatProperty("m_flActionBlendTimeSeconds"));
        node.Add("m_flHandIKBlendInTimeSeconds", compiledNode.GetRequiredFloatProperty("m_flHandIKBlendInTimeSeconds"));
        node.Add("m_flPlantingBlendTimeSeconds", compiledNode.GetRequiredFloatProperty("m_flPlantingBlendTimeSeconds"));
        return node;
    }

    private KVObject CreateFollowBoneNode(int nodeIndex, FollowBoneNode definition)
    {
        var variationData = CreateFollowBoneVariationData(definition);

        var node = CreateBaseNode("CnmGraphDocFollowBoneNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Input", "Pose"),
            new PinDef("Enabled", "Bool"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Pose", AllowMultipleOutConnections: true)]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", CreateVariationOverrides(nodeIndex, variationData, variationGraph
            => variationGraph.GetDefinition(nodeIndex) is FollowBoneNode variationNode ? CreateFollowBoneVariationData(variationNode) : null));
        node.Add("m_defaultResourceName", string.Empty);
        node.Add("m_mode", definition.Mode.ToString());
        return node;
    }

    private KVObject CreateBodyGroupNode(int nodeIndex, BodyGroupNode definition)
    {
        var variationData = CreateBodyGroupVariationData(definition);

        var node = CreateBaseNode("CNmGraphDocBodyGroupNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Input", "Pose"),
            new PinDef("Enabled", "Bool"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Pose", AllowMultipleOutConnections: true)]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", CreateVariationOverrides(nodeIndex, variationData, variationGraph
            => variationGraph.GetDefinition(nodeIndex) is BodyGroupNode variationNode ? CreateBodyGroupVariationData(variationNode) : null));
        node.Add("m_defaultResourceName", string.Empty);
        return node;
    }

    private KVObject CreateFootIkNode(int nodeIndex, FootIKNode definition)
    {
        var variationData = CreateFootIkVariationData(definition);

        var node = CreateBaseNode("CnmGraphDocFootIKNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Input", "Pose"),
            new PinDef("Left Foot Target", "Target"),
            new PinDef("Right Foot Target", "Target"),
            new PinDef("Enabled", "Bool"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Pose", AllowMultipleOutConnections: true)]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", CreateVariationOverrides(nodeIndex, variationData, variationGraph
            => variationGraph.GetDefinition(nodeIndex) is FootIKNode variationNode ? CreateFootIkVariationData(variationNode) : null));
        node.Add("m_defaultResourceName", string.Empty);
        node.Add("m_bIsTargetInWorldSpace", definition.IsTargetInWorldSpace);
        node.Add("m_blendMode", definition.BlendMode.ToString());
        return node;
    }

    private KVObject CreateTwoBoneIkNode(int nodeIndex, TwoBoneIKNode definition)
    {
        var variationData = CreateTwoBoneIkVariationData(definition);

        var node = CreateBaseNode("CnmGraphDocTwoBoneIKNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Input", "Pose"),
            new PinDef("Target", "Target"),
            new PinDef("Enabled", "Bool"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Pose", AllowMultipleOutConnections: true)]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", CreateVariationOverrides(nodeIndex, variationData, variationGraph
            => variationGraph.GetDefinition(nodeIndex) is TwoBoneIKNode variationNode ? CreateTwoBoneIkVariationData(variationNode) : null));
        node.Add("m_defaultResourceName", string.Empty);
        node.Add("m_bIsTargetInWorldSpace", definition.IsTargetInWorldSpace);
        node.Add("m_blendMode", definition.BlendMode.ToString());
        node.Add("m_flChainRotationWeight", definition.ChainRotationWeight);
        return node;
    }

    private KVObject CreateChainLookatNode(int nodeIndex, ChainLookatNode definition)
    {
        var variationData = CreateChainLookatVariationData(definition);

        var node = CreateBaseNode("CnmGraphDocChainLookatNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Input", "Pose"),
            new PinDef("Target", "Vector"),
            new PinDef("Enabled", "Bool"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Pose", AllowMultipleOutConnections: true)]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", CreateVariationOverrides(nodeIndex, variationData, variationGraph
            => variationGraph.GetDefinition(nodeIndex) is ChainLookatNode variationNode ? CreateChainLookatVariationData(variationNode) : null));
        node.Add("m_defaultResourceName", string.Empty);
        node.Add("m_bIsTargetInWorldSpace", definition.IsTargetInWorldSpace);
        return node;
    }

    private KVObject CreateOrientationWarpNode(int nodeIndex, OrientationWarpNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocOrientationWarpNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Input", "Pose"),
            new PinDef("Direction (Character)", "Vector"),
            new PinDef("Angle Offset (Deg)", "Float"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Pose", AllowMultipleOutConnections: true)]));
        node.Add("m_offsetType", definition.IsOffsetNode
            ? (definition.IsOffsetRelativeToCharacter ? "RelativeToCharacter" : "RelativeToOriginalRootMotion")
            : "RelativeToCharacter");
        node.Add("m_samplingMode", definition.SamplingMode.ToString());
        node.Add("m_alignmentMode", definition.AlignmentMode.ToString());
        node.Add("m_bWarpTranslation", definition.WarpTranslation);
        return node;
    }

    private KVObject CreateTargetWarpNode(int nodeIndex, TargetWarpNode definition)
    {
        var variationData = CreateTargetWarpVariationData(definition);

        var node = CreateBaseNode("CNmGraphDocTargetWarpNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Input", "Pose"),
            new PinDef("World Target", "Target"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Pose")]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", CreateVariationOverrides(nodeIndex, variationData, variationGraph
            => variationGraph.GetDefinition(nodeIndex) is TargetWarpNode variationNode ? CreateTargetWarpVariationData(variationNode) : null));
        node.Add("m_defaultResourceName", string.Empty);

        var targetUpdateRule = definition.TargetUpdateRule.ToString();
        node.Add("m_targetUpdateRule", targetUpdateRule);
        node.Add("m_bAllowTargetUpdate", !targetUpdateRule.Equals("None", StringComparison.Ordinal));
        node.Add("m_bAlignWithTargetAtLastWarpEvent", definition.AlignWithTargetAtLastWarpEvent);
        node.Add("m_samplingMode", definition.SamplingMode.ToString());
        node.Add("m_flSamplingPositionErrorThreshold", MathF.Sqrt(definition.SamplingPositionErrorThresholdSq));
        node.Add("m_flMaxTangentLength", definition.MaxTangentLength);
        node.Add("m_flLerpFallbackDistanceThreshold", definition.LerpFallbackDistanceThreshold);
        node.Add("m_flTargetUpdateDistanceThresholdDegrees", definition.TargetUpdateDistanceThreshold);
        node.Add("m_flTargetUpdateAngleThresholdDegrees", float.RadiansToDegrees(definition.TargetUpdateAngleThresholdRadians));
        return node;
    }

    private KVObject CreateRootMotionOverrideNode(int nodeIndex, RootMotionOverrideNode definition)
    {
        var overrideFlags = definition.OverrideFlags.Flags;

        var node = CreateBaseNode("CNmGraphDocRootMotionOverrideNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Input", "Pose"),
            new PinDef("Desired Moving Velocity (Character)", "Vector"),
            new PinDef("Desired Facing Direction (Character)", "Vector"),
            new PinDef("Linear Velocity Limit (Optional)", "Float"),
            new PinDef("Angular Velocity Limit (Optional)", "Float"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Pose", AllowMultipleOutConnections: true)]));
        node.Add("m_flMaxLinearVelocity", definition.MaxLinearVelocity);
        node.Add("m_flMaxAngularVelocityDegrees", float.RadiansToDegrees(definition.MaxAngularVelocityRadians));
        node.Add("m_bOverrideMoveDirX", (overrideFlags & (1u << 0)) != 0);
        node.Add("m_bOverrideMoveDirY", (overrideFlags & (1u << 1)) != 0);
        node.Add("m_bOverrideMoveDirZ", (overrideFlags & (1u << 2)) != 0);
        node.Add("m_bAllowPitchForFacing", (overrideFlags & (1u << 3)) != 0);
        node.Add("m_bListenForRootMotionEvents", (overrideFlags & (1u << 4)) != 0);
        return node;
    }

    private KVObject CreateExternalPoseNode(int nodeIndex, ExternalPoseNode definition)
    {
        var node = CreateBaseNode("CNmGraphDocExternalPoseNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Pose", "Pose")]));
        node.Add("m_bShouldSampleRootMotion", definition.ShouldSampleRootMotion);
        return node;
    }

    private KVObject CreateIsExternalPoseSetNode(int nodeIndex, IsExternalPoseSetNode definition)
    {
        var externalPoseNodeIndex = definition.ExternalPoseNodeIdx;

        var node = CreateBaseNode("CNmGraphDocIsExternalPoseSetNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));
        node.Add("m_slotID", GetExternalPoseSlotId(externalPoseNodeIndex));
        return node;
    }

    private KVObject CreateIsExternalGraphSlotFilledNode(int nodeIndex, IsExternalGraphSlotFilledNode definition)
    {
        var externalGraphNodeIndex = definition.ExternalGraphNodeIdx;

        var node = CreateBaseNode("CNmGraphDocIsExternalGraphSlotFilledNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Bool", AllowMultipleOutConnections: true)]));
        node.Add("m_slotID", GetExternalGraphSlotId(externalGraphNodeIndex));
        return node;
    }

    private KVObject CreateIdSelectorNode(int nodeIndex, IDSelectorNode definition)
    {
        var values = GetNames(definition.Values);
        var inputPins = values.Select(value => new PinDef(string.IsNullOrEmpty(value) ? "ID" : value, "Bool", IsDynamicPin: true)).ToArray();

        var node = CreateBaseNode("CNmGraphDocIDSelectorNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins(inputPins));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "ID", AllowMultipleOutConnections: true)]));
        node.Add("m_options", CloneStringArray(values));
        node.Add("m_defaultID", definition.DefaultValue.Name);
        return node;
    }

    private KVObject CreateConstVectorNode(int nodeIndex, ConstVectorNode definition)
    {
        var node = CreateBaseNode("CnmGraphDocConstVectorNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Value", "Vector", AllowMultipleOutConnections: true)]));
        node.Add("m_value", CloneVector3(definition.Value));
        return node;
    }

    private KVObject CreateConstTargetNode(int nodeIndex, KVObject compiledNode)
    {
        var targetValue = compiledNode.GetSubCollection("m_value");

        var node = CreateBaseNode("CnmGraphDocConstTargetNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Value", "Target", AllowMultipleOutConnections: true)]));
        node.Add("m_rotation", CloneVector3(targetValue?.GetArray<float>("m_rotation")));
        node.Add("m_translation", CloneVector3(targetValue?.GetArray<float>("m_translation")));
        return node;
    }

    private KVObject CreateConstBoneTargetNode(int nodeIndex, string boneName)
    {
        var node = CreateBaseNode("CnmGraphDocConstBoneTargetNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Value", "Target", AllowMultipleOutConnections: true)]));
        node.Add("m_boneName", boneName);
        return node;
    }

    private KVObject CreateSnapWeaponNode(int nodeIndex)
    {
        var node = CreateBaseNode("CnmGraphDocSnapWeaponNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Input", "Pose"),
            new PinDef("Flashed Amount", "Float"),
            new PinDef("Weapon Category", "ID"),
            new PinDef("Weapon Type", "ID"),
        ]));
        node.Add("m_outputPins", MakePins([new PinDef("Result", "Pose", AllowMultipleOutConnections: true)]));
        return node;
    }

    private KVObject CreateTransitionResultNode(string className, int transitionNodeIndex, bool canBeForced, string? stateId)
    {
        var transition = GetDefinition<TransitionNode>(transitionNodeIndex);
        var transitionFlags = transition.TransitionOptions.Flags;

        var node = CreateBaseNode(className, MakeGuid(), GetNodeName(transitionNodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Condition", "Bool"),
            new PinDef("Duration Override", "Float"),
            new PinDef("Sync Event Override", "Float"),
            new PinDef("Start Bone Mask", "BoneMask"),
            new PinDef("Target Sync ID", "ID"),
        ]));
        node.Add("m_outputPins", KVObject.Array());
        node.Add("m_resultType", "Special");
        node.Add("m_flDurationSeconds", transition.DurationSeconds);
        node.Add("m_bClampDurationToSource", IsTransitionFlagSet(transitionFlags, 1));
        node.Add("m_rootMotionBlend", transition.RootMotionBlend.ToString());
        node.Add("m_blendWeightEasing", transition.BlendWeightEasing.ToString());
        node.Add("m_flBoneMaskBlendInTimePercentage", transition.BoneMaskBlendInTimePercentage.Value);
        node.Add("m_timeMatchMode", GetTimeMatchMode(transitionFlags));
        node.Add("m_flTimeOffset", transition.TimeOffset);
        node.Add("m_bCanBeForced", canBeForced);

        if (stateId is not null)
        {
            node.Add("m_stateID", stateId);
        }

        return node;
    }

    private static bool IsTransitionFlagSet(uint flags, int flagBit)
        => (flags & (1u << flagBit)) != 0;

    private static string GetTimeMatchMode(uint flags)
    {
        var isSynchronized = IsTransitionFlagSet(flags, 2);
        var matchSourceTime = IsTransitionFlagSet(flags, 3);
        var matchSyncEventIndex = IsTransitionFlagSet(flags, 4);
        var matchSyncEventId = IsTransitionFlagSet(flags, 5);
        var matchSyncEventPercentage = IsTransitionFlagSet(flags, 6);
        var preferClosestSyncEventId = IsTransitionFlagSet(flags, 7);
        var matchTimeInSeconds = IsTransitionFlagSet(flags, 8);
        var offsetTimeInSeconds = IsTransitionFlagSet(flags, 9);

        if (matchTimeInSeconds)
        {
            return "MatchTimeInSeconds";
        }

        if (offsetTimeInSeconds)
        {
            return "OffsetTimeInSeconds";
        }

        if (isSynchronized)
        {
            return "Synchronized";
        }

        if (matchSourceTime)
        {
            if (matchSyncEventId)
            {
                return preferClosestSyncEventId
                    ? matchSyncEventPercentage ? "MatchClosestSyncEventIDAndPercentage" : "MatchClosestSyncEventID"
                    : matchSyncEventPercentage ? "MatchSyncEventIDAndPercentage" : "MatchSyncEventID";
            }

            if (matchSyncEventIndex)
            {
                return matchSyncEventPercentage ? "MatchSourceSyncEventIndexAndPercentage" : "MatchSourceSyncEventIndex";
            }

            if (matchSyncEventPercentage)
            {
                return "MatchSourceSyncEventPercentage";
            }
        }

        return "None";
    }

    private KVObject CreateDefaultGlobalTransitionNode(int stateNodeIndex, string stateId)
    {
        var node = CreateBaseNode("CNmGraphDocGlobalTransitionNode", MakeGuid(), GetNodeName(stateNodeIndex));
        node.Add("m_inputPins", MakePins([
            new PinDef("Condition", "Bool"),
            new PinDef("Duration Override", "Float"),
            new PinDef("Sync Event Override", "Float"),
            new PinDef("Start Bone Mask", "BoneMask"),
            new PinDef("Target Sync ID", "ID"),
        ]));
        node.Add("m_outputPins", KVObject.Array());
        node.Add("m_resultType", "Special");
        node.Add("m_flDurationSeconds", 0.2f);
        node.Add("m_bClampDurationToSource", false);
        node.Add("m_rootMotionBlend", "Blend");
        node.Add("m_blendWeightEasing", "Linear");
        node.Add("m_flBoneMaskBlendInTimePercentage", 0.33f);
        node.Add("m_timeMatchMode", "None");
        node.Add("m_flTimeOffset", 0.0f);
        node.Add("m_bCanBeForced", false);
        node.Add("m_stateID", stateId);
        return node;
    }

    private KVObject CreateConstValueNode(string className, int nodeIndex, string outputType, string valueKey, object? value)
    {
        var node = CreateBaseNode(className, MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Value", outputType, AllowMultipleOutConnections: true)]));
        AddValue(node, valueKey, value ?? string.Empty);
        return node;
    }

    private KVObject CreateConstFloatNode(int nodeIndex, ConstFloatNode definition)
    {
        var variationData = CreateConstFloatVariationData(definition);
        var overrides = CreateVariationOverrides(nodeIndex, variationData, variationGraph
            => variationGraph.GetDefinition(nodeIndex) is ConstFloatNode variationNode ? CreateConstFloatVariationData(variationNode) : null);

        if (overrides.Count == 0)
        {
            return CreateConstValueNode(GetConstDocNodeClassName("Float"), nodeIndex, "Float", "m_flValue", definition.Value);
        }

        var node = CreateBaseNode("CnmGraphDocVariationConstFloatNode", MakeGuid(), GetNodeName(nodeIndex));
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", MakePins([new PinDef("Value", "Float", AllowMultipleOutConnections: true)]));
        node.Add("m_pDefaultVariationData", variationData);
        node.Add("m_overrides", overrides);
        node.Add("m_defaultResourceName", string.Empty);
        return node;
    }

    private static KVObject CreatePoseResultNode(string key)
        => CreateResultNode("CNmGraphDocPoseResultNode", key, "Out", "Pose");

    private static KVObject CreateStateLayerResultNode(string key)
    {
        var node = CreateBaseNode("CNmGraphDocStateLayerDataNode", MakeGuid(), string.Empty);
        node.Add("m_inputPins", MakePins([
            new PinDef("Layer Weight", "Float"),
            new PinDef("Root Motion Weight", "Float"),
            new PinDef("Layer Mask", "BoneMask"),
        ]));
        node.Add("m_outputPins", KVObject.Array());
        node.Add("m_resultType", "Special");
        return node;
    }

    private static KVObject CreateEntryOverrideConditionsNode(int stateMachineNodeIndex)
    {
        var node = CreateBaseNode("CNmGraphDocEntryStateOverrideConditionsNode", MakeGuid(), string.Empty);
        node.Add("m_inputPins", KVObject.Array());
        node.Add("m_outputPins", KVObject.Array());
        node.Add("m_resultType", "Special");
        node.Add("m_pinToStateMapping", KVObject.Array());
        return node;
    }

    private static KVObject CreateResultNode(string className, string key, string inputName, string inputType)
    {
        var node = CreateBaseNode(className, MakeGuid(), string.Empty);
        node.Add("m_inputPins", MakePins([new PinDef(inputName, inputType)]));
        node.Add("m_outputPins", KVObject.Array());
        node.Add("m_resultType", inputType);
        return node;
    }

    private KVObject BuildSelectorConditionGraph(int nodeIndex, short[] conditionNodeIndices, string[] optionLabels)
    {
        var graphBuilder = new FlowGraphBuilder($"selector:{nodeIndex}:conditions", "ValueTree");
        var conditionNode = CreateSelectorConditionNode(nodeIndex, optionLabels);
        graphBuilder.Nodes.Add(conditionNode);

        for (var i = 0; i < Math.Min(conditionNodeIndices.Length, optionLabels.Length); i++)
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

    private static KVObject CreateSelectorConditionNode(int nodeIndex, string[] optionLabels)
    {
        var node = CreateBaseNode("CNmGraphDocSelectorConditionNode", MakeGuid(), string.Empty);
        node.Add("m_inputPins", MakePins(optionLabels.Select(label => new PinDef(label, "Bool", IsDynamicPin: true))));
        node.Add("m_outputPins", KVObject.Array());
        node.Add("m_resultType", "Special");
        return node;
    }

    private static string[] GetNames(StringToken[] symbols)
        => [.. symbols.Select(static symbol => symbol.Name)];

    private static KVObject CloneValueArray(byte[] values)
    {
        var array = KVObject.Array();
        foreach (var value in values)
        {
            array.Add((int)value);
        }

        return array;
    }

    private static KVObject CloneValueArray(uint[] values)
    {
        var array = KVObject.Array();
        foreach (var value in values)
        {
            array.Add((long)value);
        }

        return array;
    }

    private static KVObject CloneValueArray(Vector2[] values)
    {
        var array = KVObject.Array();
        foreach (var value in values)
        {
            var point = KVObject.Array();
            point.Add(value.X);
            point.Add(value.Y);
            array.Add(point);
        }

        return array;
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

    private static void AddTimedStateEvents(KVObject result, string type, StateNode.TimedEvent[] timedEvents)
    {
        foreach (var timedEvent in timedEvents)
        {
            var output = KVObject.Collection();
            output.Add("m_ID", timedEvent.ID.Name);
            output.Add("m_type", type);
            output.Add("m_comparisonOperator", timedEvent.ComparisionOperator.ToString());
            output.Add("m_flTimeValueSeconds", timedEvent.TimeValueSeconds);
            result.Add(output);
        }
    }

    private static KVObject ConvertStateEvents(StateNode state)
    {
        var entryEvents = GetNames(state.EntryEvents);
        var executeEvents = GetNames(state.ExecuteEvents);
        var exitEvents = GetNames(state.ExitEvents);
        var stateEvents = KVObject.Array();

        foreach (var eventId in entryEvents
            .Concat(executeEvents)
            .Concat(exitEvents)
            .Distinct(StringComparer.Ordinal))
        {
            var stateEvent = KVObject.Collection();
            stateEvent.Add("m_ID", eventId);
            stateEvent.Add("m_bIsEntry", entryEvents.Contains(eventId, StringComparer.Ordinal));
            stateEvent.Add("m_bIsFullyInState", executeEvents.Contains(eventId, StringComparer.Ordinal));
            stateEvent.Add("m_bIsExit", exitEvents.Contains(eventId, StringComparer.Ordinal));
            stateEvents.Add(stateEvent);
        }

        return stateEvents;
    }

    private static PinDef[] CreateRepeatedPins(string name, string type, int count)
        => Enumerable.Range(0, count).Select(_ => new PinDef(name, type)).ToArray();

    private static bool IsControlParameterNode(KVObject compiledNode)
        => GetCompiledClass(compiledNode).TryGetTypedSuffix("ControlParameter", out _);

    private bool IsVirtualParameterNode(int nodeIndex)
        => _virtualParameterIdsByNodeIndex.ContainsKey(nodeIndex);

    private string GetVirtualParameterName(int nodeIndex)
        => _virtualParameterIdsByNodeIndex.GetValueOrDefault(nodeIndex, GetNodeName(nodeIndex));

    private string GetVirtualParameterValueType(int nodeIndex)
    {
        var compiledNode = GetCompiledNode(nodeIndex)
            ?? throw new InvalidDataException($"Missing virtual parameter node {nodeIndex}.");
        var compiledClass = GetCompiledClass(compiledNode);
        return GetVirtualParameterValueType(compiledClass);
    }

    private static KVObject? GetOptionalObject(KVObject node, string key)
        => node.TryGetValue(key, out var value) && !value.IsNull ? value : null;

    private static string GetOptionalString(KVObject node, string key, string fallback = "")
        => GetOptionalObject(node, key) is { } value ? (string)value : fallback;

    private static string GetCompiledClassName(KVObject node)
        => node.GetStringProperty("_class");

    private static CompiledNodeClass GetCompiledClass(KVObject node)
        => new(GetCompiledClassName(node));

    private static string GetTypedDocNodeClassName(string valueType, string suffix)
        => $"CNmGraphDoc{valueType}{suffix}Node";

    private static string GetConstDocNodeClassName(string valueType)
        => $"CnmGraphDocConst{valueType}Node";

    private static IReadOnlyList<(string Key, object Value)> GetControlParameterExtraFields(string valueType)
        => valueType switch
        {
            "Float" => [("m_previewStartValue", 0.0f), ("m_previewMin", 0.0f), ("m_previewMax", 1.0f)],
            "Bool" => [("m_previewStartValue", false)],
            "ID" => [("m_previewStartValue", string.Empty), ("m_expectedValues", KVObject.Array())],
            "Vector" or "Target" => [],
            _ => throw new InvalidDataException($"Unsupported control parameter value type: {valueType}"),
        };

    private static float[] GetBlend1DPointValues(ParameterizedBlendNode.ParameterizationType parameterization, int sourceCount)
    {
        if (sourceCount <= 0)
        {
            return [];
        }

        var values = new float[sourceCount];
        var hasValue = new bool[sourceCount];

        if (parameterization is not { BlendRanges.Length: > 0 })
        {
            return values;
        }

        foreach (var range in parameterization.BlendRanges)
        {
            if (range.InputIdx0 >= 0 && range.InputIdx0 < sourceCount)
            {
                values[range.InputIdx0] = range.ParameterValueRange.Min;
                hasValue[range.InputIdx0] = true;
            }

            if (range.InputIdx1 >= 0 && range.InputIdx1 < sourceCount)
            {
                values[range.InputIdx1] = range.ParameterValueRange.Max;
                hasValue[range.InputIdx1] = true;
            }
        }

        for (var i = 0; i < sourceCount; i++)
        {
            if (!hasValue[i])
            {
                values[i] = i == 0 ? parameterization.ParameterRange.Min : parameterization.ParameterRange.Max;
            }
        }

        return values;
    }

    private static KVObject CloneRange(Animation.Range source)
    {
        var range = KVObject.Collection();
        range.Add("m_flMin", source.Min);
        range.Add("m_flMax", source.Max);
        return range;
    }

    private static KVObject CloneRemapRange(FloatRemapNode.RemapRange source)
    {
        var range = KVObject.Collection();
        range.Add("m_flBegin", source.Begin);
        range.Add("m_flEnd", source.End);
        return range;
    }

    private static KVObject CloneVector3(Vector3 source)
    {
        var vector = KVObject.Array();
        vector.Add(source.X);
        vector.Add(source.Y);
        vector.Add(source.Z);
        return vector;
    }

    private static KVObject CloneVector3(float[]? source)
    {
        var vector = KVObject.Array();
        vector.Add(source is not null && source.Length > 0 ? source[0] : 0.0f);
        vector.Add(source is not null && source.Length > 1 ? source[1] : 0.0f);
        vector.Add(source is not null && source.Length > 2 ? source[2] : 0.0f);
        return vector;
    }

    private static EventConditionRulesData GetEventConditionRules(BitFlags rules)
    {
        var flags = rules.Flags;

        return new EventConditionRulesData(
            Operator: (flags & (1u << (int)NmEventConditionRules.OperatorAnd)) != 0 ? "And" : "Or",
            SearchRule: (flags & (1u << (int)NmEventConditionRules.SearchOnlyGraphEvents)) != 0
                ? "OnlySearchGraphEvents"
                : (flags & (1u << (int)NmEventConditionRules.SearchOnlyAnimEvents)) != 0 ? "OnlySearchAnimEvents" : "SearchAll",
            PriorityRule: (flags & (1u << (int)NmEventConditionRules.PreferHighestProgress)) != 0 ? "HighestPercentageThrough" : "HighestWeight",
            LimitSearchToSourceState: (flags & (1u << (int)NmEventConditionRules.LimitSearchToSourceState)) != 0,
            IgnoreInactiveBranchEvents: (flags & (1u << (int)NmEventConditionRules.IgnoreInactiveEvents)) != 0);
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

    private string GetVelocityBlendInputLabel(int sourceNodeIndex, int fallbackIndex)
    {
        var fallbackLabel = GetNodeName(sourceNodeIndex);
        if (fallbackLabel.StartsWith("Node ", StringComparison.Ordinal))
        {
            fallbackLabel = $"Input {fallbackIndex}";
        }

        var averageLinearVelocity = GetClipAverageLinearVelocity(sourceNodeIndex);
        return averageLinearVelocity.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"{fallbackLabel} ({averageLinearVelocity.Value:0.##})")
            : fallbackLabel;
    }

    private float? GetClipAverageLinearVelocity(int nodeIndex)
    {
        var dataSlotIndex = GetDefinition(nodeIndex) switch
        {
            ClipNode clip => clip.DataSlotIdx,
            TimeControlledClipNode clip => clip.DataSlotIdx,
            AnimationPoseNode pose => pose.DataSlotIdx,
            _ => -1,
        };

        if (dataSlotIndex < 0)
        {
            return null;
        }

        var resourcePath = GetResourcePath(dataSlotIndex);
        if (string.IsNullOrEmpty(resourcePath))
        {
            return null;
        }

        if (_clipAverageLinearVelocityCache.TryGetValue(resourcePath, out var cachedValue))
        {
            return cachedValue;
        }

        using var clipResource = _fileLoader.LoadFileCompiled(resourcePath);
        if (clipResource?.DataBlock is not BinaryKV3 clipData)
        {
            _clipAverageLinearVelocityCache[resourcePath] = null;
            return null;
        }

        var averageLinearVelocity = clipData.Data.Root.GetSubCollection("m_rootMotion")?.GetFloatProperty("m_flAverageLinearVelocity");
        _clipAverageLinearVelocityCache[resourcePath] = averageLinearVelocity;
        return averageLinearVelocity;
    }

    private string GetNodeName(int nodeIndex)
    {
        if (nodeIndex < 0 || nodeIndex >= _nodePaths.Length)
        {
            return $"Node {nodeIndex}";
        }

        return GetPathLeaf(_nodePaths[nodeIndex]);
    }

    private string GetResourcePath(int dataSlotIndex)
        => GetResourcePath(_definition, dataSlotIndex);

    private static string GetResourcePath(GraphDefinition definition, int dataSlotIndex)
        => dataSlotIndex >= 0 && dataSlotIndex < definition.Resources.Length ? definition.Resources[dataSlotIndex] : string.Empty;

    private string GetReferencedGraphPath(int referencedGraphIndex)
        => GetReferencedGraphPath(_definition, referencedGraphIndex);

    private static string GetReferencedGraphPath(GraphDefinition definition, int referencedGraphIndex)
    {
        if (referencedGraphIndex < 0 || referencedGraphIndex >= definition.ReferencedGraphSlots.Length)
        {
            return string.Empty;
        }

        return GetResourcePath(definition, definition.ReferencedGraphSlots[referencedGraphIndex].DataSlotIdx);
    }

    private string GetExternalGraphSlotId(int nodeIndex)
        => Array.Find(_definition.ExternalGraphSlots, slot => slot.NodeIdx == nodeIndex)?.SlotID.Name ?? string.Empty;

    private string GetExternalPoseSlotId(int nodeIndex)
        => Array.Find(_definition.ExternalPoseSlots, slot => slot.NodeIdx == nodeIndex)?.SlotID.Name ?? string.Empty;

    private KVObject? GetCompiledNode(int nodeIndex)
        => nodeIndex >= 0 && nodeIndex < _compiledNodes.Length ? _compiledNodes[nodeIndex] : null;

    // The node as the graph library reads it, or null for a class it has no node for
    private Animation.GraphNode? GetDefinition(int nodeIndex)
    {
        if (GetCompiledNode(nodeIndex) is not { } compiledNode)
        {
            return null;
        }

        if (!_definitionCreated[nodeIndex])
        {
            _definitionCreated[nodeIndex] = true;
            _definitions[nodeIndex] = CreateDefinition(compiledNode);
        }

        return _definitions[nodeIndex];
    }

    private T GetDefinition<T>(int nodeIndex) where T : Animation.GraphNode
        => GetDefinition(nodeIndex) as T
            ?? throw new InvalidDataException($"Node {nodeIndex} is not a {typeof(T).Name}.");

    private static Animation.GraphNode? CreateDefinition(KVObject compiledNode)
        => GetCompiledClassName(compiledNode) switch
        {
            // Older graphs name these two selectors differently, with the same members
            "CNmAnimationClipSelectorNode::CDefinition" => new ClipSelectorNode(compiledNode),
            "CNmParameterizedAnimationClipSelectorNode::CDefinition" => new ParameterizedClipSelectorNode(compiledNode),
            var className => GraphNodeFactory.IsSupported(className) ? GraphNodeFactory.Create(compiledNode) : null,
        };

    private static KVObject CreateBaseNode(string className, string nodeId, string name)
    {
        var node = KVObject.Collection();
        node.Add("_class", className);
        node.Add("m_ID", nodeId);
        node.Add("m_name", name);
        node.Add("m_floatingComment", string.Empty);
        node.Add("m_position", MakeVector2(0.0f, 0.0f));
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
                var point = overridePoints[overrideIndex++]!;
                array.Add(CreatePinWithId(pin.Name, pin.Type, point.GetStringProperty("m_pinID"), pin.IsDynamicPin, pin.AllowMultipleOutConnections));
                continue;
            }

            array.Add(CreatePin(pin.Name, pin.Type, pin.IsDynamicPin, pin.AllowMultipleOutConnections));
        }

        return array;
    }

    private static KVObject CreatePin(string name, string type, bool isDynamicPin = false, bool allowMultipleOutConnections = false)
        => CreatePinWithId(name, type, MakeGuid(), isDynamicPin, allowMultipleOutConnections);

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

    private static string MakeGuid()
        => Guid.NewGuid().ToString();

    private string GetOrCreateRootParameterNodeId(int nodeIndex)
    {
        if (_rootParameterNodeIds.TryGetValue(nodeIndex, out var nodeId))
        {
            return nodeId;
        }

        nodeId = MakeGuid();
        _rootParameterNodeIds[nodeIndex] = nodeId;
        return nodeId;
    }

    private static void AddValue(KVObject node, string key, object? value)
    {
        switch (value)
        {
            case null:
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
}
