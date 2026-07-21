using System.Linq;
using GUI.Types.GLViewers;
using GUI.Types.Graphs.Core;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Serialization.KeyValues;
using Node = GUI.Types.Graphs.KVGraphNode;

namespace GUI.Types.Graphs;

internal class AnimationGraphViewer : GLGraphViewer
{
    private const GraphHue PoseHue = GraphHue.Green;
    private const GraphHue ValueHue = GraphHue.Cyan;

    private readonly KVObject graphDefinition;

    public AnimationGraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, KVObject data)
        : base(vrfGuiContext, rendererContext, new GraphView())
    {
        graphDefinition = data;

        CreateGraph();
    }

    private struct Pose;
    private struct Value;

    private static GraphHue HueOf(Type type) => type == typeof(Value) ? ValueHue : PoseHue;

    private static void SetResourceReference(GraphNode node, string resourceName)
    {
        node.ExternalResourceName = resourceName;

        var isGraphFile = resourceName.Contains(".vnmgraph", StringComparison.OrdinalIgnoreCase);
        var icon = isGraphFile ? "anmgrph" : "anim";

        var display = resourceName;
        var fileExtensionStart = display.LastIndexOf('.');
        if (fileExtensionStart >= 0)
        {
            display = display[..fileExtensionStart];
        }

        display = display.Replace(".vnmgraph", string.Empty, StringComparison.Ordinal);

        var lastSlashIndex = display.LastIndexOf('/');
        if (lastSlashIndex >= 0)
        {
            display = display[(lastSlashIndex + 1)..];
        }

        if (display.Length > 23)
        {
            display = '…' + display[^22..];
        }

        node.AddResourceRow(display, icon, PoseHue);
    }

    private void CreateGraph()
    {
        var rootNodeIdx = graphDefinition.GetInt32Property("m_nRootNodeIdx");
        var nodePaths = graphDefinition.GetArray<string>("m_nodePaths");
        var nodes = graphDefinition.GetArray("m_nodes");

        string GetName(int nodeIdx)
        {
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

            var node = new Node(nodes[nodeIdx])
            {
                Name = $"({nodeIdx}) {GetName(nodeIdx)}",
                NodeType = GetType(nodeIdx),
            };

            View.AddNode(node);
            createdNodes[nodeIdx] = node;
            return node;
        }


        (Node, GraphSocket) CreateInputAndChild<ValueType>(Node parent, int nodeIdx, string? parentInputName = null, string? childOutputName = null, bool hub = false)
            where ValueType : struct
        {
            var (childNode, childNodeOutput) = CreateChild<ValueType>(nodeIdx, childOutputName);

            var input = parent.AddInput(parentInputName ?? childNode.Name ?? string.Empty, HueOf(typeof(ValueType)), hub);
            View.Connect(childNodeOutput, input);

            return (childNode, input);
        }

        (Node, GraphSocket) CreateChild<ValueType>(int nodeIdx, string? childOutputName = null)
        {
            var childNode = CreateNode(nodePaths, nodes, nodeIdx);

            // child node already exists, all we do is connect to its existing output.
            if (childNode.Outputs.Count > 0)
            {
                return (childNode, childNode.Outputs[0]);
            }

            if (childNode.NodeType is "Clip" or "ReferencedGraph")
            {
                childOutputName = string.Empty;
            }

            var childNodeOutput = childNode.AddOutput(childOutputName ?? string.Empty, HueOf(typeof(ValueType)));

            try
            {
                CreateChildren(childNode, nodeIdx);
            }
            catch (IndexOutOfRangeException)
            {
                Log.Error(nameof(AnimationGraphViewer), $"Error creating children for {childNode.Name} (idx = {nodeIdx}).");
            }

            return (childNode, childNodeOutput);
        }

        void CreateChildren(Node node, int nodeIdx)
        {
            var data = nodes[nodeIdx];

            if (node.NodeType == "StateMachine")
            {
                var children = data.GetArray("m_stateDefinitions");

                // States materialize as their own nodes so transitions render as a statechart.
                var stateGraphNodes = new List<Node>(children.Count);

                foreach (var stateDefinition in children)
                {
                    var stateNodeIdx = stateDefinition.GetInt32Property("m_nStateNodeIdx");
                    var entryConditionNodeIdx = stateDefinition.GetInt32Property("m_nEntryConditionNodeIdx"); // can be -1

                    var stateName = GetName(stateNodeIdx);
                    var stateNode = nodes[stateNodeIdx];
                    var stateInputIdx = stateNode.GetInt32Property("m_nChildNodeIdx");

                    var input = node.AddInput(stateName, PoseHue, allowMultiple: true);

                    var stateGraphNode = View.AddNode(new Node(stateNode)
                    {
                        Name = stateName,
                        NodeType = "State",
                        Category = GraphHue.Slate,
                    });
                    View.Connect(stateGraphNode.AddOutput(string.Empty, PoseHue), input);
                    stateGraphNodes.Add(stateGraphNode);

                    if (stateInputIdx != -1)
                    {
                        var (_, stateNodeOut) = CreateChild<Pose>(stateInputIdx);
                        View.Connect(stateNodeOut, stateGraphNode.AddInput(string.Empty, PoseHue, allowMultiple: true));
                    }

                    if (entryConditionNodeIdx != -1)
                    {
                        var (_, childOutput) = CreateChild<Value>(entryConditionNodeIdx, stateName);
                        View.Connect(childOutput, stateGraphNode.AddInput("Entry condition", ValueHue, allowMultiple: true));
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

                        var target = stateGraphNodes[targetStateIdx];
                        var from = source.Outputs.Find(static o => o.Name == "Transitions") ?? source.AddOutput("Transitions", GraphHue.Slate);
                        var to = target.Inputs.Find(static i => i.Name == "From") ?? target.AddInput("From", GraphHue.Slate, allowMultiple: true);

                        if (!to.Wires.Any(w => w.From == from))
                        {
                            View.Connect(from, to, dashed: true, label: label);
                        }
                    }
                }
            }
            else if (node.NodeType is "ParameterizedSelector" or "ParameterizedClipSelector")
            {
                var options = data.GetArray<int>("m_optionNodeIndices");

                var parameterNodeIdx = data.GetInt32Property("m_parameterNodeIdx");
                CreateInputAndChild<Value>(node, parameterNodeIdx);

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
                        // If weights are set, show the weight and its percentage.
                        var weight = weights[i];
                        var weightPercentage = weight / (float)totalWeight * 100;

                        weightDesc = $"Weight: {weight} ({weightPercentage:F2}%)";
                    }

                    CreateInputAndChild<Pose>(node, optionNodeIdx, $"Option {++i} {weightDesc}");
                }
            }
            else if (node.NodeType is "Selector" or "ClipSelector")
            {
                // Select the first option for which the condition passes?
                var options = data.GetArray<int>("m_optionNodeIndices");
                var conditions = data.GetArray<int>("m_conditionNodeIndices");

                foreach (var (optionNodeIdx, conditionNodeIdx) in options.Zip(conditions))
                {
                    var (_, optionInput) = CreateInputAndChild<Pose>(node, optionNodeIdx, hub: true);
                    var (_, conditionOutput) = CreateChild<Value>(conditionNodeIdx);
                    View.Connect(conditionOutput, optionInput);
                }
            }
            else if (node.NodeType is "LayerBlend")
            {
                var baseNodeIdx = data.GetInt32Property("m_nBaseNodeIdx");
                CreateInputAndChild<Pose>(node, baseNodeIdx, "Base", "Result");

                var layerInput = node.AddInput("Layers", PoseHue, allowMultiple: true);

                var layerDefinition = data.GetArray("m_layerDefinition");
                var layerIndex = 0;
                foreach (var layer in layerDefinition)
                {
                    var layerNode = View.AddNode(new Node(layerDefinition[layerIndex])
                    {
                        Name = $"Layer{layerIndex}",
                        NodeType = "_LayerDefinition_",
                    });

                    var layerOutput = layerNode.AddOutput(string.Empty, PoseHue);
                    View.Connect(layerOutput, layerInput);
                    CreateInputAndChild<Pose>(layerNode, layer.GetInt32Property("m_nInputNodeIdx"));

                    // Optional inputs
                    var weightNodeIdx = layer.GetInt32Property("m_nWeightValueNodeIdx");
                    var boneMaskNodeIdx = layer.GetInt32Property("m_nBoneMaskValueNodeIdx");
                    var rootMotionNodeIdx = layer.GetInt32Property("m_nRootMotionWeightValueNodeIdx");

                    if (weightNodeIdx != -1)
                    {
                        CreateInputAndChild<Value>(layerNode, weightNodeIdx, parentInputName: "Weight");
                    }

                    if (boneMaskNodeIdx != -1)
                    {
                        CreateInputAndChild<Pose>(layerNode, boneMaskNodeIdx, parentInputName: "Bone Mask");
                    }

                    if (rootMotionNodeIdx != -1)
                    {
                        CreateInputAndChild<Pose>(layerNode, rootMotionNodeIdx, parentInputName: "Root Motion");
                    }

                    layerNode.AddText($"Is Synchronized: {layer.GetBooleanProperty("m_bIsSynchronized")}");
                    layerNode.AddText($"Ignore Events: {layer.GetBooleanProperty("m_bIgnoreEvents")}");
                    layerNode.AddText($"Is State Machine Layer: {layer.GetBooleanProperty("m_bIsStateMachineLayer")}");
                    layerNode.AddText($"Blend Mode: {layer.GetStringProperty("m_blendMode")}");
                    layerIndex++;
                }
            }
            else if (node.NodeType is "Blend1D" or "Blend2D")
            {
                var sourceNodeIndices = data.GetArray<int>("m_sourceNodeIndices");
                var childCount = sourceNodeIndices.Length + 2;

                if (node.NodeType == "Blend1D")
                {
                    var inputNodeIdx = data.GetInt32Property("m_nInputParameterValueNodeIdx");
                    CreateInputAndChild<Value>(node, inputNodeIdx, "Parameter");
                }
                else if (node.NodeType == "Blend2D")
                {
                    var inputNodeIdxA = data.GetInt32Property("m_nInputParameterNodeIdx0");
                    var inputNodeIdxB = data.GetInt32Property("m_nInputParameterNodeIdx1");

                    childCount += 1;
                    CreateInputAndChild<Value>(node, inputNodeIdxA, "Parameter A");
                    CreateInputAndChild<Value>(node, inputNodeIdxB, "Parameter B");
                }

                var optionIndex = 0;
                foreach (var sourceNodeIdx in sourceNodeIndices)
                {
                    CreateInputAndChild<Pose>(node, sourceNodeIdx, $"Option {++optionIndex}");
                }

                node.AddText($"Allow Looping: {data.GetBooleanProperty("m_bAllowLooping")}");
            }
            else if (node.NodeType is "BoneMask")
            {
                node.AddText(data.GetStringProperty("m_boneMaskID"));
            }
            else if (node.NodeType is "CachedFloat")
            {
                CreateInputAndChild<Value>(node, data.GetInt32Property("m_nInputValueNodeIdx"), "Input");
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
            else if (node.NodeType is "SpeedScale")
            {
                CreateInputAndChild<Pose>(node, data.GetInt32Property("m_nChildNodeIdx"), "Input");
                CreateInputAndChild<Value>(node, data.GetInt32Property("m_nInputValueNodeIdx"), "Scale Value");
                node.AddText($"Default Scale: {data.GetFloatProperty("m_flDefaultInputValue")}");
            }
            else if (node.NodeType is "Not" or "FloatCurve")
            {
                CreateInputAndChild<Value>(node, data.GetInt32Property("m_nInputValueNodeIdx"), "Value");

                // curve
            }
            else if (node.NodeType is "FloatRemap")
            {
                CreateInputAndChild<Value>(node, data.GetInt32Property("m_nInputValueNodeIdx"), "Value");
                var inputRange = data.GetSubCollection("m_inputRange");
                var outputRange = data.GetSubCollection("m_outputRange");
                node.AddText($"InputBegin: {inputRange.GetFloatProperty("m_flBegin")} InputEnd: {inputRange.GetFloatProperty("m_flEnd")}");
                node.AddText($"OutputBegin: {outputRange.GetFloatProperty("m_flBegin")} OutputEnd: {outputRange.GetFloatProperty("m_flEnd")}");
            }
            else if (node.NodeType is "IDEventCondition")
            {
                var eventIds = data.GetArray<string>("m_eventIDs");
                var sourceStateNodeIdx = data.GetInt32Property("m_nSourceStateNodeIdx");
                if (sourceStateNodeIdx != -1)
                {
                    node.AddText($"State: {GetName(sourceStateNodeIdx)}");
                }

                foreach (var eventId in eventIds)
                {
                    node.AddText($"Event: '{eventId}'");
                }
            }
            else if (node.NodeType.EndsWith("Math", StringComparison.Ordinal))
            {
                var inputNodeIdxA = data.GetInt32Property("m_nInputValueNodeIdxA", -1);
                var inputNodeIdxB = data.GetInt32Property("m_nInputValueNodeIdxB", -1);

                CreateInputAndChild<Value>(node, inputNodeIdxA, "A");

                var @operator = data.GetStringProperty("m_operator");
                node.AddText(@operator);

                if (inputNodeIdxB != -1)
                {
                    CreateInputAndChild<Value>(node, inputNodeIdxB, "B");
                }
                else
                {
                    if (node.NodeType == "FloatMath")
                    {
                        node.AddText($"{data.GetFloatProperty("m_flValueB"):f}");
                    }
                }
            }
            else if (node.NodeType.EndsWith("Comparison", StringComparison.Ordinal))
            {
                var childNodeIdx = data.GetInt32Property("m_nInputValueNodeIdx");
                CreateInputAndChild<Value>(node, childNodeIdx, GetName(childNodeIdx));

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
                        CreateInputAndChild<Value>(node, comparandNodeIdx, "Comparand");
                    }
                    else
                    {
                        node.AddText($"{data.GetFloatProperty("m_flComparisonValue"):f}");
                    }
                }
                else
                {
                    Log.Error(nameof(AnimationGraphViewer), $"Generic handled node: {node.NodeType} ({node.Name})");
                }
            }
            else if (node.Data?.ContainsKey("m_conditionNodeIndices") ?? false) // Conditional node
            {
                var conditions = data.GetArray<int>("m_conditionNodeIndices");
                foreach (var condition in conditions)
                {
                    CreateInputAndChild<Value>(node, condition);
                }
            }
            else if (node.Data?.ContainsKey("m_nChildNodeIdx") ?? false)
            {
                var childCount = 1;

                void AddMaybeOptionalInput(string name, int idx)
                {
                    if (idx != -1)
                    {
                        CreateInputAndChild<Value>(node, idx, name);
                    }
                }

                if (node.NodeType == "Scale")
                {
                    childCount = 3;
                    CreateInputAndChild<Pose>(node, data.GetInt32Property("m_nMaskNodeIdx"), "Mask");
                    CreateInputAndChild<Value>(node, data.GetInt32Property("m_nEnableNodeIdx"), "Enable");
                }
                else if (node.NodeType == "TwoBoneIK")
                {
                    childCount = 2;
                    node.AddText($"Bone: {data.GetStringProperty("m_effectorBoneID")}");
                    CreateInputAndChild<Pose>(node, data.GetInt32Property("m_nEffectorTargetNodeIdx"), "Effector");
                    var enabledNodeIdx = data.GetInt32Property("m_nEnabledNodeIdx");
                    if (enabledNodeIdx != -1)
                    {
                        CreateInputAndChild<Value>(node, enabledNodeIdx, "Enabled");
                    }
                    else
                    {
                        node.AddText("Enabled: true");
                    }
                    node.AddText($"Blend Time: {data.GetFloatProperty("m_flBlendTimeSeconds"):f}");
                    node.AddText($"Blend Mode: {data.GetStringProperty("m_blendMode")}");
                    node.AddText($"Worldspace: {data.GetBooleanProperty("m_bIsTargetInWorldSpace")}");
                }
                else if (node.NodeType == "FootIK")
                {
                    childCount = 3;

                    node.AddText($"Left Effector: {data.GetStringProperty("m_leftEffectorBoneID")}");
                    node.AddText($"Right Effector: {data.GetStringProperty("m_rightEffectorBoneID")}");

                    var leftTargetIdx = data.GetInt32Property("m_nLeftTargetNodeIdx");
                    var rightTargetIdx = data.GetInt32Property("m_nRightTargetNodeIdx");

                    CreateInputAndChild<Pose>(node, leftTargetIdx, "Left Target");
                    CreateInputAndChild<Pose>(node, rightTargetIdx, "Right Target");

                    var enabledNodeIdx = data.GetInt32Property("m_nEnabledNodeIdx", -1);
                    if (enabledNodeIdx != -1)
                    {
                        CreateInputAndChild<Value>(node, enabledNodeIdx, "Enabled");
                    }

                    node.AddText($"Blend Time: {data.GetFloatProperty("m_flBlendTimeSeconds"):F2}");
                    node.AddText($"Blend Mode: {data.GetStringProperty("m_blendMode")}");
                    node.AddText($"Worldspace: {data.GetBooleanProperty("m_bIsTargetInWorldSpace")}");
                }
                else if (node.NodeType is "AimCS")
                {
                    childCount = 8;

                    AddMaybeOptionalInput("Vertical Angle", data.GetInt32Property("m_nVerticalAngleNodeIdx"));
                    AddMaybeOptionalInput("Horizontal Angle", data.GetInt32Property("m_nHorizontalAngleNodeIdx"));
                    AddMaybeOptionalInput("Weapon Category", data.GetInt32Property("m_nWeaponCategoryNodeIdx"));
                    AddMaybeOptionalInput("Weapon Type", data.GetInt32Property("m_nWeaponTypeNodeIdx"));
                    AddMaybeOptionalInput("Is Weapon Action Active", data.GetInt32Property("m_nIsWeaponActionActiveNodeIdx"));
                    AddMaybeOptionalInput("Weapon Drop", data.GetInt32Property("m_nWeaponDropNodeIdx"));
                    AddMaybeOptionalInput("Disable Hand IK", data.GetInt32Property("m_nDisableHandIKNodeIdx"));
                    AddMaybeOptionalInput("Crouch Weight", data.GetInt32Property("m_nCrouchWeightNodeIdx"));

                    node.AddText($"Hand IK Blend In: {data.GetFloatProperty("m_flHandIKBlendInTimeSeconds"):F2}");
                    node.AddText($"Action Blend Time: {data.GetFloatProperty("m_flActionBlendTimeSeconds"):F2}");
                }
                else if (node.NodeType is "SnapWeapon")
                {
                    childCount = 4;
                    AddMaybeOptionalInput("Flashed Amount", data.GetInt32Property("m_nFlashedAmountNodeIdx"));
                    AddMaybeOptionalInput("Weapon Category", data.GetInt32Property("m_nWeaponCategoryNodeIdx"));
                    AddMaybeOptionalInput("Weapon Type", data.GetInt32Property("m_nWeaponTypeNodeIdx"));
                }

                var childNodeIdx = data.GetInt32Property("m_nChildNodeIdx");
                CreateInputAndChild<Pose>(node, childNodeIdx, "Input", "Result");
            }
            else if (node.NodeType is "Clip" or "AnimationPose" or "ReferencedGraph")
            {
                // Debug.Assert output type is Pose
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
                        CreateInputAndChild<Pose>(node, fallbackNodeIdx, "Fallback");
                    }
                }
                else if (node.NodeType is "Clip")
                {
                    node.AddSpace();
                    node.AddText($"Speed: {data.GetFloatProperty("m_flSpeedMultiplier"):F2}x");
                    node.AddText($"StartSyncEvent Offset: {data.GetInt32Property("m_nStartSyncEventOffset")}");
                    node.AddText($"Sample RootMotion: {data.GetBooleanProperty("m_bSampleRootMotion")}");
                    node.AddText($"Allow Looping: {data.GetBooleanProperty("m_bAllowLooping")}");

                    var playInReverseNodeIdx = data.GetInt32Property("m_nPlayInReverseValueNodeIdx");
                    if (playInReverseNodeIdx != -1)
                    {
                        CreateInputAndChild<Value>(node, playInReverseNodeIdx, "Play in reverse");
                    }

                    var resetTimeValueNodeIdx = data.GetInt32Property("m_nResetTimeValueNodeIdx");
                    if (resetTimeValueNodeIdx != -1)
                    {
                        CreateInputAndChild<Value>(node, resetTimeValueNodeIdx, "Reset time");
                    }

                }
                else if (node.NodeType is "AnimationPose")
                {
                    node.AddSpace();

                    var poseTimeNodeIdx = data.GetInt32Property("m_nPoseTimeValueNodeIdx");
                    if (poseTimeNodeIdx != -1)
                    {
                        CreateInputAndChild<Value>(node, poseTimeNodeIdx, "Time");
                    }

                    var timeRemapRange = data.GetSubCollection("m_inputTimeRemapRange");
                    var remapMin = timeRemapRange.GetFloatProperty("m_flMin");
                    var remapMax = timeRemapRange.GetFloatProperty("m_flMax");
                    var remapMinDesc = remapMin == float.MaxValue ? "None" : $"{remapMin:f}";
                    var remapMaxDesc = remapMax == float.MinValue ? "None" : $"{remapMax:f}";

                    node.AddText($"Remap: {remapMinDesc} - {remapMaxDesc}");
                    node.AddText($"Const Time: {data.GetFloatProperty("m_flUserSpecifiedTime"):f}");
                    node.AddText($"Use frames: {data.GetBooleanProperty("m_bUseFramesAsInput")}");
                }

                if (dataSlotIdx != -1)
                {
                    SetResourceReference(node, resources[dataSlotIdx]);
                }
            }
            else if (node.NodeType.StartsWith("ControlParameter", StringComparison.Ordinal))
            {
                // Graph input value set by game code.
            }
            else if (node.NodeType is "ZeroPose")
            {
                // Empty node
            }
            else
            {
                Log.Error(nameof(AnimationGraphViewer), $"Unhandled node type: {node.NodeType} ({node.Name})");
            }

        }

        var finalPose = new Node(null)
        {
            Name = "Result",
            NodeType = "FinalPose",
            Category = PoseHue,
        };

        var finalPoseInput = finalPose.AddInput("Out", PoseHue, allowMultiple: false);
        View.AddNode(finalPose);

        var root = CreateNode(nodePaths, nodes, rootNodeIdx);

        var rootOutput = root.AddOutput(string.Empty, PoseHue);
        View.Connect(rootOutput, finalPoseInput);

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

        View.LayoutNodesPacked();

        View.Legend.AddRange(
        [
            new("Pose flow", PoseHue, GraphLegendKind.Wire),
            new("Value", ValueHue, GraphLegendKind.Wire),
            new("Clip / graph reference", PoseHue, GraphLegendKind.Marker),
        ]);

        Log.Debug(nameof(AnimationGraphViewer), $"Created {createdNodes.Count} nodes (out of {nodes.Count}) or {createdNodes.Count / (float)nodes.Count:P}.");
    }

}
