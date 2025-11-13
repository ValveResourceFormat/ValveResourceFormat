using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

#nullable disable

namespace ValveResourceFormat.IO;

/// <summary>
/// Extracts and converts animation graph resources to editable format.
/// </summary>
public class AnimationGraphExtract
{
    private readonly BinaryKV3 resourceData;
    private KVObject graph => resourceData.Data;
    private readonly string outputFileName;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationGraphExtract"/> class.
    /// </summary>
    /// <param name="resource">The resource to extract from.</param>
    public AnimationGraphExtract(Resource resource)
    {
        resourceData = (BinaryKV3)resource.DataBlock;

        if (resource.FileName != null)
        {
            outputFileName = resource.FileName;
            outputFileName = outputFileName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal)
                ? outputFileName[..^2]
                : outputFileName;
        }
    }

    /// <summary>
    /// Converts the animation graph to a content file.
    /// </summary>
    /// <returns>A content file containing the animation graph data.</returns>
    public ContentFile ToContentFile()
    {
        // for newer resources, the class is "CAnimGraphModelBinding"
        var isUncompiledAnimationGraph = graph.GetStringProperty("_class") == "CAnimationGraph";

        var contentFile = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(isUncompiledAnimationGraph
                ? resourceData.GetKV3File().ToString()
                : ToEditableAnimGraphVersion19()
            ),
            FileName = outputFileName,
        };

        return contentFile;
    }

    /// <summary>
    /// Gets or sets the animation tags.
    /// </summary>
    public KVObject[] Tags { get; set; }

    /// <summary>
    /// Gets or sets the animation parameters.
    /// </summary>
    public KVObject[] Parameters { get; set; }

    /// <summary>
    /// Converts the compiled animation graph to editable version 19 format.
    /// </summary>
    /// <returns>The animation graph as a <see cref="KV3File"/> string in version 19 format.</returns>
    public string ToEditableAnimGraphVersion19()
    {
        var data = graph.GetSubCollection("m_pSharedData");
        var compiledNodes = data.GetArray("m_nodes");
        var compiledNodeIndexMap = data.GetArray("m_nodeIndexMap").Select(kv => kv.GetIntegerProperty("value")).ToArray();

        var tagManager = data.GetSubCollection("m_pTagManagerUpdater");
        var paramListUpdater = data.GetSubCollection("m_pParamListUpdater");

        if (data.GetArray("m_managers") is KVObject[] managers)
        {
            tagManager = managers.FirstOrDefault(m => m.GetProperty<string>("_class") == "CAnimTagManagerUpdater");
            paramListUpdater = managers.FirstOrDefault(m => m.GetProperty<string>("_class") == "CAnimParameterListUpdater");
        }

        if (tagManager == null || paramListUpdater == null)
        {
            throw new InvalidDataException("Missing tag manager or parameter list updater");
        }

        Tags = tagManager.GetArray("m_tags");
        Parameters = paramListUpdater.GetArray("m_parameters");

        var nodeManager = MakeListNode("CAnimNodeManager", "m_nodes");

        var i = 0;
        foreach (var compiledNode in compiledNodes)
        {
            var nodeId = i++; // compiledNodeIndexMap[i++];
            var nodeData = ConvertToUncompiled(compiledNode);
            var nodeIdObject = MakeNodeIdObjectValue(nodeId);

            nodeData.AddProperty("m_nNodeID", nodeIdObject);

            var nodeManagerItem = new KVObject(null, 2);
            nodeManagerItem.AddProperty("key", nodeIdObject);
            nodeManagerItem.AddProperty("value", nodeData);

            nodeManager.Children.AddItem(nodeManagerItem);
        }

        var localParameters = KVValue.MakeArray(Parameters);
        var localTags = KVValue.MakeArray(Tags);

        var kv = MakeNode(
            "CAnimationGraph",
            [
                ("m_nodeManager", nodeManager.Node),
                // ("m_componentManager", componentManager.Node),
                ("m_localParameters", localParameters),
                ("m_localTags", localTags),
                // ("m_referencedParamGroups", referencedParamGroups),
                // ("m_referencedTagGroups", referencedTagGroups),
                // ("m_referencedAnimGraphs", referencedAnimGraphs),
                // ("m_pSettingsManager", settingsManager),
                // ("m_clipDataManager", clipDataManager),
                ("m_modelName", graph.GetProperty<string>("m_modelName")),
            ]
        );

        return new KV3File(kv, format: KV3IDLookup.Get("animgraph19")).ToString();
    }

    private static KVValue MakeNodeIdObjectValue(long nodeId)
    {
        var nodeIdObject = new KVObject("fakeIndentKey", 1);
        nodeIdObject.AddProperty("m_id", unchecked((uint)nodeId));
        return new KVValue(nodeIdObject);
    }

    private static KVValue MakeInputConnection(long nodeId)
    {
        var nodeIdObject = MakeNodeIdObjectValue(nodeId);

        var inputConnection = new KVObject("fakeIndentKey", 2);
        inputConnection.AddProperty("m_nodeID", nodeIdObject);
        inputConnection.AddProperty("m_outputID", nodeIdObject);
        return new KVValue(inputConnection);
    }

    private static void AddInputConnection(KVObject node, long childNodeId)
    {
        var inputConnection = MakeInputConnection(childNodeId);
        node.AddProperty("m_inputConnection", inputConnection);
    }

    KVObject ConvertToUncompiled(KVObject compiledNode)
    {
        var className = compiledNode.GetProperty<string>("_class");
        className = className.Replace("UpdateNode", string.Empty, StringComparison.Ordinal);

        var newClass = className + "AnimNode";
        var node = MakeNode(newClass);

        var children = compiledNode.GetArray("m_children");
        var inputNodeIds = children?.Select(child => child.GetIntegerProperty("m_nodeIndex")).ToArray();

        foreach (var (key, value) in compiledNode.Properties)
        {
            if (key is "_class"
                    or "m_nodePath"
                    or "m_paramSpans" // todo
            )
            {
                continue;
            }

            var newKey = key;
            var subCollection = new Lazy<KVObject>(() => (KVObject)value.Value);

            // common remapped key
            if (key is "m_name" && className is "CSequence" or "CChoice" or "CSelector"
                or "CStateMachine" or "CRoot")
            {
                newKey = "m_sName";
            }

            if (className is "CRoot")
            {
                // Get the input connection of the final pose (root node)
                if (key is "m_pChildNode")
                {
                    var finalNodeInputIndex = subCollection.Value.GetIntegerProperty("m_nodeIndex");
                    AddInputConnection(node, finalNodeInputIndex);
                    continue;
                }
            }
            else if (className is "CSelector")
            {
                if (key is "m_eTagBehavior")
                {
                    newKey = "m_tagBehavior";
                }
                else if (key is "m_hParameter")
                {
                    var type = subCollection.Value.GetProperty<string>("m_type");
                    var paramIndex = subCollection.Value.GetIntegerProperty("m_index");

                    var source = type["ANIMPARAM_".Length..];
                    source = char.ToUpperInvariant(source[0]) + source[1..].ToLowerInvariant();

                    node.AddProperty("m_selectionSource", "SelectionSource_" + source);
                    node.AddProperty($"m_{source.ToLowerInvariant()}ParamID", ParameterIDFromIndex(paramIndex));
                    continue;
                }

                // blendDuration
            }
            else if (className is "CStateMachine")
            {
                if (key is "m_stateMachine" or "m_stateData" or "m_transitionData")
                {
                    continue;
                }
            }
            else if (className is "CSequence")
            {
                // skip
                if (key is "m_hSequence" or "m_duration")
                {
                    continue;
                }

                // remap
                if (key is "m_name")
                {
                    // Is this reliable? Using the node name as the sequence name.
                    node.AddProperty("m_sequenceName", value);
                }
            }
            else if (className is "CChoice")
            {
                // skip
                if (key is "m_weights" or "m_blendTimes")
                {
                    continue;
                }

                if (key is "m_children")
                {
                    var weights = compiledNode.GetFloatArray("m_weights");
                    var blendTimes = compiledNode.GetFloatArray("m_blendTimes");

                    var newInputs = weights.Zip(blendTimes, inputNodeIds).Select((choice) =>
                    {
                        var (weight, blendTime, nodeId) = choice;

                        var choiceNode = new KVObject(null, 3);
                        AddInputConnection(choiceNode, nodeId);
                        choiceNode.AddProperty("m_weight", weight);
                        choiceNode.AddProperty("m_blendTime", blendTime);

                        return choiceNode;
                    });

                    node.AddProperty("m_children", KVValue.MakeArray(newInputs));
                    continue;
                }
            }

            if (key is "m_children")
            {
                node.AddProperty(key, KVValue.MakeArray(inputNodeIds.Select(MakeInputConnection)));
                continue;
            }

            if (key is "m_tags")
            {
                if (className is "CSequence" or "CCycleControlClip" or "CBlend2D")
                {
                    // this is tag spans like so:
                    // {
                    //     m_tagIndex = 0
                    //     m_startCycle = 0.000000
                    //     m_endCycle = 1.000000
                    // },
                    continue;
                }

                try
                {
                    var tagIds = compiledNode.GetIntegerArray(key);
                    node.AddProperty(key, KVValue.MakeArray(tagIds.Select(MakeNodeIdObjectValue)));
                    continue;
                }
                catch (InvalidCastException)
                {
                    Console.WriteLine(className + " m_tags is a tag span");
                    continue;
                }
            }

            if (key is "m_paramIndex")
            {
                var paramIndex = subCollection.Value.GetIntegerProperty("m_index");
                node.AddProperty("m_paramID", ParameterIDFromIndex(paramIndex));
                continue;
            }

            node.AddProperty(newKey, value);
        }

        // TODO: CSelector, CStateMachine

        if (className is "CStateMachine")
        {
            var stateMachine = compiledNode.GetSubCollection("m_stateMachine");
            var stateData = compiledNode.GetArray("m_stateData");
            var transitionData = compiledNode.GetArray("m_transitionData");

            var states = stateMachine.GetArray("m_states");
            var transitions = stateMachine.GetArray("m_transitions");

            HashSet<string> passThroughStateProperties =
            [
                "m_name",
                "m_stateID",
                "m_bIsStartState",
                "m_bIsEndtState",
                "m_bIsPassthrough",
            ];

            var uncompiledStates = states.Select((state, i) =>
            {
                var data = stateData[i];
                var transitionIndices = state.GetIntegerArray("m_transitionIndices");

                var stateNode = MakeNode("CAnimNodeState");

                var uncompiledTransitions = transitionIndices.Select((transitionId) =>
                {
                    var transition = transitions[transitionId];
                    var data = transitionData[transitionId];

                    // m_conditionList?

                    var transitionNode = MakeNode("CAnimNodeStateTransition",
                        ("m_srcState", MakeNodeIdObjectValue(transition.GetIntegerProperty("m_srcStateIndex"))),
                        ("m_destState", MakeNodeIdObjectValue(transition.GetIntegerProperty("m_destStateIndex"))),
                        ("m_bDisabled", transition.GetIntegerProperty("m_bDisabled") > 0),
                        ("m_bReset", data.GetIntegerProperty("m_bReset") > 0)
                    );

                    // data m_resetCycleOption int -> string
                    // cycle and blend duration

                    AddInputConnection(transitionNode, transition.GetIntegerProperty("m_nodeIndex"));
                    return transitionNode;
                });

                stateNode.AddProperty("m_transitions", KVValue.MakeArray(uncompiledTransitions));

                if (state.ContainsKey("m_actions"))
                {
                    stateNode.AddProperty("m_actions", KVValue.MakeArray(state.GetArray("m_actions").Select(compiledAction =>
                    {
                        var uncompiledAction = MakeNode("CStateAction");
                        foreach (var compiledProperty in compiledAction)
                        {
                            if (compiledProperty.Key is "m_pAction")
                            {
                                var action = (KVObject)compiledProperty.Value;
                                var actionData = MakeNode(action.GetProperty<string>("_class").Replace("Updater", string.Empty, StringComparison.Ordinal));
                                if (action.ContainsKey("m_nTagIndex"))
                                {
                                    // convert from index to handle
                                    var tagId = action.GetIntegerProperty("m_nTagIndex");
                                    if (tagId != -1)
                                    {
                                        tagId = Tags[tagId].GetSubCollection("m_tagID").GetIntegerProperty("m_id");
                                    }

                                    actionData.AddProperty("m_tag", MakeNodeIdObjectValue(tagId));
                                }

                                if (action.ContainsKey("m_hParam"))
                                {
                                    var parameterId = action.GetSubCollection("m_hParam").GetIntegerProperty("m_index");
                                    actionData.AddProperty("m_param", ParameterIDFromIndex(parameterId));
                                }

                                if (action.ContainsKey("m_value"))
                                {
                                    actionData.AddProperty("m_value", action.GetSubCollection("m_value"));
                                }

                                uncompiledAction.AddProperty(compiledProperty.Key, actionData);
                                continue;
                            }
                            uncompiledAction.AddProperty(compiledProperty.Key, compiledProperty.Value);
                        }
                        return uncompiledAction;
                    })));
                }

                // Pasthrough properties
                foreach (var (key, value) in state.Properties)
                {
                    if (passThroughStateProperties.Contains(key))
                    {
                        stateNode.AddProperty(key, value);
                    }
                }

                AddInputConnection(stateNode, data.GetSubCollection("m_pChild").GetIntegerProperty("m_nodeIndex"));
                stateNode.AddProperty("m_bIsRootMotionExclusive", data.GetIntegerProperty("m_bExclusiveRootMotion") > 0);
                return stateNode;
            });

            node.AddProperty("m_states", KVValue.MakeArray(uncompiledStates));
        }

        return node;
    }

    private KVValue ParameterIDFromIndex(long paramIndex)
    {
        if (paramIndex == 255)
        {
            paramIndex = -1;
        }
        if (paramIndex != -1)
        {
            paramIndex = Parameters[paramIndex].GetSubCollection("m_id").GetIntegerProperty("m_id");
        }

        return MakeNodeIdObjectValue(paramIndex);
    }
}
