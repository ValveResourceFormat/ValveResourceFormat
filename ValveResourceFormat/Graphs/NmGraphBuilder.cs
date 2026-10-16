using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Animation;
using ValveResourceFormat.Serialization.KeyValues;
using Node = ValveResourceFormat.Graphs.KVGraphNode;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// Builds the node graph of a compiled AG2 animation graph (.vnmgraph): one card per node of the
/// definition, wired by the pose and value pins the node constructors declare.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/animlib/CNmGraphDefinition">CNmGraphDefinition</seealso>
/// <param name="graphDefinition">The compiled graph definition to read.</param>
internal sealed class NmGraphBuilder(KVObject graphDefinition)
{
    private static readonly GraphHue PoseHue = AnimGraphHues.HueOf(AnimGraphValueKind.Pose);

    /// <summary>Name a card and a label take for an index that names no node.</summary>
    private const string MissingNodeName = "missing node";

    /// <summary>Node type of the cards a layer blend grows for each of its layers.</summary>
    private const string LayerDefinitionType = "_LayerDefinition_";

    private readonly HashSet<string> reportedUnknownNodeTypes = [];

    /// <summary>Whether state machine states are drawn as their own cards.</summary>
    public bool DrawStateMachines { get; set; }

    /// <summary>Whether control parameter wires are drawn instead of being inlined as annotations.</summary>
    public bool DrawParameterWires { get; set; }

    /// <summary>Whether the graph declares any control parameter, set once a build has run.</summary>
    public bool HasControlParameters { get; private set; }

    /// <summary>Receives diagnostics about how much of the definition was turned into cards.</summary>
    public IProgress<string>? ProgressReporter { get; set; }

    /// <summary>Fills <paramref name="document"/> with the graph and lays it out.</summary>
    /// <param name="document">The graph to fill.</param>
    public void Build(GraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CreateGraph(document);
    }

    /// <summary>The value kinds wires actually carry in this graph, for the legend.</summary>
    private readonly HashSet<AnimGraphValueKind> usedValueKinds = [];

    private static void SetResourceReference(GraphNode node, string resourceName)
    {
        var isGraphFile = resourceName.Contains(".vnmgraph", StringComparison.OrdinalIgnoreCase);
        var icon = isGraphFile ? "anmgrph" : "anim";

        node.AddResourceReference(resourceName, icon, AnimGraphHues.HueOf(AnimGraphCategory.ExternalReference));
    }

    private void CreateGraph(GraphDocument document)
    {
        var graph = new GraphDefinition(graphDefinition);
        var rootNodeIdx = graph.RootNodeIdx;
        var nodePaths = graph.NodePaths;
        var nodes = graphDefinition.GetArray("m_nodes");

        usedValueKinds.Clear();
        HasControlParameters = false;

        foreach (var definition in nodes)
        {
            if (definition.GetStringProperty("_class").StartsWith("CNmControlParameter", StringComparison.Ordinal))
            {
                HasControlParameters = true;
                break;
            }
        }

        bool IsValidNodeIndex(int nodeIdx) => nodeIdx >= 0 && nodeIdx < nodes.Count && nodeIdx < nodePaths.Length;

        string GetName(int nodeIdx)
        {
            if (nodeIdx < 0 || nodeIdx >= nodePaths.Length)
            {
                return MissingNodeName;
            }

            return nodePaths[nodeIdx].Split('/')[^1];
        }

        string GetType(int nodeIdx)
        {
            var className = nodes[nodeIdx].GetStringProperty("_class");
            const string Prefix = "CNm";
            const string Suffix = "Node::CDefinition";
            var @type = className[Prefix.Length..^Suffix.Length];
            return @type;
        }

        Dictionary<int, Node> createdNodes = new(nodes.Count);
        Dictionary<int, Animation.GraphNode?> definitions = new(nodes.Count);

        Node CreateNode(int nodeIdx)
        {
            if (createdNodes.TryGetValue(nodeIdx, out var existingNode))
            {
                return existingNode;
            }

            Node node;

            if (!IsValidNodeIndex(nodeIdx))
            {
                ProgressReporter?.Report($"Node index {nodeIdx} is out of range ({nodes.Count} nodes), adding a placeholder node.");

                node = new Node
                {
                    Name = $"({nodeIdx}) {MissingNodeName}",
                    NodeType = "Missing",
                    Category = AnimGraphHues.HueOf(AnimGraphCategory.Other),
                };
            }
            else
            {
                node = new Node
                {
                    Name = $"({nodeIdx}) {GetName(nodeIdx)}",
                    NodeType = GetType(nodeIdx),
                };

                // The authored container path feeds the right-click "Isolate group" action.
                var path = nodePaths[nodeIdx];
                var lastSlash = path.LastIndexOf('/');
                node.GroupPath = lastSlash > 0 ? path[..lastSlash] : null;

                var category = AnimGraphHues.CategoryOfAG2(node.NodeType);
                node.Category = AnimGraphHues.HueOf(category);

                if (category == AnimGraphCategory.Other && reportedUnknownNodeTypes.Add(node.NodeType))
                {
                    ProgressReporter?.Report($"Unclassified AG2 node type \"{node.NodeType}\".");
                }
            }

            document.AddNode(node);
            createdNodes[nodeIdx] = node;
            return node;
        }

        (Node, GraphSocket) CreateInputAndChild(Node parent, int nodeIdx, string? parentInputName = null, string? childOutputName = null, bool hub = false)
        {
            var (childNode, childNodeOutput) = CreateChild(nodeIdx, childOutputName);

            // The input takes the created output's hue so both ends of the wire agree on the
            // value kind the child produces.
            var input = parent.AddInput(parentInputName ?? childNode.Name, childNodeOutput.Hue, hub);
            document.Connect(childNodeOutput, input);

            return (childNode, input);
        }

        void AddOptionalInput(Node parent, int nodeIdx, string name)
        {
            if (nodeIdx != -1)
            {
                CreateInputAndChild(parent, nodeIdx, name);
            }
        }

        (Node, GraphSocket) CreateChild(int nodeIdx, string? childOutputName = null)
        {
            var childNode = CreateNode(nodeIdx);

            if (childNode.Outputs.Count > 0)
            {
                return (childNode, childNode.Outputs[0]);
            }

            if (childNode.NodeType is "Clip" or "TimeControlledClip" or "ReferencedGraph")
            {
                childOutputName = string.Empty;
            }

            // Every AG2 node constructor bakes its pin's value type; the type name tells which
            // one, so wires carry their real kind's colour.
            var kind = AnimGraphHues.AG2ValueKindOf(childNode.NodeType);

            if (kind is not (AnimGraphValueKind.Pose or AnimGraphValueKind.Unknown))
            {
                usedValueKinds.Add(kind);
            }

            var childNodeOutput = childNode.AddOutput(childOutputName ?? string.Empty, AnimGraphHues.HueOf(kind));

            CreateChildren(childNode, nodeIdx);

            return (childNode, childNodeOutput);
        }

        void AddSourceState(Node node, int sourceStateNodeIdx)
        {
            if (sourceStateNodeIdx != -1)
            {
                node.AddText($"State: {GetName(sourceStateNodeIdx)}");
            }
        }

        // The node as the graph library reads it, or null for a class it has no node for
        Animation.GraphNode? GetDefinition(int nodeIdx)
        {
            if (!definitions.TryGetValue(nodeIdx, out var definition))
            {
                var data = nodes[nodeIdx];
                definition = GraphNodeFactory.IsSupported(data.GetStringProperty("_class")) ? GraphNodeFactory.Create(data) : null;
                definitions[nodeIdx] = definition;
            }

            return definition;
        }

        void CreateStateInputs(Node stateMachine, StateMachineNode definition, List<Node>? stateGraphNodes)
        {
            foreach (var stateDefinition in definition.StateDefinitions)
            {
                var stateNodeIdx = stateDefinition.StateNodeIdx;
                var stateName = GetName(stateNodeIdx);
                var input = stateMachine.AddInput(stateName, PoseHue, allowMultiple: true);

                // The placeholder keeps the state list index-aligned with the transitions.
                if (!IsValidNodeIndex(stateNodeIdx) || GetDefinition(stateNodeIdx) is not StateNode state)
                {
                    var missingState = CreateNode(stateNodeIdx);
                    document.Connect(missingState.GetOrAddOutput(string.Empty, PoseHue), input);
                    stateGraphNodes?.Add(missingState);
                    continue;
                }

                // States materialize as their own nodes so transitions render as a statechart,
                // or flatten into input sockets on the state machine node itself.
                var target = stateMachine;
                var childInput = input;

                if (stateGraphNodes != null)
                {
                    target = document.AddNode(new Node
                    {
                        Name = $"({stateNodeIdx}) {stateName}",
                        NodeType = "State",
                        Category = AnimGraphHues.HueOf(AnimGraphCategory.StateMachine),
                    });
                    document.Connect(target.AddOutput(string.Empty, PoseHue), input);
                    stateGraphNodes.Add(target);
                }

                if (state.ChildNodeIdx != -1)
                {
                    var (_, stateNodeOut) = CreateChild(state.ChildNodeIdx);
                    document.Connect(stateNodeOut, stateGraphNodes != null ? target.AddInput(string.Empty, PoseHue, allowMultiple: true) : childInput);
                }

                if (stateDefinition.EntryConditionNodeIdx != -1)
                {
                    var (_, childOutput) = CreateChild(stateDefinition.EntryConditionNodeIdx, stateName);
                    document.Connect(childOutput, target.AddInput("Entry condition", childOutput.Hue, allowMultiple: true));
                }

                // A state inside a state machine layer can carry the bone mask that layer
                // blends with; the other per-state layer fields ship unused.
                if (state.LayerBoneMaskNodeIdx != -1)
                {
                    var (_, maskOutput) = CreateChild(state.LayerBoneMaskNodeIdx, stateName);
                    document.Connect(maskOutput, target.AddInput("Layer bone mask", maskOutput.Hue, allowMultiple: true));
                }
            }
        }

        void AddWeightedOptions(Node node, short parameterNodeIdx, short[] options, byte[] weights, bool hasWeightsSet)
        {
            CreateInputAndChild(node, parameterNodeIdx);

            var totalWeight = hasWeightsSet ? Math.Max(1, weights.Sum(static w => (int)w)) : 0;

            for (var i = 0; i < options.Length; i++)
            {
                var weightDesc = string.Empty;
                if (hasWeightsSet)
                {
                    var weight = weights[i];
                    var weightPercentage = weight / (float)totalWeight * 100;

                    weightDesc = string.Create(CultureInfo.InvariantCulture, $"Weight: {weight} ({weightPercentage:F2}%)");
                }

                CreateInputAndChild(node, options[i], $"Option {i + 1} {weightDesc}");
            }
        }

        void AddIdOptions(Node node, short parameterNodeIdx, short fallbackNodeIdx, short[] options, StringToken[] optionIds)
        {
            AddOptionalInput(node, parameterNodeIdx, "Parameter");
            AddOptionalInput(node, fallbackNodeIdx, "Fallback");

            // The option a given ID selects is positional, so the label carries the ID it matches.
            for (var i = 0; i < options.Length; i++)
            {
                CreateInputAndChild(node, options[i], i < optionIds.Length ? optionIds[i].Name : $"Option {i + 1}");
            }
        }

        void AddSwitchInputs(Node node, short switchValueNodeIdx, short trueNodeIdx, short falseNodeIdx)
        {
            AddOptionalInput(node, switchValueNodeIdx, "Switch");
            AddOptionalInput(node, trueNodeIdx, "True");
            AddOptionalInput(node, falseNodeIdx, "False");
        }

        void AddConditionalOptions(Node node, short[] options, short[] conditions)
        {
            foreach (var (optionNodeIdx, conditionNodeIdx) in options.Zip(conditions))
            {
                var (_, optionInput) = CreateInputAndChild(node, optionNodeIdx, hub: true);
                var (_, conditionOutput) = CreateChild(conditionNodeIdx);
                document.Connect(conditionOutput, optionInput);
            }
        }

        void AddBlendSources(Node node, short[] sourceNodeIndices, bool allowLooping)
        {
            for (var i = 0; i < sourceNodeIndices.Length; i++)
            {
                CreateInputAndChild(node, sourceNodeIndices[i], $"Option {i + 1}");
            }

            node.AddText($"Allow Looping: {allowLooping}");
        }

        void AddCachedValue(Node node, short inputValueNodeIdx, CachedValueMode mode)
        {
            CreateInputAndChild(node, inputValueNodeIdx, "Input");
            node.AddText($"Mode: {mode}");
        }

        void AddIKSettings(Node node, float blendTimeSeconds, string format, IKBlendMode? blendMode, bool isTargetInWorldSpace)
        {
            node.AddText(string.Create(CultureInfo.InvariantCulture, $"Blend Time: {blendTimeSeconds.ToString(format, CultureInfo.InvariantCulture)}"));

            if (blendMode != null)
            {
                node.AddText($"Blend Mode: {blendMode}");
            }

            node.AddText($"Worldspace: {isTargetInWorldSpace}");
        }

        void CreateChildren(Node node, int nodeIdx)
        {
            if (node.NodeType == "Missing" || !IsValidNodeIndex(nodeIdx))
            {
                return;
            }

            var data = nodes[nodeIdx];

            switch (GetDefinition(nodeIdx))
            {
                case StateMachineNode stateMachine when DrawStateMachines:
                {
                    var stateGraphNodes = new List<Node>(stateMachine.StateDefinitions.Length);
                    CreateStateInputs(node, stateMachine, stateGraphNodes);

                    // Dashed state-to-state transition wires labeled with their condition.
                    for (var stateIndex = 0; stateIndex < stateMachine.StateDefinitions.Length; stateIndex++)
                    {
                        foreach (var transition in stateMachine.StateDefinitions[stateIndex].TransitionDefinitions)
                        {
                            if (transition.TargetStateIdx < 0 || transition.TargetStateIdx >= stateGraphNodes.Count)
                            {
                                continue;
                            }

                            var label = transition.ConditionNodeIdx != -1 ? GetName(transition.ConditionNodeIdx) : null;
                            AnimGraphHues.ConnectTransition(document, stateGraphNodes[stateIndex], stateGraphNodes[transition.TargetStateIdx], label);
                        }
                    }

                    break;
                }

                case StateMachineNode stateMachine:
                    CreateStateInputs(node, stateMachine, stateGraphNodes: null);
                    break;

                case ParameterizedSelectorNode selector:
                    AddWeightedOptions(node, selector.ParameterNodeIdx, selector.OptionNodeIndices, selector.OptionWeights, selector.HasWeightsSet);
                    break;

                case ParameterizedClipSelectorNode selector:
                    AddWeightedOptions(node, selector.ParameterNodeIdx, selector.OptionNodeIndices, selector.OptionWeights, selector.HasWeightsSet);
                    break;

                case IDBasedSelectorNode selector:
                    AddIdOptions(node, selector.ParameterNodeIdx, selector.FallbackNodeIdx, selector.OptionNodeIndices, selector.OptionIDs);
                    break;

                case IDBasedClipSelectorNode selector:
                    AddIdOptions(node, selector.ParameterNodeIdx, selector.FallbackNodeIdx, selector.OptionNodeIndices, selector.OptionIDs);
                    break;

                case TargetSelectorNode selector:
                    AddOptionalInput(node, selector.ParameterNodeIdx, "Target");

                    foreach (var optionNodeIdx in selector.OptionNodeIndices)
                    {
                        CreateInputAndChild(node, optionNodeIdx);
                    }

                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Orientation Weight: {selector.OrientationScoreWeight:F2}"));
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Position Weight: {selector.PositionScoreWeight:F2}"));
                    node.AddText($"Worldspace: {selector.IsWorldSpaceTarget}");
                    break;

                // A switch branch with no wired node is a constant, so show that constant in its place.
                case FloatSwitchNode floatSwitch:
                    AddSwitchInputs(node, floatSwitch.SwitchValueNodeIdx, floatSwitch.TrueValueNodeIdx, floatSwitch.FalseValueNodeIdx);
                    if (floatSwitch.TrueValueNodeIdx == -1) { node.AddText(string.Create(CultureInfo.InvariantCulture, $"True: {floatSwitch.TrueValue:f}")); }
                    if (floatSwitch.FalseValueNodeIdx == -1) { node.AddText(string.Create(CultureInfo.InvariantCulture, $"False: {floatSwitch.FalseValue:f}")); }
                    break;

                case IDSwitchNode idSwitch:
                    AddSwitchInputs(node, idSwitch.SwitchValueNodeIdx, idSwitch.TrueValueNodeIdx, idSwitch.FalseValueNodeIdx);
                    if (idSwitch.TrueValueNodeIdx == -1) { node.AddText($"True: '{idSwitch.TrueValue.Name}'"); }
                    if (idSwitch.FalseValueNodeIdx == -1) { node.AddText($"False: '{idSwitch.FalseValue.Name}'"); }
                    break;

                case BoneMaskSwitchNode maskSwitch:
                    AddSwitchInputs(node, maskSwitch.SwitchValueNodeIdx, maskSwitch.TrueValueNodeIdx, maskSwitch.FalseValueNodeIdx);
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Blend Time: {maskSwitch.BlendTimeSeconds:F2}"));
                    node.AddText($"Switch Dynamically: {maskSwitch.SwitchDynamically}");
                    break;

                case BoneMaskSelectorNode maskSelector:
                    AddOptionalInput(node, maskSelector.ParameterValueNodeIdx, "Parameter");
                    AddOptionalInput(node, maskSelector.DefaultMaskNodeIdx, "Default");

                    for (var i = 0; i < maskSelector.MaskNodeIndices.Length; i++)
                    {
                        var label = i < maskSelector.ParameterValues.Length ? maskSelector.ParameterValues[i].Name : $"Mask {i + 1}";
                        CreateInputAndChild(node, maskSelector.MaskNodeIndices[i], label);
                    }

                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Blend Time: {maskSelector.BlendTimeSeconds:F2}"));
                    node.AddText($"Switch Dynamically: {maskSelector.SwitchDynamically}");
                    break;

                case BoneMaskBlendNode maskBlend:
                    AddOptionalInput(node, maskBlend.SourceMaskNodeIdx, "Source");
                    AddOptionalInput(node, maskBlend.TargetMaskNodeIdx, "Target");
                    AddOptionalInput(node, maskBlend.BlendWeightValueNodeIdx, "Blend Weight");
                    break;

                case OrientationWarpNode warp:
                    AddOptionalInput(node, warp.ClipReferenceNodeIdx, "Clip");
                    AddOptionalInput(node, warp.TargetValueNodeIdx, "Target");
                    node.AddText($"Offset: {warp.IsOffsetNode}");
                    node.AddText($"Relative To Character: {warp.IsOffsetRelativeToCharacter}");
                    node.AddText($"Warp Translation: {warp.WarpTranslation}");
                    node.AddText($"Alignment: {warp.AlignmentMode}");
                    node.AddText($"Sampling: {warp.SamplingMode}");
                    break;

                case TargetWarpNode warp:
                    AddOptionalInput(node, warp.ClipReferenceNodeIdx, "Clip");
                    AddOptionalInput(node, warp.TargetValueNodeIdx, "Target");
                    node.AddText($"Update Rule: {warp.TargetUpdateRule}");
                    node.AddText($"Align At Last Warp Event: {warp.AlignWithTargetAtLastWarpEvent}");
                    node.AddText($"Sampling: {warp.SamplingMode}");
                    break;

                case SelectorNode selector:
                    AddConditionalOptions(node, selector.OptionNodeIndices, selector.ConditionNodeIndices);
                    break;

                case ClipSelectorNode selector:
                    AddConditionalOptions(node, selector.OptionNodeIndices, selector.ConditionNodeIndices);
                    break;

                // The first condition that passes picks the value at its position.
                case FloatSelectorNode selector:
                    for (var i = 0; i < selector.ConditionNodeIndices.Length; i++)
                    {
                        CreateInputAndChild(node, selector.ConditionNodeIndices[i], i < selector.Values.Length
                            ? string.Create(CultureInfo.InvariantCulture, $"= {selector.Values[i]:F2}")
                            : null);
                    }

                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Default: {selector.DefaultValue:F2}"));
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Ease: {selector.EasingOp} {selector.EaseTime:F2}s"));
                    break;

                case IDSelectorNode selector:
                    for (var i = 0; i < selector.ConditionNodeIndices.Length; i++)
                    {
                        CreateInputAndChild(node, selector.ConditionNodeIndices[i], i < selector.Values.Length ? $"= {selector.Values[i].Name}" : null);
                    }

                    node.AddText($"Default: '{selector.DefaultValue.Name}'");
                    break;

                case LayerBlendNode layerBlend:
                {
                    CreateInputAndChild(node, layerBlend.BaseNodeIdx, "Base", "Result");

                    var layerInput = node.AddInput("Layers", PoseHue, allowMultiple: true);

                    for (var layerIndex = 0; layerIndex < layerBlend.LayerDefinition.Length; layerIndex++)
                    {
                        var layer = layerBlend.LayerDefinition[layerIndex];
                        var layerNode = document.AddNode(new Node
                        {
                            Name = $"Layer{layerIndex}",
                            NodeType = LayerDefinitionType,
                            Category = AnimGraphHues.HueOf(AnimGraphHues.CategoryOfAG2(LayerDefinitionType)),
                        });

                        document.Connect(layerNode.AddOutput(string.Empty, PoseHue), layerInput);
                        CreateInputAndChild(layerNode, layer.InputNodeIdx);

                        AddOptionalInput(layerNode, layer.WeightValueNodeIdx, "Weight");
                        AddOptionalInput(layerNode, layer.BoneMaskValueNodeIdx, "Bone Mask");
                        AddOptionalInput(layerNode, layer.RootMotionWeightValueNodeIdx, "Root Motion");

                        layerNode.AddText($"Is Synchronized: {layer.IsSynchronized}");
                        layerNode.AddText($"Ignore Events: {layer.IgnoreEvents}");
                        layerNode.AddText($"Is State Machine Layer: {layer.IsStateMachineLayer}");
                        layerNode.AddText($"Blend Mode: {layer.BlendMode}");
                    }

                    break;
                }

                case Blend2DNode blend:
                    CreateInputAndChild(node, blend.InputParameterNodeIdx0, "Parameter A");
                    CreateInputAndChild(node, blend.InputParameterNodeIdx1, "Parameter B");
                    AddBlendSources(node, blend.SourceNodeIndices, blend.AllowLooping);
                    break;

                case ParameterizedBlendNode blend:
                    CreateInputAndChild(node, blend.InputParameterValueNodeIdx, "Parameter");
                    AddBlendSources(node, blend.SourceNodeIndices, blend.AllowLooping);
                    break;

                case BoneMaskNode boneMask:
                    node.AddText(boneMask.BoneMaskID.Name);
                    break;

                case FixedWeightBoneMaskNode boneMask:
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Weight: {boneMask.BoneWeight:F2}"));
                    break;

                case CachedBoolNode cached:
                    AddCachedValue(node, cached.InputValueNodeIdx, cached.Mode);
                    break;

                case CachedFloatNode cached:
                    AddCachedValue(node, cached.InputValueNodeIdx, cached.Mode);
                    break;

                case CachedIDNode cached:
                    AddCachedValue(node, cached.InputValueNodeIdx, cached.Mode);
                    break;

                case CachedTargetNode cached:
                    AddCachedValue(node, cached.InputValueNodeIdx, cached.Mode);
                    break;

                case CachedVectorNode cached:
                    AddCachedValue(node, cached.InputValueNodeIdx, cached.Mode);
                    break;

                case ConstTargetNode constTarget:
                    node.AddText($"Bone: {constTarget.Value.BoneID.Name}");
                    node.AddText($"Is Bone Target: {constTarget.Value.IsBoneTarget}");
                    node.AddText($"Bone Space Offsets: {constTarget.Value.IsUsingBoneSpaceOffsets}");
                    node.AddText($"Has Offsets: {constTarget.Value.HasOffsets}");
                    node.AddText($"Is Set: {constTarget.Value.IsSet}");
                    break;

                case ConstFloatNode constFloat:
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"{constFloat.Value:F2}"));
                    break;

                case ConstBoolNode constBool:
                    node.AddText($"{constBool.Value}");
                    break;

                case ConstIDNode constId:
                    node.AddText($"'{constId.Value.Name}'");
                    break;

                case ConstVectorNode:
                    node.AddText(Node.StringifyValue(data["m_value"]));
                    break;

                case SpeedScaleBaseNode speedScale:
                {
                    CreateInputAndChild(node, speedScale.ChildNodeIdx, "Input");

                    var valueName = speedScale switch
                    {
                        DurationScaleNode => "Duration",
                        VelocityBasedSpeedScaleNode => "Velocity",
                        _ => "Scale Value",
                    };

                    AddOptionalInput(node, speedScale.InputValueNodeIdx, valueName);
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Default {valueName}: {speedScale.DefaultInputValue}"));
                    break;
                }

                case NotNode not:
                    CreateInputAndChild(node, not.InputValueNodeIdx, "Value");
                    break;

                case FloatCurveNode curve:
                    CreateInputAndChild(node, curve.InputValueNodeIdx, "Value");
                    break;

                case IsTargetSetNode isTargetSet:
                    CreateInputAndChild(node, isTargetSet.InputValueNodeIdx, "Value");
                    break;

                case VectorNegateNode negate:
                    CreateInputAndChild(node, negate.InputValueNodeIdx, "Value");
                    break;

                case FloatClampNode clamp:
                    CreateInputAndChild(node, clamp.InputValueNodeIdx, "Value");
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"{clamp.ClampRange.Min:F2} - {clamp.ClampRange.Max:F2}"));
                    break;

                case FloatAngleMathNode angleMath:
                    CreateInputAndChild(node, angleMath.InputValueNodeIdx, "Value");
                    node.AddText(angleMath.Operation.ToString());
                    break;

                case VectorInfoNode vectorInfo:
                    CreateInputAndChild(node, vectorInfo.InputValueNodeIdx, "Value");
                    node.AddText(vectorInfo.DesiredInfo.ToString());
                    break;

                case TargetInfoNode targetInfo:
                    CreateInputAndChild(node, targetInfo.InputValueNodeIdx, "Value");
                    node.AddText(targetInfo.InfoType.ToString());
                    node.AddText($"Worldspace: {targetInfo.IsWorldSpaceTarget}");
                    break;

                case TargetPointNode targetPoint:
                    CreateInputAndChild(node, targetPoint.InputValueNodeIdx, "Value");
                    node.AddText($"Worldspace: {targetPoint.IsWorldSpaceTarget}");
                    break;

                case TargetOffsetNode targetOffset:
                    CreateInputAndChild(node, targetOffset.InputValueNodeIdx, "Target");
                    node.AddText($"Bone Space: {targetOffset.IsBoneSpaceOffset}");
                    node.AddText($"Rotation: {Node.StringifyValue(data["m_rotationOffset"])}");
                    node.AddText($"Translation: {Node.StringifyValue(data["m_translationOffset"])}");
                    break;

                case VectorCreateNode vectorCreate:
                    AddOptionalInput(node, vectorCreate.InputVectorValueNodeIdx, "Vector");
                    AddOptionalInput(node, vectorCreate.InputValueXNodeIdx, "X");
                    AddOptionalInput(node, vectorCreate.InputValueYNodeIdx, "Y");
                    AddOptionalInput(node, vectorCreate.InputValueZNodeIdx, "Z");
                    break;

                case FloatEaseNode ease:
                    CreateInputAndChild(node, ease.InputValueNodeIdx, "Value");
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Ease: {ease.EasingOp} {ease.EaseTime:F2}s"));

                    if (ease.UseStartValue)
                    {
                        node.AddText(string.Create(CultureInfo.InvariantCulture, $"Start Value: {ease.StartValue:F2}"));
                    }

                    break;

                case FloatSpringNode spring:
                    CreateInputAndChild(node, spring.InputValueNodeIdx, "Value");
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Frequency: {spring.Hertz:F2} Hz"));
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Damping Ratio: {spring.DampingRatio:F2}"));

                    if (spring.UseStartValue)
                    {
                        node.AddText(string.Create(CultureInfo.InvariantCulture, $"Start Value: {spring.StartValue:F2}"));
                    }

                    break;

                case IDToFloatNode idToFloat:
                    CreateInputAndChild(node, idToFloat.InputValueNodeIdx, "ID");

                    for (var i = 0; i < idToFloat.IDs.Length && i < idToFloat.Values.Length; i++)
                    {
                        node.AddText(string.Create(CultureInfo.InvariantCulture, $"'{idToFloat.IDs[i].Name}': {idToFloat.Values[i]:F2}"));
                    }

                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Default: {idToFloat.DefaultValue:F2}"));
                    break;

                case FloatRemapNode remap:
                    CreateInputAndChild(node, remap.InputValueNodeIdx, "Value");
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"InputBegin: {remap.InputRange.Begin} InputEnd: {remap.InputRange.End}"));
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"OutputBegin: {remap.OutputRange.Begin} OutputEnd: {remap.OutputRange.End}"));
                    break;

                case FloatCurveEventNode curveEvent:
                    AddOptionalInput(node, curveEvent.DefaultNodeIdx, "Default");
                    node.AddText($"Event: '{curveEvent.EventID.Name}'");
                    break;

                case IDEventConditionNode eventCondition:
                    AddSourceState(node, eventCondition.SourceStateNodeIdx);

                    foreach (var eventId in eventCondition.EventIDs)
                    {
                        node.AddText($"Event: '{eventId.Name}'");
                    }

                    break;

                case GraphEventConditionNode eventCondition:
                    AddSourceState(node, eventCondition.SourceStateNodeIdx);

                    foreach (var condition in eventCondition.Conditions)
                    {
                        node.AddText($"Event: '{condition.EventID.Name}' ({condition.EventTypeCondition})");
                    }

                    break;

                case IDEventNode idEvent:
                    AddSourceState(node, idEvent.SourceStateNodeIdx);
                    node.AddText($"Default: '{idEvent.DefaultValue.Name}'");
                    break;

                case IDEventPercentageThroughNode idEvent:
                    AddSourceState(node, idEvent.SourceStateNodeIdx);
                    node.AddText($"Event: '{idEvent.EventID.Name}'");
                    break;

                case FootEventConditionNode footEvent:
                    AddSourceState(node, footEvent.SourceStateNodeIdx);
                    node.AddText($"Phase: {footEvent.PhaseCondition}");
                    break;

                case FootstepEventPercentageThroughNode footEvent:
                    AddSourceState(node, footEvent.SourceStateNodeIdx);
                    node.AddText($"Phase: {footEvent.PhaseCondition}");
                    break;

                case FootstepEventIDNode footEvent:
                    AddSourceState(node, footEvent.SourceStateNodeIdx);
                    break;

                case TransitionEventConditionNode transitionEvent:
                    AddSourceState(node, transitionEvent.SourceStateNodeIdx);
                    node.AddText($"Rule: {transitionEvent.RuleCondition}");

                    if (transitionEvent.RequireRuleID.Name.Length > 0)
                    {
                        node.AddText($"Rule ID: '{transitionEvent.RequireRuleID.Name}'");
                    }

                    break;

                case SyncEventIndexConditionNode syncEvent:
                    AddSourceState(node, syncEvent.SourceStateNodeIdx);
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"{syncEvent.TriggerMode}: {syncEvent.SyncEventIdx}"));
                    break;

                case CurrentSyncEventNode syncEvent:
                    AddSourceState(node, syncEvent.SourceStateNodeIdx);
                    node.AddText(syncEvent.InfoType.ToString());
                    break;

                case CurrentSyncEventIDNode syncEvent:
                    AddSourceState(node, syncEvent.SourceStateNodeIdx);
                    break;

                case StateCompletedConditionNode stateCompleted:
                    AddSourceState(node, stateCompleted.SourceStateNodeIdx);
                    AddOptionalInput(node, stateCompleted.TransitionDurationOverrideNodeIdx, "Duration Override");
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Transition Duration: {stateCompleted.TransitionDurationSeconds:F2}"));
                    break;

                case TimeConditionNode timeCondition:
                    AddSourceState(node, timeCondition.SourceStateNodeIdx);
                    AddOptionalInput(node, timeCondition.InputValueNodeIdx, "Time Value");
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"{timeCondition.Type} {timeCondition.Operator} {timeCondition.Comparand:F2}"));
                    break;

                case IsExternalGraphSlotFilledNode slotFilled:
                    node.AddText($"Slot: {GetName(slotFilled.ExternalGraphNodeIdx)}");
                    break;

                case IsExternalPoseSetNode poseSet:
                    node.AddText($"Slot: {GetName(poseSet.ExternalPoseNodeIdx)}");
                    break;

                case FloatMathNode math:
                    CreateInputAndChild(node, math.InputValueNodeIdxA, "A");
                    node.AddText(math.Operator.ToString());

                    if (math.InputValueNodeIdxB != -1)
                    {
                        CreateInputAndChild(node, math.InputValueNodeIdxB, "B");
                    }
                    else
                    {
                        node.AddText(string.Create(CultureInfo.InvariantCulture, $"{math.ValueB:f}"));
                    }

                    break;

                case IDComparisonNode comparison:
                    CreateInputAndChild(node, comparison.InputValueNodeIdx, GetName(comparison.InputValueNodeIdx));
                    node.AddText(comparison.Comparison.ToString());

                    foreach (var comparisonId in comparison.ComparisionIDs)
                    {
                        node.AddText($"'{comparisonId.Name}'");
                    }

                    break;

                case FloatComparisonNode comparison:
                    CreateInputAndChild(node, comparison.InputValueNodeIdx, GetName(comparison.InputValueNodeIdx));
                    node.AddText(comparison.Comparison.ToString());

                    if (comparison.ComparandValueNodeIdx != -1)
                    {
                        CreateInputAndChild(node, comparison.ComparandValueNodeIdx, "Comparand");
                    }
                    else
                    {
                        node.AddText(string.Create(CultureInfo.InvariantCulture, $"{comparison.ComparisonValue:f}"));
                    }

                    break;

                case FloatRangeComparisonNode comparison:
                    CreateInputAndChild(node, comparison.InputValueNodeIdx, GetName(comparison.InputValueNodeIdx));
                    node.AddText(comparison.IsInclusiveCheck
                        ? string.Create(CultureInfo.InvariantCulture, $"{comparison.Range.Min:f} <= x <= {comparison.Range.Max:f}")
                        : string.Create(CultureInfo.InvariantCulture, $"{comparison.Range.Min:f} < x < {comparison.Range.Max:f}"));
                    break;

                case AndNode and:
                    foreach (var condition in and.ConditionNodeIndices)
                    {
                        CreateInputAndChild(node, condition);
                    }

                    break;

                case OrNode or:
                    foreach (var condition in or.ConditionNodeIndices)
                    {
                        CreateInputAndChild(node, condition);
                    }

                    break;

                case PassthroughNode passthrough:
                    switch (passthrough)
                    {
                        case ScaleNode scale:
                            CreateInputAndChild(node, scale.MaskNodeIdx, "Mask");
                            CreateInputAndChild(node, scale.EnableNodeIdx, "Enable");
                            break;

                        case TwoBoneIKNode ik:
                            node.AddText($"Bone: {ik.EffectorBoneID.Name}");
                            CreateInputAndChild(node, ik.EffectorTargetNodeIdx, "Effector");

                            if (ik.EnabledNodeIdx != -1)
                            {
                                CreateInputAndChild(node, ik.EnabledNodeIdx, "Enabled");
                            }
                            else
                            {
                                node.AddText("Enabled: true");
                            }

                            AddIKSettings(node, ik.BlendTimeSeconds, "f", ik.BlendMode, ik.IsTargetInWorldSpace);
                            break;

                        case FootIKNode ik:
                            node.AddText($"Left Effector: {ik.LeftEffectorBoneID.Name}");
                            node.AddText($"Right Effector: {ik.RightEffectorBoneID.Name}");

                            CreateInputAndChild(node, ik.LeftTargetNodeIdx, "Left Target");
                            CreateInputAndChild(node, ik.RightTargetNodeIdx, "Right Target");
                            AddOptionalInput(node, ik.EnabledNodeIdx, "Enabled");

                            AddIKSettings(node, ik.BlendTimeSeconds, "F2", ik.BlendMode, ik.IsTargetInWorldSpace);
                            break;

                        case ChainLookatNode lookat:
                            node.AddText($"End Effector: {lookat.EndEffectorBoneID.Name}");
                            node.AddText(string.Create(CultureInfo.InvariantCulture, $"Chain Length: {lookat.ChainLength}"));

                            AddOptionalInput(node, lookat.LookatTargetNodeIdx, "Lookat Target");
                            AddOptionalInput(node, lookat.EnabledNodeIdx, "Enabled");

                            AddIKSettings(node, lookat.BlendTimeSeconds, "F2", blendMode: null, lookat.IsTargetInWorldSpace);
                            break;

                        case FollowBoneNode follow:
                            node.AddText($"Bone: {follow.Bone.Name}");
                            node.AddText($"Follow: {follow.FollowTargetBone.Name}");
                            node.AddText($"Mode: {follow.Mode}");

                            AddOptionalInput(node, follow.EnabledNodeIdx, "Enabled");
                            break;

                        case RootMotionOverrideNode rootMotion:
                            AddOptionalInput(node, rootMotion.DesiredMovingVelocityNodeIdx, "Moving Velocity");
                            AddOptionalInput(node, rootMotion.DesiredFacingDirectionNodeIdx, "Facing Direction");
                            AddOptionalInput(node, rootMotion.LinearVelocityLimitNodeIdx, "Linear Velocity Limit");
                            AddOptionalInput(node, rootMotion.AngularVelocityLimitNodeIdx, "Angular Velocity Limit");
                            AddOptionalInput(node, rootMotion.EnabledNodeIdx, "Enabled");
                            break;

                        case BodyGroupNode bodyGroup:
                            AddOptionalInput(node, bodyGroup.EnabledNodeIdx, "Enabled");
                            break;
                    }

                    CreateInputAndChild(node, passthrough.ChildNodeIdx, "Input", "Result");
                    break;

                case ReferencedGraphNode referencedGraph:
                {
                    var dataSlotIdx = graph.ReferencedGraphSlots[referencedGraph.ReferencedGraphIdx].DataSlotIdx;

                    if (referencedGraph.FallbackNodeIdx != -1)
                    {
                        node.AddSpace();
                        CreateInputAndChild(node, referencedGraph.FallbackNodeIdx, "Fallback");
                    }

                    AddDataSlot(node, dataSlotIdx);
                    break;
                }

                case ClipNode clip:
                    node.AddSpace();
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Speed: {clip.SpeedMultiplier:F2}x"));
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"StartSyncEvent Offset: {clip.StartSyncEventOffset}"));
                    node.AddText($"Sample RootMotion: {clip.SampleRootMotion}");
                    node.AddText($"Allow Looping: {clip.AllowLooping}");

                    AddOptionalInput(node, clip.PlayInReverseValueNodeIdx, "Play in reverse");
                    AddOptionalInput(node, clip.ResetTimeValueNodeIdx, "Reset time");
                    AddDataSlot(node, clip.DataSlotIdx);
                    break;

                case TimeControlledClipNode clip:
                    node.AddSpace();
                    node.AddText($"Sample RootMotion: {clip.SampleRootMotion}");

                    AddOptionalInput(node, clip.TimeValueNodeIdx, "Time");
                    AddOptionalInput(node, clip.PlayInReverseValueNodeIdx, "Play in reverse");
                    AddDataSlot(node, clip.DataSlotIdx);
                    break;

                case AnimationPoseNode pose:
                {
                    node.AddSpace();

                    AddOptionalInput(node, pose.PoseTimeValueNodeIdx, "Time");

                    var remap = pose.InputTimeRemapRange;
                    var remapMinDesc = remap.Min == float.MaxValue ? "None" : string.Create(CultureInfo.InvariantCulture, $"{remap.Min:f}");
                    var remapMaxDesc = remap.Max == float.MinValue ? "None" : string.Create(CultureInfo.InvariantCulture, $"{remap.Max:f}");

                    node.AddText($"Remap: {remapMinDesc} - {remapMaxDesc}");
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Const Time: {pose.UserSpecifiedTime:f}"));
                    node.AddText($"Use frames: {pose.UseFramesAsInput}");
                    AddDataSlot(node, pose.DataSlotIdx);
                    break;
                }

                case ExternalPoseNode externalPose:
                    node.AddText($"Sample RootMotion: {externalPose.ShouldSampleRootMotion}");
                    break;

                default:
                    CreateUntypedChildren(node, data);
                    break;
            }
        }

        void AddDataSlot(Node node, int dataSlotIdx)
        {
            if (dataSlotIdx != -1)
            {
                SetResourceReference(node, graph.Resources[dataSlotIdx]);
            }
        }

        // Nodes the graph library has no class for, such as the ones client code evaluates
        void CreateUntypedChildren(Node node, KVObject data)
        {
            if (data.ContainsKey("m_nChildNodeIdx"))
            {
                if (node.NodeType is "AimCS")
                {
                    AddOptionalInput(node, data.GetInt32Property("m_nVerticalAngleNodeIdx"), "Vertical Angle");
                    AddOptionalInput(node, data.GetInt32Property("m_nHorizontalAngleNodeIdx"), "Horizontal Angle");
                    AddOptionalInput(node, data.GetInt32Property("m_nWeaponCategoryNodeIdx"), "Weapon Category");
                    AddOptionalInput(node, data.GetInt32Property("m_nWeaponTypeNodeIdx"), "Weapon Type");
                    AddOptionalInput(node, data.GetInt32Property("m_nIsWeaponActionActiveNodeIdx", -1), "Is Weapon Action Active");
                    AddOptionalInput(node, data.GetInt32Property("m_nWeaponActionNodeIdx", -1), "Weapon Action");
                    AddOptionalInput(node, data.GetInt32Property("m_nWeaponDropNodeIdx", -1), "Weapon Drop");
                    AddOptionalInput(node, data.GetInt32Property("m_nIsDefusingNodeIdx", -1), "Is Defusing");
                    AddOptionalInput(node, data.GetInt32Property("m_nDisableHandIKNodeIdx", -1), "Disable Hand IK");
                    AddOptionalInput(node, data.GetInt32Property("m_nCrouchWeightNodeIdx"), "Crouch Weight");

                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Hand IK Blend In: {data.GetFloatProperty("m_flHandIKBlendInTimeSeconds"):F2}"));
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Action Blend Time: {data.GetFloatProperty("m_flActionBlendTimeSeconds"):F2}"));

                    if (data.ContainsKey("m_flPlantingBlendTimeSeconds"))
                    {
                        node.AddText(string.Create(CultureInfo.InvariantCulture, $"Planting Blend Time: {data.GetFloatProperty("m_flPlantingBlendTimeSeconds"):F2}"));
                    }
                }
                else if (node.NodeType is "SnapWeapon")
                {
                    AddOptionalInput(node, data.GetInt32Property("m_nFlashedAmountNodeIdx"), "Flashed Amount");
                    AddOptionalInput(node, data.GetInt32Property("m_nWeaponCategoryNodeIdx"), "Weapon Category");
                    AddOptionalInput(node, data.GetInt32Property("m_nWeaponTypeNodeIdx"), "Weapon Type");
                }

                CreateInputAndChild(node, data.GetInt32Property("m_nChildNodeIdx"), "Input", "Result");
            }
            else if (node.NodeType.StartsWith("ControlParameter", StringComparison.Ordinal)
                || node.NodeType is "ZeroPose" or "ReferencePose" or "IsInactiveBranchCondition")
            {
                // Nothing to show: a value set from outside the graph, or a node with no settings
            }
            else
            {
                ProgressReporter?.Report($"Unhandled node type: {node.NodeType} ({node.Name})");
            }
        }

        var finalPose = new Node
        {
            Name = "Result",
            NodeType = "FinalPose",
            Category = PoseHue,
        };

        var finalPoseInput = finalPose.AddInput("Out", PoseHue, allowMultiple: false);
        document.AddNode(finalPose);

        var root = CreateNode(rootNodeIdx);

        var rootOutput = root.AddOutput(string.Empty, PoseHue);
        document.Connect(rootOutput, finalPoseInput);

        CreateChildren(root, rootNodeIdx);

        for (var i = 0; i < nodes.Count; i++)
        {
            if (!createdNodes.ContainsKey(i) && GetType(i).StartsWith("ControlParameter", StringComparison.Ordinal))
            {
                CreateNode(i);
            }
        }

        if (!DrawParameterWires)
        {
            SuppressParameterWires(document);
        }

        document.LayoutOptions.LongWireDummies = true;
        document.LayoutNodesPacked();

        document.Legend.AddRange(AnimGraphHues.Legend());

        foreach (var kind in Enum.GetValues<AnimGraphValueKind>())
        {
            if (usedValueKinds.Contains(kind))
            {
                document.Legend.Add(new($"{ValueKindLabel(kind)} value", AnimGraphHues.HueOf(kind), GraphLegendKind.Wire));
            }
        }

        ProgressReporter?.Report(string.Create(CultureInfo.InvariantCulture, $"Created {createdNodes.Count} nodes (out of {nodes.Count}) or {createdNodes.Count / (float)nodes.Count:P}."));
    }

    private static string ValueKindLabel(AnimGraphValueKind kind) => kind switch
    {
        AnimGraphValueKind.Id => "ID",
        AnimGraphValueKind.BoneMask => "Bone mask",
        _ => kind.ToString(),
    };

    /// <summary>
    /// Replaces every wire leaving a control parameter node with an annotation on its consumer
    /// naming the parameter, and drops the sockets that end up bare. The parameter nodes stay
    /// as loose reference cards, parked beside the graph like the AG1 parameter hubs.
    /// </summary>
    private static void SuppressParameterWires(GraphDocument document)
    {
        var parameters = document.Nodes.OfType<Node>().Where(static node => node.NodeType.StartsWith("ControlParameter", StringComparison.Ordinal)).ToList();
        var removed = new HashSet<GraphWire>();

        foreach (var node in parameters)
        {
            var name = node.Name;
            var closingParen = name.IndexOf(") ", StringComparison.Ordinal);
            var parameterName = closingParen >= 0 ? name[(closingParen + 2)..] : name;
            var hue = AnimGraphHues.HueOf(AnimGraphHues.AG2ValueKindOf(node.NodeType));

            foreach (var output in node.Outputs)
            {
                foreach (var wire in output.Wires)
                {
                    wire.To.Owner.AddAnnotation($"{(wire.To.Name.Length > 0 ? wire.To.Name : "Parameter")}: {parameterName}", hue);
                    removed.Add(wire);
                }
            }
        }

        document.Disconnect(removed);

        foreach (var input in removed.Select(static wire => wire.To).Distinct())
        {
            if (input.Wires.Count == 0)
            {
                document.RemoveSocket(input.Owner, input);
            }
        }

        foreach (var node in parameters)
        {
            foreach (var output in node.Outputs.ToArray())
            {
                if (output.Wires.Count == 0)
                {
                    document.RemoveSocket(node, output);
                }
            }
        }
    }
}
