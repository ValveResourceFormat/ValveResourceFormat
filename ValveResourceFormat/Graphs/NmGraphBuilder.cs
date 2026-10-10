using System.Globalization;
using System.Linq;
using ValveKeyValue;
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
        var rootNodeIdx = graphDefinition.GetInt32Property("m_nRootNodeIdx");
        var nodePaths = graphDefinition.GetArray<string>("m_nodePaths");
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

        Node CreateNode(string[] nodePaths, IReadOnlyList<KVObject> nodes, int nodeIdx)
        {
            if (createdNodes.TryGetValue(nodeIdx, out var existingNode))
            {
                return existingNode;
            }

            Node node;

            if (!IsValidNodeIndex(nodeIdx))
            {
                ProgressReporter?.Report($"Node index {nodeIdx} is out of range ({nodes.Count} nodes), adding a placeholder node.");

                node = new Node(null)
                {
                    Name = $"({nodeIdx}) {MissingNodeName}",
                    NodeType = "Missing",
                    Category = GraphHue.Red,
                };
            }
            else
            {
                node = new Node(nodes[nodeIdx])
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
            var input = parent.AddInput(parentInputName ?? childNode.Name ?? string.Empty, childNodeOutput.Hue, hub);
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
            var childNode = CreateNode(nodePaths, nodes, nodeIdx);

            // child node already exists, all we do is connect to its existing output.
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

        void AddSourceState(Node node, KVObject data, string key = "m_nSourceStateNodeIdx")
        {
            var sourceStateNodeIdx = data.GetInt32Property(key, -1);

            if (sourceStateNodeIdx != -1)
            {
                node.AddText($"State: {GetName(sourceStateNodeIdx)}");
            }
        }

        void CreateChildren(Node node, int nodeIdx)
        {
            if (node.NodeType == "Missing" || !IsValidNodeIndex(nodeIdx))
            {
                return;
            }

            var data = nodes[nodeIdx];

            if (node.NodeType == "StateMachine" && DrawStateMachines)
            {
                var children = data.GetArray("m_stateDefinitions");

                // States materialize as their own nodes so transitions render as a statechart.
                var stateGraphNodes = new List<Node>(children.Count);

                foreach (var stateDefinition in children)
                {
                    var stateNodeIdx = stateDefinition.GetInt32Property("m_nStateNodeIdx");
                    var entryConditionNodeIdx = stateDefinition.GetInt32Property("m_nEntryConditionNodeIdx"); // can be -1

                    var stateName = GetName(stateNodeIdx);
                    var input = node.AddInput(stateName, PoseHue, allowMultiple: true);

                    // The placeholder keeps the state list index-aligned with the transitions.
                    if (!IsValidNodeIndex(stateNodeIdx))
                    {
                        var missingState = CreateNode(nodePaths, nodes, stateNodeIdx);
                        document.Connect(missingState.GetOrAddOutput(string.Empty, PoseHue), input);
                        stateGraphNodes.Add(missingState);
                        continue;
                    }

                    var stateNode = nodes[stateNodeIdx];
                    var stateInputIdx = stateNode.GetInt32Property("m_nChildNodeIdx");

                    var stateGraphNode = document.AddNode(new Node(stateNode)
                    {
                        Name = $"({stateNodeIdx}) {stateName}",
                        NodeType = "State",
                        Category = GraphHue.Slate,
                    });
                    document.Connect(stateGraphNode.AddOutput(string.Empty, PoseHue), input);
                    stateGraphNodes.Add(stateGraphNode);

                    if (stateInputIdx != -1)
                    {
                        var (_, stateNodeOut) = CreateChild(stateInputIdx);
                        document.Connect(stateNodeOut, stateGraphNode.AddInput(string.Empty, PoseHue, allowMultiple: true));
                    }

                    if (entryConditionNodeIdx != -1)
                    {
                        var (_, childOutput) = CreateChild(entryConditionNodeIdx, stateName);
                        document.Connect(childOutput, stateGraphNode.AddInput("Entry condition", childOutput.Hue, allowMultiple: true));
                    }

                    // A state inside a state machine layer can carry the bone mask that layer
                    // blends with; the other per-state layer fields ship unused.
                    var layerBoneMaskNodeIdx = stateNode.GetInt32Property("m_nLayerBoneMaskNodeIdx", -1);

                    if (layerBoneMaskNodeIdx != -1)
                    {
                        var (_, maskOutput) = CreateChild(layerBoneMaskNodeIdx, stateName);
                        document.Connect(maskOutput, stateGraphNode.AddInput("Layer bone mask", maskOutput.Hue, allowMultiple: true));
                    }
                }

                // Dashed state-to-state transition wires labeled with their condition.
                for (var stateIndex = 0; stateIndex < children.Count; stateIndex++)
                {
                    var transitions = children[stateIndex].GetArray("m_transitionDefinitions");

                    if (transitions == null)
                    {
                        continue;
                    }

                    var source = stateGraphNodes[stateIndex];

                    foreach (var transition in transitions)
                    {
                        var targetStateIdx = transition.GetInt32Property("m_nTargetStateIdx");

                        if (targetStateIdx < 0 || targetStateIdx >= stateGraphNodes.Count)
                        {
                            continue;
                        }

                        var conditionNodeIdx = transition.GetInt32Property("m_nConditionNodeIdx");
                        var label = conditionNodeIdx != -1 ? GetName(conditionNodeIdx) : null;

                        AnimGraphHues.ConnectTransition(document, source, stateGraphNodes[targetStateIdx], label);
                    }
                }
            }
            else if (node.NodeType == "StateMachine")
            {
                // Flattened form: each state is an input socket on the state machine node,
                // with no separate state nodes and no transition wires.
                var children = data.GetArray("m_stateDefinitions");

                foreach (var stateDefinition in children)
                {
                    var stateNodeIdx = stateDefinition.GetInt32Property("m_nStateNodeIdx");
                    var entryConditionNodeIdx = stateDefinition.GetInt32Property("m_nEntryConditionNodeIdx"); // can be -1

                    var stateName = GetName(stateNodeIdx);
                    var input = node.AddInput(stateName, PoseHue, allowMultiple: true);

                    if (!IsValidNodeIndex(stateNodeIdx))
                    {
                        var missingState = CreateNode(nodePaths, nodes, stateNodeIdx);
                        document.Connect(missingState.GetOrAddOutput(string.Empty, PoseHue), input);
                        continue;
                    }

                    var stateNode = nodes[stateNodeIdx];
                    var stateInputIdx = stateNode.GetInt32Property("m_nChildNodeIdx");

                    if (stateInputIdx != -1)
                    {
                        var (_, stateNodeOut) = CreateChild(stateInputIdx);
                        document.Connect(stateNodeOut, input);
                    }

                    if (entryConditionNodeIdx != -1)
                    {
                        var (_, childOutput) = CreateChild(entryConditionNodeIdx, stateName);
                        document.Connect(childOutput, node.AddInput("Entry condition", childOutput.Hue, allowMultiple: true));
                    }

                    var layerBoneMaskNodeIdx = stateNode.GetInt32Property("m_nLayerBoneMaskNodeIdx", -1);

                    if (layerBoneMaskNodeIdx != -1)
                    {
                        var (_, maskOutput) = CreateChild(layerBoneMaskNodeIdx, stateName);
                        document.Connect(maskOutput, node.AddInput("Layer bone mask", maskOutput.Hue, allowMultiple: true));
                    }
                }
            }
            else if (node.NodeType is "ParameterizedSelector" or "ParameterizedClipSelector")
            {
                var options = data.GetArray<int>("m_optionNodeIndices");

                var parameterNodeIdx = data.GetInt32Property("m_parameterNodeIdx");
                CreateInputAndChild(node, parameterNodeIdx);

                var hasWeightsSet = data.GetBooleanProperty("m_bHasWeightsSet");
                var totalWeight = 0;
                var weights = data.GetArray<uint>("m_optionWeights");

                if (hasWeightsSet)
                {
                    totalWeight = Math.Max(1, weights.Sum(w => (int)w));
                }

                var i = 0;
                foreach (var optionNodeIdx in options)
                {
                    var weightDesc = string.Empty;
                    if (hasWeightsSet)
                    {
                        var weight = weights[i];
                        var weightPercentage = weight / (float)totalWeight * 100;

                        weightDesc = string.Create(CultureInfo.InvariantCulture, $"Weight: {weight} ({weightPercentage:F2}%)");
                    }

                    CreateInputAndChild(node, optionNodeIdx, $"Option {++i} {weightDesc}");
                }
            }
            else if (node.NodeType is "IDBasedSelector" or "IDBasedClipSelector")
            {
                AddOptionalInput(node, data.GetInt32Property("m_nParameterNodeIdx", -1), "Parameter");
                AddOptionalInput(node, data.GetInt32Property("m_nFallbackNodeIdx", -1), "Fallback");

                // The option a given ID selects is positional, so the label carries the ID it matches.
                var options = data.GetArray<int>("m_optionNodeIndices");
                var optionIds = data.GetArray<string>("m_optionIDs");

                for (var i = 0; i < options.Length; i++)
                {
                    var label = optionIds != null && i < optionIds.Length ? optionIds[i] : $"Option {i + 1}";
                    CreateInputAndChild(node, options[i], label);
                }
            }
            else if (node.NodeType is "TargetSelector")
            {
                AddOptionalInput(node, data.GetInt32Property("m_parameterNodeIdx", -1), "Target");

                foreach (var optionNodeIdx in data.GetArray<int>("m_optionNodeIndices") ?? [])
                {
                    CreateInputAndChild(node, optionNodeIdx);
                }

                node.AddText(string.Create(CultureInfo.InvariantCulture, $"Orientation Weight: {data.GetFloatProperty("m_flOrientationScoreWeight"):F2}"));
                node.AddText(string.Create(CultureInfo.InvariantCulture, $"Position Weight: {data.GetFloatProperty("m_flPositionScoreWeight"):F2}"));
                node.AddText($"Worldspace: {data.GetBooleanProperty("m_bIsWorldSpaceTarget")}");
            }
            else if (node.NodeType is "FloatSwitch" or "IDSwitch" or "BoneMaskSwitch")
            {
                AddOptionalInput(node, data.GetInt32Property("m_nSwitchValueNodeIdx", -1), "Switch");

                // A branch with no wired node is a constant, so show that constant in its place.
                var trueNodeIdx = data.GetInt32Property("m_nTrueValueNodeIdx", -1);
                var falseNodeIdx = data.GetInt32Property("m_nFalseValueNodeIdx", -1);

                AddOptionalInput(node, trueNodeIdx, "True");
                AddOptionalInput(node, falseNodeIdx, "False");

                if (node.NodeType is "FloatSwitch")
                {
                    if (trueNodeIdx == -1) { node.AddText(string.Create(CultureInfo.InvariantCulture, $"True: {data.GetFloatProperty("m_flTrueValue"):f}")); }
                    if (falseNodeIdx == -1) { node.AddText(string.Create(CultureInfo.InvariantCulture, $"False: {data.GetFloatProperty("m_flFalseValue"):f}")); }
                }
                else if (node.NodeType is "IDSwitch")
                {
                    if (trueNodeIdx == -1) { node.AddText($"True: '{data.GetStringProperty("m_trueValue")}'"); }
                    if (falseNodeIdx == -1) { node.AddText($"False: '{data.GetStringProperty("m_falseValue")}'"); }
                }
                else
                {
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Blend Time: {data.GetFloatProperty("m_flBlendTimeSeconds"):F2}"));
                    node.AddText($"Switch Dynamically: {data.GetBooleanProperty("m_bSwitchDynamically")}");
                }
            }
            else if (node.NodeType is "BoneMaskSelector")
            {
                AddOptionalInput(node, data.GetInt32Property("m_parameterValueNodeIdx", -1), "Parameter");
                AddOptionalInput(node, data.GetInt32Property("m_defaultMaskNodeIdx", -1), "Default");

                var maskNodeIndices = data.GetArray<int>("m_maskNodeIndices") ?? [];
                var parameterValues = data.GetArray<string>("m_parameterValues") ?? [];

                for (var i = 0; i < maskNodeIndices.Length; i++)
                {
                    var label = i < parameterValues.Length ? parameterValues[i] : $"Mask {i + 1}";
                    CreateInputAndChild(node, maskNodeIndices[i], label);
                }

                node.AddText(string.Create(CultureInfo.InvariantCulture, $"Blend Time: {data.GetFloatProperty("m_flBlendTimeSeconds"):F2}"));
                node.AddText($"Switch Dynamically: {data.GetBooleanProperty("m_bSwitchDynamically")}");
            }
            else if (node.NodeType is "BoneMaskBlend")
            {
                AddOptionalInput(node, data.GetInt32Property("m_nSourceMaskNodeIdx", -1), "Source");
                AddOptionalInput(node, data.GetInt32Property("m_nTargetMaskNodeIdx", -1), "Target");
                AddOptionalInput(node, data.GetInt32Property("m_nBlendWeightValueNodeIdx", -1), "Blend Weight");
            }
            else if (node.NodeType is "OrientationWarp" or "TargetWarp")
            {
                AddOptionalInput(node, data.GetInt32Property("m_nClipReferenceNodeIdx", -1), "Clip");
                AddOptionalInput(node, data.GetInt32Property("m_nTargetValueNodeIdx", -1), "Target");

                if (node.NodeType is "OrientationWarp")
                {
                    node.AddText($"Offset: {data.GetBooleanProperty("m_bIsOffsetNode")}");
                    node.AddText($"Relative To Character: {data.GetBooleanProperty("m_bIsOffsetRelativeToCharacter")}");
                    node.AddText($"Warp Translation: {data.GetBooleanProperty("m_bWarpTranslation")}");
                    node.AddText($"Alignment: {data.GetStringProperty("m_alignmentMode")}");
                }
                else
                {
                    node.AddText($"Update Rule: {data.GetStringProperty("m_targetUpdateRule")}");
                    node.AddText($"Align At Last Warp Event: {data.GetBooleanProperty("m_bAlignWithTargetAtLastWarpEvent")}");
                }

                node.AddText($"Sampling: {data.GetStringProperty("m_samplingMode")}");
            }
            else if (node.NodeType is "Selector" or "ClipSelector")
            {
                // Select the first option for which the condition passes?
                var options = data.GetArray<int>("m_optionNodeIndices");
                var conditions = data.GetArray<int>("m_conditionNodeIndices");

                foreach (var (optionNodeIdx, conditionNodeIdx) in options.Zip(conditions))
                {
                    var (_, optionInput) = CreateInputAndChild(node, optionNodeIdx, hub: true);
                    var (_, conditionOutput) = CreateChild(conditionNodeIdx);
                    document.Connect(conditionOutput, optionInput);
                }
            }
            else if (node.NodeType is "FloatSelector" or "IDSelector")
            {
                // The first condition that passes picks the value at its position.
                var conditions = data.GetArray<int>("m_conditionNodeIndices") ?? [];
                var values = node.NodeType is "FloatSelector"
                    ? (data.GetFloatArray("m_values") ?? []).Select(static value => value.ToString("F2", CultureInfo.InvariantCulture)).ToArray()
                    : data.GetArray<string>("m_values") ?? [];

                for (var i = 0; i < conditions.Length; i++)
                {
                    CreateInputAndChild(node, conditions[i], i < values.Length ? $"= {values[i]}" : null);
                }

                node.AddText(node.NodeType is "FloatSelector"
                    ? string.Create(CultureInfo.InvariantCulture, $"Default: {data.GetFloatProperty("m_flDefaultValue"):F2}")
                    : $"Default: '{data.GetStringProperty("m_defaultValue")}'");

                if (node.NodeType is "FloatSelector")
                {
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Ease: {data.GetStringProperty("m_easingOp")} {data.GetFloatProperty("m_flEaseTime"):F2}s"));
                }
            }
            else if (node.NodeType is "LayerBlend")
            {
                var baseNodeIdx = data.GetInt32Property("m_nBaseNodeIdx");
                CreateInputAndChild(node, baseNodeIdx, "Base", "Result");

                var layerInput = node.AddInput("Layers", PoseHue, allowMultiple: true);

                var layerDefinition = data.GetArray("m_layerDefinition");
                var layerIndex = 0;
                foreach (var layer in layerDefinition)
                {
                    var layerNode = document.AddNode(new Node(layerDefinition[layerIndex])
                    {
                        Name = $"Layer{layerIndex}",
                        NodeType = LayerDefinitionType,
                        Category = AnimGraphHues.HueOf(AnimGraphHues.CategoryOfAG2(LayerDefinitionType)),
                    });

                    var layerOutput = layerNode.AddOutput(string.Empty, PoseHue);
                    document.Connect(layerOutput, layerInput);
                    CreateInputAndChild(layerNode, layer.GetInt32Property("m_nInputNodeIdx"));

                    AddOptionalInput(layerNode, layer.GetInt32Property("m_nWeightValueNodeIdx"), "Weight");
                    AddOptionalInput(layerNode, layer.GetInt32Property("m_nBoneMaskValueNodeIdx"), "Bone Mask");
                    AddOptionalInput(layerNode, layer.GetInt32Property("m_nRootMotionWeightValueNodeIdx"), "Root Motion");

                    layerNode.AddText($"Is Synchronized: {layer.GetBooleanProperty("m_bIsSynchronized")}");
                    layerNode.AddText($"Ignore Events: {layer.GetBooleanProperty("m_bIgnoreEvents")}");
                    layerNode.AddText($"Is State Machine Layer: {layer.GetBooleanProperty("m_bIsStateMachineLayer")}");
                    layerNode.AddText($"Blend Mode: {layer.GetStringProperty("m_blendMode")}");
                    layerIndex++;
                }
            }
            else if (node.NodeType is "Blend1D" or "Blend2D" or "VelocityBlend" or "ParameterizedBlend")
            {
                var sourceNodeIndices = data.GetArray<int>("m_sourceNodeIndices");

                if (node.NodeType == "Blend2D")
                {
                    CreateInputAndChild(node, data.GetInt32Property("m_nInputParameterNodeIdx0"), "Parameter A");
                    CreateInputAndChild(node, data.GetInt32Property("m_nInputParameterNodeIdx1"), "Parameter B");
                }
                else
                {
                    CreateInputAndChild(node, data.GetInt32Property("m_nInputParameterValueNodeIdx"), "Parameter");
                }

                var optionIndex = 0;
                foreach (var sourceNodeIdx in sourceNodeIndices)
                {
                    CreateInputAndChild(node, sourceNodeIdx, $"Option {++optionIndex}");
                }

                node.AddText($"Allow Looping: {data.GetBooleanProperty("m_bAllowLooping")}");
            }
            else if (node.NodeType is "BoneMask")
            {
                node.AddText(data.GetStringProperty("m_boneMaskID"));
            }
            else if (node.NodeType is "FixedWeightBoneMask")
            {
                node.AddText(string.Create(CultureInfo.InvariantCulture, $"Weight: {data.GetFloatProperty("m_flBoneWeight"):F2}"));
            }
            else if (node.NodeType.StartsWith("Cached", StringComparison.Ordinal))
            {
                CreateInputAndChild(node, data.GetInt32Property("m_nInputValueNodeIdx"), "Input");
                node.AddText($"Mode: {data.GetStringProperty("m_mode")}");
            }
            else if (node.NodeType is "ConstTarget")
            {
                var value = data.GetSubCollection("m_value");
                var boneId = value.GetStringProperty("m_boneID");
                var isBoneTarget = value.GetBooleanProperty("m_bIsBoneTarget");
                var isUsingBoneSpaceOffsets = value.GetBooleanProperty("m_bIsUsingBoneSpaceOffsets");
                var hasOffsets = value.GetBooleanProperty("m_bHasOffsets");
                var isSet = value.GetBooleanProperty("m_bIsSet");

                node.AddText($"Bone: {boneId}");
                node.AddText($"Is Bone Target: {isBoneTarget}");
                node.AddText($"Bone Space Offsets: {isUsingBoneSpaceOffsets}");
                node.AddText($"Has Offsets: {hasOffsets}");
                node.AddText($"Is Set: {isSet}");
            }
            else if (node.NodeType is "ConstFloat")
            {
                node.AddText(string.Create(CultureInfo.InvariantCulture, $"{data.GetFloatProperty("m_flValue"):F2}"));
            }
            else if (node.NodeType is "ConstBool")
            {
                node.AddText($"{data.GetBooleanProperty("m_bValue")}");
            }
            else if (node.NodeType is "ConstID")
            {
                node.AddText($"'{data.GetStringProperty("m_value")}'");
            }
            else if (node.NodeType is "ConstVector")
            {
                node.AddText(Node.StringifyValue(data["m_value"]));
            }
            else if (node.NodeType is "SpeedScale" or "DurationScale" or "VelocityBasedSpeedScale")
            {
                CreateInputAndChild(node, data.GetInt32Property("m_nChildNodeIdx"), "Input");

                var valueName = node.NodeType switch
                {
                    "DurationScale" => "Duration",
                    "VelocityBasedSpeedScale" => "Velocity",
                    _ => "Scale Value",
                };

                AddOptionalInput(node, data.GetInt32Property("m_nInputValueNodeIdx", -1), valueName);
                node.AddText(string.Create(CultureInfo.InvariantCulture, $"Default {valueName}: {data.GetFloatProperty("m_flDefaultInputValue")}"));
            }
            else if (node.NodeType is "Not" or "FloatCurve" or "FloatClamp" or "FloatAngleMath" or "IsTargetSet"
                or "VectorNegate" or "VectorInfo" or "TargetInfo" or "TargetPoint")
            {
                CreateInputAndChild(node, data.GetInt32Property("m_nInputValueNodeIdx"), "Value");

                if (node.NodeType is "FloatClamp")
                {
                    var range = data.GetSubCollection("m_clampRange");
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"{range.GetFloatProperty("m_flMin"):F2} - {range.GetFloatProperty("m_flMax"):F2}"));
                }
                else if (node.NodeType is "FloatAngleMath")
                {
                    node.AddText(data.GetStringProperty("m_operation"));
                }
                else if (node.NodeType is "VectorInfo")
                {
                    node.AddText(data.GetStringProperty("m_desiredInfo"));
                }
                else if (node.NodeType is "TargetInfo")
                {
                    node.AddText(data.GetStringProperty("m_infoType"));
                }

                if (node.NodeType is "TargetInfo" or "TargetPoint")
                {
                    node.AddText($"Worldspace: {data.GetBooleanProperty("m_bIsWorldSpaceTarget")}");
                }
            }
            else if (node.NodeType is "TargetOffset")
            {
                CreateInputAndChild(node, data.GetInt32Property("m_nInputValueNodeIdx"), "Target");
                node.AddText($"Bone Space: {data.GetBooleanProperty("m_bIsBoneSpaceOffset")}");
                node.AddText($"Rotation: {Node.StringifyValue(data["m_rotationOffset"])}");
                node.AddText($"Translation: {Node.StringifyValue(data["m_translationOffset"])}");
            }
            else if (node.NodeType is "VectorCreate")
            {
                AddOptionalInput(node, data.GetInt32Property("m_inputVectorValueNodeIdx", -1), "Vector");
                AddOptionalInput(node, data.GetInt32Property("m_inputValueXNodeIdx", -1), "X");
                AddOptionalInput(node, data.GetInt32Property("m_inputValueYNodeIdx", -1), "Y");
                AddOptionalInput(node, data.GetInt32Property("m_inputValueZNodeIdx", -1), "Z");
            }
            else if (node.NodeType is "FloatEase" or "FloatSpring")
            {
                CreateInputAndChild(node, data.GetInt32Property("m_nInputValueNodeIdx"), "Value");

                if (node.NodeType is "FloatEase")
                {
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Ease: {data.GetStringProperty("m_easingOp")} {data.GetFloatProperty("m_flEaseTime"):F2}s"));
                }
                else
                {
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Frequency: {data.GetFloatProperty("m_flHertz"):F2} Hz"));
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Damping Ratio: {data.GetFloatProperty("m_flDampingRatio"):F2}"));
                }

                if (data.GetBooleanProperty("m_bUseStartValue"))
                {
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Start Value: {data.GetFloatProperty("m_flStartValue"):F2}"));
                }
            }
            else if (node.NodeType is "IDToFloat")
            {
                CreateInputAndChild(node, data.GetInt32Property("m_nInputValueNodeIdx"), "ID");

                var ids = data.GetArray<string>("m_IDs") ?? [];
                var values = data.GetFloatArray("m_values") ?? [];

                for (var i = 0; i < ids.Length && i < values.Length; i++)
                {
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"'{ids[i]}': {values[i]:F2}"));
                }

                node.AddText(string.Create(CultureInfo.InvariantCulture, $"Default: {data.GetFloatProperty("m_defaultValue"):F2}"));
            }
            else if (node.NodeType is "FloatRemap")
            {
                CreateInputAndChild(node, data.GetInt32Property("m_nInputValueNodeIdx"), "Value");
                var inputRange = data.GetSubCollection("m_inputRange");
                var outputRange = data.GetSubCollection("m_outputRange");
                node.AddText(string.Create(CultureInfo.InvariantCulture, $"InputBegin: {inputRange.GetFloatProperty("m_flBegin")} InputEnd: {inputRange.GetFloatProperty("m_flEnd")}"));
                node.AddText(string.Create(CultureInfo.InvariantCulture, $"OutputBegin: {outputRange.GetFloatProperty("m_flBegin")} OutputEnd: {outputRange.GetFloatProperty("m_flEnd")}"));
            }
            else if (node.NodeType is "FloatCurveEvent")
            {
                AddOptionalInput(node, data.GetInt32Property("m_nDefaultNodeIdx", -1), "Default");
                node.AddText($"Event: '{data.GetStringProperty("m_eventID")}'");
            }
            else if (node.NodeType is "IDEventCondition")
            {
                AddSourceState(node, data);

                foreach (var eventId in data.GetArray<string>("m_eventIDs") ?? [])
                {
                    node.AddText($"Event: '{eventId}'");
                }
            }
            else if (node.NodeType is "GraphEventCondition")
            {
                AddSourceState(node, data);

                foreach (var condition in data.GetArray("m_conditions") ?? [])
                {
                    node.AddText($"Event: '{condition.GetStringProperty("m_eventID")}' ({condition.GetStringProperty("m_eventTypeCondition")})");
                }
            }
            else if (node.NodeType is "IDEvent" or "IDEventPercentageThrough")
            {
                AddSourceState(node, data);
                node.AddText(node.NodeType is "IDEvent"
                    ? $"Default: '{data.GetStringProperty("m_defaultValue")}'"
                    : $"Event: '{data.GetStringProperty("m_eventID")}'");
            }
            else if (node.NodeType is "FootEventCondition" or "FootstepEventID" or "FootstepEventPercentageThrough")
            {
                AddSourceState(node, data);

                if (data.ContainsKey("m_phaseCondition"))
                {
                    node.AddText($"Phase: {data.GetStringProperty("m_phaseCondition")}");
                }
            }
            else if (node.NodeType is "TransitionEventCondition")
            {
                AddSourceState(node, data);
                node.AddText($"Rule: {data.GetStringProperty("m_ruleCondition")}");

                var requiredRuleId = data.GetStringProperty("m_requireRuleID");

                if (!string.IsNullOrEmpty(requiredRuleId))
                {
                    node.AddText($"Rule ID: '{requiredRuleId}'");
                }
            }
            else if (node.NodeType is "SyncEventIndexCondition")
            {
                AddSourceState(node, data);
                node.AddText($"{data.GetStringProperty("m_triggerMode")}: {data.GetInt32Property("m_syncEventIdx")}");
            }
            else if (node.NodeType is "CurrentSyncEvent" or "CurrentSyncEventID")
            {
                AddSourceState(node, data);

                if (data.ContainsKey("m_infoType"))
                {
                    node.AddText(data.GetStringProperty("m_infoType"));
                }
            }
            else if (node.NodeType is "StateCompletedCondition")
            {
                AddSourceState(node, data);
                AddOptionalInput(node, data.GetInt32Property("m_nTransitionDurationOverrideNodeIdx", -1), "Duration Override");
                node.AddText(string.Create(CultureInfo.InvariantCulture, $"Transition Duration: {data.GetFloatProperty("m_flTransitionDurationSeconds"):F2}"));
            }
            else if (node.NodeType is "TimeCondition")
            {
                AddSourceState(node, data, "m_sourceStateNodeIdx");
                AddOptionalInput(node, data.GetInt32Property("m_nInputValueNodeIdx", -1), "Time Value");
                node.AddText(string.Create(CultureInfo.InvariantCulture, $"{data.GetStringProperty("m_type")} {data.GetStringProperty("m_operator")} {data.GetFloatProperty("m_flComparand"):F2}"));
            }
            else if (node.NodeType is "IsExternalGraphSlotFilled" or "IsExternalPoseSet")
            {
                var key = node.NodeType is "IsExternalGraphSlotFilled" ? "m_nExternalGraphNodeIdx" : "m_nExternalPoseNodeIdx";
                node.AddText($"Slot: {GetName(data.GetInt32Property(key, -1))}");
            }
            else if (node.NodeType is "FloatMath")
            {
                CreateInputAndChild(node, data.GetInt32Property("m_nInputValueNodeIdxA", -1), "A");

                var @operator = data.GetStringProperty("m_operator");
                node.AddText(@operator);

                var inputNodeIdxB = data.GetInt32Property("m_nInputValueNodeIdxB", -1);

                if (inputNodeIdxB != -1)
                {
                    CreateInputAndChild(node, inputNodeIdxB, "B");
                }
                else
                {
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"{data.GetFloatProperty("m_flValueB"):f}"));
                }
            }
            else if (node.NodeType.EndsWith("Comparison", StringComparison.Ordinal))
            {
                var childNodeIdx = data.GetInt32Property("m_nInputValueNodeIdx");
                CreateInputAndChild(node, childNodeIdx, GetName(childNodeIdx));

                if (data.ContainsKey("m_comparison"))
                {
                    var comparison = data.GetStringProperty("m_comparison");
                    node.AddText(comparison);
                }

                if (node.NodeType is "IDComparison")
                {
                    var comparisonIds = data.GetArray<string>("m_comparisionIDs");
                    foreach (var comparisonId in comparisonIds)
                    {
                        node.AddText($"'{comparisonId}'");
                    }
                }
                else if (node.NodeType is "FloatComparison" or "IntComparison")
                {
                    var comparandNodeIdx = data.GetInt32Property("m_nComparandValueNodeIdx");
                    if (comparandNodeIdx != -1)
                    {
                        CreateInputAndChild(node, comparandNodeIdx, "Comparand");
                    }
                    else
                    {
                        node.AddText(string.Create(CultureInfo.InvariantCulture, $"{data.GetFloatProperty("m_flComparisonValue"):f}"));
                    }
                }
                else if (node.NodeType is "FloatRangeComparison")
                {
                    var range = data.GetSubCollection("m_range");
                    var inclusive = data.GetBooleanProperty("m_bIsInclusiveCheck");

                    node.AddText(inclusive
                        ? string.Create(CultureInfo.InvariantCulture, $"{range.GetFloatProperty("m_flMin"):f} <= x <= {range.GetFloatProperty("m_flMax"):f}")
                        : string.Create(CultureInfo.InvariantCulture, $"{range.GetFloatProperty("m_flMin"):f} < x < {range.GetFloatProperty("m_flMax"):f}"));
                }
                else
                {
                    ProgressReporter?.Report($"Generic handled node: {node.NodeType} ({node.Name})");
                }
            }
            else if (node.NodeType is "And" or "Or")
            {
                foreach (var condition in data.GetArray<int>("m_conditionNodeIndices"))
                {
                    CreateInputAndChild(node, condition);
                }
            }
            else if (node.Data?.ContainsKey("m_nChildNodeIdx") ?? false)
            {
                if (node.NodeType == "Scale")
                {
                    CreateInputAndChild(node, data.GetInt32Property("m_nMaskNodeIdx"), "Mask");
                    CreateInputAndChild(node, data.GetInt32Property("m_nEnableNodeIdx"), "Enable");
                }
                else if (node.NodeType == "TwoBoneIK")
                {
                    node.AddText($"Bone: {data.GetStringProperty("m_effectorBoneID")}");
                    CreateInputAndChild(node, data.GetInt32Property("m_nEffectorTargetNodeIdx"), "Effector");
                    var enabledNodeIdx = data.GetInt32Property("m_nEnabledNodeIdx");
                    if (enabledNodeIdx != -1)
                    {
                        CreateInputAndChild(node, enabledNodeIdx, "Enabled");
                    }
                    else
                    {
                        node.AddText("Enabled: true");
                    }
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Blend Time: {data.GetFloatProperty("m_flBlendTimeSeconds"):f}"));
                    node.AddText($"Blend Mode: {data.GetStringProperty("m_blendMode")}");
                    node.AddText($"Worldspace: {data.GetBooleanProperty("m_bIsTargetInWorldSpace")}");
                }
                else if (node.NodeType == "FootIK")
                {
                    node.AddText($"Left Effector: {data.GetStringProperty("m_leftEffectorBoneID")}");
                    node.AddText($"Right Effector: {data.GetStringProperty("m_rightEffectorBoneID")}");

                    CreateInputAndChild(node, data.GetInt32Property("m_nLeftTargetNodeIdx"), "Left Target");
                    CreateInputAndChild(node, data.GetInt32Property("m_nRightTargetNodeIdx"), "Right Target");
                    AddOptionalInput(node, data.GetInt32Property("m_nEnabledNodeIdx", -1), "Enabled");

                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Blend Time: {data.GetFloatProperty("m_flBlendTimeSeconds"):F2}"));
                    node.AddText($"Blend Mode: {data.GetStringProperty("m_blendMode")}");
                    node.AddText($"Worldspace: {data.GetBooleanProperty("m_bIsTargetInWorldSpace")}");
                }
                else if (node.NodeType == "ChainLookat")
                {
                    node.AddText($"End Effector: {data.GetStringProperty("m_endEffectorBoneID")}");
                    node.AddText($"Chain Length: {data.GetInt32Property("m_nChainLength")}");

                    AddOptionalInput(node, data.GetInt32Property("m_nLookatTargetNodeIdx", -1), "Lookat Target");
                    AddOptionalInput(node, data.GetInt32Property("m_nEnabledNodeIdx", -1), "Enabled");

                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Blend Time: {data.GetFloatProperty("m_flBlendTimeSeconds"):F2}"));
                    node.AddText($"Worldspace: {data.GetBooleanProperty("m_bIsTargetInWorldSpace")}");
                }
                else if (node.NodeType == "FollowBone")
                {
                    node.AddText($"Bone: {data.GetStringProperty("m_bone")}");
                    node.AddText($"Follow: {data.GetStringProperty("m_followTargetBone")}");
                    node.AddText($"Mode: {data.GetStringProperty("m_mode")}");

                    AddOptionalInput(node, data.GetInt32Property("m_nEnabledNodeIdx", -1), "Enabled");
                }
                else if (node.NodeType == "RootMotionOverride")
                {
                    AddOptionalInput(node, data.GetInt32Property("m_desiredMovingVelocityNodeIdx", -1), "Moving Velocity");
                    AddOptionalInput(node, data.GetInt32Property("m_desiredFacingDirectionNodeIdx", -1), "Facing Direction");
                    AddOptionalInput(node, data.GetInt32Property("m_linearVelocityLimitNodeIdx", -1), "Linear Velocity Limit");
                    AddOptionalInput(node, data.GetInt32Property("m_angularVelocityLimitNodeIdx", -1), "Angular Velocity Limit");
                    AddOptionalInput(node, data.GetInt32Property("m_enabledNodeIdx", -1), "Enabled");
                }
                else if (node.NodeType == "BodyGroup")
                {
                    AddOptionalInput(node, data.GetInt32Property("m_nEnabledNodeIdx", -1), "Enabled");
                }
                else if (node.NodeType is "AimCS")
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

                var childNodeIdx = data.GetInt32Property("m_nChildNodeIdx");
                CreateInputAndChild(node, childNodeIdx, "Input", "Result");
            }
            else if (node.NodeType is "Clip" or "TimeControlledClip" or "AnimationPose" or "ReferencedGraph")
            {
                var resources = graphDefinition.GetArray<string>("m_resources");

                var referencedGraphIdx = data.GetInt32Property("m_nReferencedGraphIdx");
                var referencedGraphSlots = graphDefinition.GetArray("m_referencedGraphSlots");
                var dataSlotIdx = data.GetInt32Property("m_nDataSlotIdx");

                if (node.NodeType is "ReferencedGraph")
                {
                    dataSlotIdx = referencedGraphSlots[referencedGraphIdx].GetInt32Property("m_dataSlotIdx");

                    var fallbackNodeIdx = data.GetInt32Property("m_nFallbackNodeIdx");
                    if (fallbackNodeIdx != -1)
                    {
                        node.AddSpace();
                        CreateInputAndChild(node, fallbackNodeIdx, "Fallback");
                    }
                }
                else if (node.NodeType is "Clip")
                {
                    node.AddSpace();
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Speed: {data.GetFloatProperty("m_flSpeedMultiplier"):F2}x"));
                    node.AddText($"StartSyncEvent Offset: {data.GetInt32Property("m_nStartSyncEventOffset")}");
                    node.AddText($"Sample RootMotion: {data.GetBooleanProperty("m_bSampleRootMotion")}");
                    node.AddText($"Allow Looping: {data.GetBooleanProperty("m_bAllowLooping")}");

                    AddOptionalInput(node, data.GetInt32Property("m_nPlayInReverseValueNodeIdx"), "Play in reverse");
                    AddOptionalInput(node, data.GetInt32Property("m_nResetTimeValueNodeIdx"), "Reset time");
                }
                else if (node.NodeType is "TimeControlledClip")
                {
                    node.AddSpace();
                    node.AddText($"Sample RootMotion: {data.GetBooleanProperty("m_bSampleRootMotion")}");

                    AddOptionalInput(node, data.GetInt32Property("m_nTimeValueNodeIdx", -1), "Time");
                    AddOptionalInput(node, data.GetInt32Property("m_nPlayInReverseValueNodeIdx", -1), "Play in reverse");
                }
                else if (node.NodeType is "AnimationPose")
                {
                    node.AddSpace();

                    AddOptionalInput(node, data.GetInt32Property("m_nPoseTimeValueNodeIdx"), "Time");

                    var timeRemapRange = data.GetSubCollection("m_inputTimeRemapRange");
                    var remapMin = timeRemapRange.GetFloatProperty("m_flMin");
                    var remapMax = timeRemapRange.GetFloatProperty("m_flMax");
                    var remapMinDesc = remapMin == float.MaxValue ? "None" : string.Create(CultureInfo.InvariantCulture, $"{remapMin:f}");
                    var remapMaxDesc = remapMax == float.MinValue ? "None" : string.Create(CultureInfo.InvariantCulture, $"{remapMax:f}");

                    node.AddText($"Remap: {remapMinDesc} - {remapMaxDesc}");
                    node.AddText(string.Create(CultureInfo.InvariantCulture, $"Const Time: {data.GetFloatProperty("m_flUserSpecifiedTime"):f}"));
                    node.AddText($"Use frames: {data.GetBooleanProperty("m_bUseFramesAsInput")}");
                }

                if (dataSlotIdx != -1)
                {
                    SetResourceReference(node, resources[dataSlotIdx]);
                }
            }
            else if (node.NodeType is "ExternalPose")
            {
                node.AddText($"Sample RootMotion: {data.GetBooleanProperty("m_bShouldSampleRootMotion")}");
            }
            else if (node.NodeType.StartsWith("ControlParameter", StringComparison.Ordinal))
            {
                // Graph input value set by game code.
            }
            else if (node.NodeType is "ZeroPose" or "ReferencePose" or "IsInactiveBranchCondition")
            {
                // Empty node
            }
            else
            {
                ProgressReporter?.Report($"Unhandled node type: {node.NodeType} ({node.Name})");
            }
        }

        var finalPose = new Node(null)
        {
            Name = "Result",
            NodeType = "FinalPose",
            Category = PoseHue,
        };

        var finalPoseInput = finalPose.AddInput("Out", PoseHue, allowMultiple: false);
        document.AddNode(finalPose);

        var root = CreateNode(nodePaths, nodes, rootNodeIdx);

        var rootOutput = root.AddOutput(string.Empty, PoseHue);
        document.Connect(rootOutput, finalPoseInput);

        CreateChildren(root, rootNodeIdx);

        // create some unreferenced nodes
        for (var i = 0; i < nodes.Count; i++)
        {
            var exists = createdNodes.ContainsKey(i);
            if (exists)
            {
                continue;
            }

            var className = GetType(i);

            if (className.StartsWith("ControlParameter", StringComparison.Ordinal))
            {
                CreateNode(nodePaths, nodes, i);
                continue;
            }
        }

        if (!DrawParameterWires)
        {
            SuppressParameterWires(document);
        }

        // A pose graph is one connected DAG, where routing a long wire through dummy ranks keeps
        // it between the cards of the ranks it spans rather than over them, and where closing
        // every rank of slack pays off instead of costing the room the repair moves cards in.
        document.LayoutOptions.LongWireDummies = true;
        document.LayoutOptions.TightenMinSpan = 1;
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
        foreach (var graphNode in document.Nodes)
        {
            if (graphNode is not Node node || node.NodeType?.StartsWith("ControlParameter", StringComparison.Ordinal) != true)
            {
                continue;
            }

            var name = node.Name ?? string.Empty;
            var closingParen = name.IndexOf(") ", StringComparison.Ordinal);
            var parameterName = closingParen >= 0 ? name[(closingParen + 2)..] : name;
            var kind = AnimGraphHues.AG2ValueKindOf(node.NodeType);

            foreach (var output in node.Outputs.ToArray())
            {
                foreach (var wire in output.Wires.ToArray())
                {
                    var input = wire.To;
                    var consumer = input.Owner;
                    document.Disconnect(wire);
                    consumer.AddAnnotation($"{(input.Name.Length > 0 ? input.Name : "Parameter")}: {parameterName}", AnimGraphHues.HueOf(kind));

                    if (input.Wires.Count == 0)
                    {
                        document.RemoveSocket(consumer, input);
                    }
                }

                if (output.Wires.Count == 0)
                {
                    document.RemoveSocket(node, output);
                }
            }
        }
    }
}
