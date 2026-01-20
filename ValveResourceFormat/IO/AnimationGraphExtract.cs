using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;
namespace ValveResourceFormat.IO;

/// <summary>
/// Extracts and converts animation graph resources to editable format.
/// </summary>
public class AnimationGraphExtract
{
    private readonly BinaryKV3 resourceData;
    private KVObject graph => resourceData.Data;
    private readonly string? outputFileName;
    private int nodePositionOffset;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationGraphExtract"/> class.
    /// </summary>
    /// <param name="resource">The resource to extract from.</param>
    public AnimationGraphExtract(Resource resource)
    {
        if (resource.DataBlock is not BinaryKV3 kv3)
        {
            throw new InvalidDataException($"Resource data block is not a BinaryKV3");
        }

        resourceData = kv3;

        if (resource.FileName != null)
        {
            outputFileName = resource.FileName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal)
                ? resource.FileName[..^2]
                : resource.FileName;
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
            FileName = outputFileName ?? "animgraph",
        };

        return contentFile;
    }

    /// <summary>
    /// Gets or sets the animation tags.
    /// </summary>
    public KVObject[] Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the animation parameters.
    /// </summary>
    public KVObject[] Parameters { get; set; } = [];

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
        var scriptManager = data.GetSubCollection("m_scriptManager");

        if (data.GetArray("m_managers") is KVObject[] managers)
        {
            tagManager = managers.FirstOrDefault(m => m.GetProperty<string>("_class") == "CAnimTagManagerUpdater");
            paramListUpdater = managers.FirstOrDefault(m => m.GetProperty<string>("_class") == "CAnimParameterListUpdater");
            scriptManager = managers.FirstOrDefault(m => m.GetProperty<string>("_class") == "CAnimScriptManager");
        }

        if (tagManager == null || paramListUpdater == null)
        {
            throw new InvalidDataException("Missing tag manager or parameter list updater");
        }

        Tags = tagManager.GetArray("m_tags");
        Parameters = paramListUpdater.GetArray("m_parameters");

        KVObject clipDataManager;
        if (tagManager.ContainsKey("sequence_tag_spans"))
        {
            var sequenceTagSpans = tagManager.GetArray("sequence_tag_spans");
            clipDataManager = ConvertClipDataManager(sequenceTagSpans);
        }
        else
        {
            clipDataManager = MakeNode("CAnimClipDataManager");
            clipDataManager.AddProperty("m_itemTable", new KVObject(null, isArray: false, 0));
        }

        var nodeManager = MakeListNode("CAnimNodeManager", "m_nodes");
        var componentList = new List<KVObject>();
        if (data.ContainsKey("m_components"))
        {
            var compiledComponents = data.GetArray("m_components");
            foreach (var compiledComponent in compiledComponents)
            {
                try
                {
                    var componentData = ConvertComponent(compiledComponent);
                    componentList.Add(componentData);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error converting component: {ex.Message}");
                }
            }
        }

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
        var componentManager = MakeNode("CAnimComponentManager");
        componentManager.AddProperty("m_components", KVValue.MakeArray(componentList.ToArray()));

        var kv = MakeNode(
            "CAnimationGraph",
            [
            ("m_nodeManager", nodeManager.Node),
            ("m_componentManager", componentManager),
            ("m_localParameters", localParameters),
            ("m_localTags", localTags),
            // ("m_referencedParamGroups", referencedParamGroups),
            // ("m_referencedTagGroups", referencedTagGroups),
            // ("m_referencedAnimGraphs", referencedAnimGraphs),
            // ("m_pSettingsManager", settingsManager),
            ("m_clipDataManager", clipDataManager),
            ("m_modelName", graph.GetProperty<string>("m_modelName")),
            ]
        );

        return new KV3File(kv, format: KV3IDLookup.Get("animgraph19")).ToString();
    }

    private KVObject ConvertClipDataManager(KVObject[] sequenceTagSpans)
    {
        var clipDataManager = MakeNode("CAnimClipDataManager");
        var itemTable = new KVObject(null, isArray: false, 0);
        foreach (var sequenceSpan in sequenceTagSpans)
        {
            var sequenceName = sequenceSpan.GetStringProperty("m_sSequenceName");
            var compiledTagSpans = sequenceSpan.GetArray("m_tags");
            if (string.IsNullOrEmpty(sequenceName) || compiledTagSpans.Length == 0)
            {
                continue;
            }
            var clipData = MakeNode("CAnimClipData");
            clipData.AddProperty("m_clipName", sequenceName);
            var tagSpans = new List<KVObject>();
            foreach (var compiledTagSpan in compiledTagSpans)
            {
                var tagIndex = compiledTagSpan.GetIntegerProperty("m_tagIndex");
                var startCycle = compiledTagSpan.GetFloatProperty("m_startCycle");
                var endCycle = compiledTagSpan.GetFloatProperty("m_endCycle");
                var duration = endCycle - startCycle;
                var tagId = -1L;
                if (tagIndex >= 0 && tagIndex < Tags.Length)
                {
                    tagId = Tags[tagIndex].GetSubCollection("m_tagID").GetIntegerProperty("m_id");
                }
                var tagSpan = MakeNode("CAnimTagSpan");
                tagSpan.AddProperty("m_id", MakeNodeIdObjectValue(tagId));
                tagSpan.AddProperty("m_fStartCycle", startCycle);
                tagSpan.AddProperty("m_fDuration", duration);
                tagSpans.Add(tagSpan);
            }
            clipData.AddProperty("m_tagSpans", KVValue.MakeArray(tagSpans.ToArray()));
            itemTable.AddProperty(sequenceName, clipData);
        }

        clipDataManager.AddProperty("m_itemTable", itemTable);
        return clipDataManager;
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

    private static KVValue MakeVector2(float x, float y)
    {
        var values = new object[] { x, y };
        return KVValue.MakeArray(values.Select(v => new KVValue(ValveKeyValue.KVValueType.FloatingPoint, v)).ToArray());
    }
    private KVObject ConvertBlendDuration(KVObject compiledBlendDuration)
    {
        var constValue = compiledBlendDuration.GetFloatProperty("m_constValue");
        var paramRef = compiledBlendDuration.GetSubCollection("m_hParam");
        var paramType = paramRef.GetStringProperty("m_type");
        var paramIndex = paramRef.GetIntegerProperty("m_index");

        var blendDuration = MakeNode("CFloatAnimValue");
        blendDuration.AddProperty("m_flConstValue", constValue);

        var paramIdValue = ParameterIDFromIndex(paramType, paramIndex, requireFloat: true);
        blendDuration.AddProperty("m_paramID", paramIdValue);
        var paramIdObject = (KVObject)paramIdValue.Value;
        var paramId = paramIdObject.GetIntegerProperty("m_id");
        var source = paramId == uint.MaxValue ? "Constant" : "Parameter";
        blendDuration.AddProperty("m_eSource", source);

        return blendDuration;
    }
    private KVObject[] ConvertStateMachine(KVObject compiledStateMachine, KVObject[] stateDataArray, KVObject[] transitionDataArray, bool isComponent = false)
    {
        var compiledStates = compiledStateMachine.GetArray("m_states");
        var compiledTransitions = compiledStateMachine.GetArray("m_transitions");
        var states = new KVObject[compiledStates.Length];

        int startStateIndex = -1;
        for (int i = 0; i < compiledStates.Length; i++)
        {
            if (compiledStates[i].GetIntegerProperty("m_bIsStartState") > 0)
            {
                startStateIndex = i;
                break;
            }
        }
        for (int i = 0; i < compiledStates.Length; i++)
        {
            var compiledState = compiledStates[i];
            var stateData = stateDataArray != null && i < stateDataArray.Length ? stateDataArray[i] : null;

            string stateNodeType = isComponent ? "CAnimComponentState" : "CAnimNodeState";
            var stateNode = MakeNode(stateNodeType);
            float stateX, stateY;
            var random = new Random(i);
            if (i == startStateIndex)
            {
                stateX = -50.0f + random.Next(-20, 21);
                stateY = -30.0f + random.Next(-15, 16);
            }
            else
            {
                int positionFromStart = i > startStateIndex ? i - startStateIndex : i + (compiledStates.Length - startStateIndex);
                stateX = 150.0f * positionFromStart + random.Next(-30, 31);
                stateY = 40.0f + random.Next(-10, 11);
            }
            stateNode.AddProperty("m_position", MakeVector2(stateX, stateY));
            nodePositionOffset++;
            stateNode.AddProperty("m_name", compiledState.GetStringProperty("m_name"));
            stateNode.AddProperty("m_stateID", compiledState.GetSubCollection("m_stateID"));
            stateNode.AddProperty("m_bIsStartState", compiledState.GetIntegerProperty("m_bIsStartState") > 0);
            stateNode.AddProperty("m_bIsEndtState", compiledState.GetIntegerProperty("m_bIsEndState") > 0);
            stateNode.AddProperty("m_bIsPassthrough", compiledState.GetIntegerProperty("m_bIsPassthrough") > 0);

            if (compiledState.ContainsKey("m_transitionIndices"))
            {
                var transitionIndices = compiledState.GetIntegerArray("m_transitionIndices");
                var transitions = new List<KVObject>();

                foreach (var transitionIndex in transitionIndices)
                {
                    if (transitionIndex >= 0 && transitionIndex < compiledTransitions.Length)
                    {
                        var compiledTransition = compiledTransitions[transitionIndex];
                        var transitionData = transitionDataArray != null && transitionIndex < transitionDataArray.Length ?
                            transitionDataArray[transitionIndex] : null;

                        string transitionNodeType = isComponent ? "CAnimComponentStateTransition" : "CAnimNodeStateTransition";
                        var transitionNode = MakeNode(transitionNodeType);
                        var srcStateIndex = compiledTransition.GetIntegerProperty("m_srcStateIndex");
                        var destStateIndex = compiledTransition.GetIntegerProperty("m_destStateIndex");
                        var srcStateID = compiledStates[srcStateIndex].GetSubCollection("m_stateID");
                        var destStateID = compiledStates[destStateIndex].GetSubCollection("m_stateID");

                        transitionNode.AddProperty("m_srcState", srcStateID);
                        transitionNode.AddProperty("m_destState", destStateID);
                        transitionNode.AddProperty("m_bDisabled", compiledTransition.GetIntegerProperty("m_bDisabled") > 0);

                        var conditionList = MakeNode("CConditionContainer");
                        conditionList.AddProperty("m_conditions", new KVObject(null, isArray: true, 0));
                        transitionNode.AddProperty("m_conditionList", conditionList);

                        if (!isComponent)
                        {
                            AddInputConnection(transitionNode, compiledTransition.GetIntegerProperty("m_nodeIndex"));
                            if (transitionData != null)
                            {
                                transitionNode.AddProperty("m_bReset", transitionData.GetIntegerProperty("m_bReset") > 0);
                                if (transitionData.ContainsKey("m_resetCycleOption"))
                                {
                                    var resetOption = transitionData.GetIntegerProperty("m_resetCycleOption");
                                    var resetOptionStr = resetOption switch
                                    {
                                        0 => "Beginning",
                                        1 => "SameCycleAsSource",
                                        2 => "InverseSourceCycle",
                                        3 => "FixedValue",
                                        4 => "SameTimeAsSource",
                                        _ => "Beginning"
                                    };
                                    transitionNode.AddProperty("m_resetCycleOption", resetOptionStr);
                                }

                                if (transitionData.ContainsKey("m_blendDuration"))
                                {
                                    var blendDuration = transitionData.GetSubCollection("m_blendDuration");
                                    if (blendDuration != null)
                                    {
                                        var convertedBlendDuration = ConvertBlendDuration(blendDuration);
                                        transitionNode.AddProperty("m_blendDuration", convertedBlendDuration);
                                    }
                                }

                                if (transitionData.ContainsKey("m_resetCycleValue"))
                                {
                                    var resetcycleValue = transitionData.GetSubCollection("m_resetCycleValue");
                                    if (resetcycleValue != null)
                                    {
                                        var convertedfixedcycleValue = ConvertBlendDuration(resetcycleValue);
                                        transitionNode.AddProperty("m_flFixedCycleValue", convertedfixedcycleValue);
                                    }
                                }

                                if (transitionData.ContainsKey("m_curve"))
                                {
                                    var compiledCurve = transitionData.GetSubCollection("m_curve");
                                    var blendCurve = MakeNode("CBlendCurve");

                                    if (compiledCurve.ContainsKey("m_flControlPoint1"))
                                    {
                                        blendCurve.AddProperty("m_flControlPoint1", compiledCurve.GetFloatProperty("m_flControlPoint1"));
                                    }
                                    else
                                    {
                                        blendCurve.AddProperty("m_flControlPoint1", 0.0f);
                                    }
                                    if (compiledCurve.ContainsKey("m_flControlPoint2"))
                                    {
                                        blendCurve.AddProperty("m_flControlPoint2", compiledCurve.GetFloatProperty("m_flControlPoint2"));
                                    }
                                    else
                                    {
                                        blendCurve.AddProperty("m_flControlPoint2", 1.0f);
                                    }
                                    transitionNode.AddProperty("m_blendCurve", blendCurve);
                                }
                                else
                                {
                                    var blendCurve = MakeNode("CBlendCurve");
                                    blendCurve.AddProperty("m_flControlPoint1", 0.0f);
                                    blendCurve.AddProperty("m_flControlPoint2", 1.0f);
                                    transitionNode.AddProperty("m_blendCurve", blendCurve);
                                }
                            }
                        }
                        transitions.Add(transitionNode);
                    }
                }
                stateNode.AddProperty("m_transitions", KVValue.MakeArray(transitions.ToArray()));
            }

            if (compiledState.ContainsKey("m_actions"))
            {
                var compiledActions = compiledState.GetArray("m_actions");
                var actions = new List<KVObject>();

                foreach (var compiledAction in compiledActions)
                {
                    var action = MakeNode("CStateAction");
                    if (compiledAction.ContainsKey("m_pAction"))
                    {
                        var compiledActionData = compiledAction.GetSubCollection("m_pAction");
                        var actionData = MakeNode(compiledActionData.GetStringProperty("_class").Replace("Updater", string.Empty, StringComparison.Ordinal));

                        if (compiledActionData.ContainsKey("m_nTagIndex"))
                        {
                            var tagId = compiledActionData.GetIntegerProperty("m_nTagIndex");
                            if (tagId != -1)
                            {
                                tagId = Tags[tagId].GetSubCollection("m_tagID").GetIntegerProperty("m_id");
                            }
                            actionData.AddProperty("m_tag", MakeNodeIdObjectValue(tagId));
                        }
                        if (compiledActionData.ContainsKey("m_hParam"))
                        {
                            var paramRef = compiledActionData.GetSubCollection("m_hParam");
                            var paramType = paramRef.GetStringProperty("m_type");
                            var paramIndex = paramRef.GetIntegerProperty("m_index");
                            actionData.AddProperty("m_param", ParameterIDFromIndex(paramType, paramIndex));
                        }
                        if (compiledActionData.ContainsKey("m_value"))
                        {
                            actionData.AddProperty("m_value", compiledActionData.GetSubCollection("m_value"));
                        }
                        action.AddProperty("m_pAction", actionData);
                    }
                    actions.Add(action);
                }
                stateNode.AddProperty("m_actions", KVValue.MakeArray(actions.ToArray()));
            }
            if (!isComponent && stateData != null)
            {
                AddInputConnection(stateNode, stateData.GetSubCollection("m_pChild").GetIntegerProperty("m_nodeIndex"));
                stateNode.AddProperty("m_bIsRootMotionExclusive", stateData.GetIntegerProperty("m_bExclusiveRootMotion") > 0);
            }
            states[i] = stateNode;
        }

        return states;
    }
    private KVObject ConvertComponent(KVObject compiledComponent)
    {
        var className = compiledComponent.GetStringProperty("_class");
        var newClassName = className.Replace("Updater", string.Empty, StringComparison.Ordinal);
        var component = MakeNode(newClassName);

        component.AddProperty("m_group", "");
        component.AddProperty("m_id", compiledComponent.GetSubCollection("m_id"));
        component.AddProperty("m_bStartEnabled", compiledComponent.GetIntegerProperty("m_bStartEnabled") > 0);
        component.AddProperty("m_nPriority", 100);
        if (compiledComponent.ContainsKey("m_networkMode"))
        {
            component.AddProperty("m_networkMode", compiledComponent.GetProperty<string>("m_networkMode"));
        }

        /* Action Component */
        if (className == "CActionComponentUpdater")
        {
            if (compiledComponent.ContainsKey("m_actions"))
            {
                var actions = compiledComponent.GetArray("m_actions");
                var convertedActions = actions.Select(action =>
                {
                    var actionClassName = action.GetStringProperty("_class");
                    var newActionClassName = actionClassName.Replace("Updater", string.Empty, StringComparison.Ordinal);
                    var newAction = MakeNode(newActionClassName);

                    foreach (var (actionKey, actionValue) in action.Properties)
                    {
                        if (actionKey is "_class")
                        {
                            continue;
                        }
                        if (actionKey is "m_nTagIndex")
                        {
                            var tagIndex = action.GetIntegerProperty("m_nTagIndex");
                            var tagId = tagIndex != -1 ?
                                Tags[tagIndex].GetSubCollection("m_tagID").GetIntegerProperty("m_id") :
                                -1;
                            newAction.AddProperty("m_tag", MakeNodeIdObjectValue(tagId));
                            continue;
                        }
                        if (actionKey is "m_hParam")
                        {
                            var paramRef = (KVObject)actionValue.Value;
                            var paramType = paramRef.GetStringProperty("m_type");
                            var paramIndex = paramRef.GetIntegerProperty("m_index");
                            newAction.AddProperty("m_param", ParameterIDFromIndex(paramType, paramIndex));
                            continue;
                        }
                        if (actionKey is "m_hScript")
                        {
                            continue;
                        }
                        if (actionKey is "m_eParamType")
                        {
                            continue;
                        }
                        newAction.AddProperty(actionKey, actionValue);
                    }
                    return newAction;
                });
                component.AddProperty("m_actions", KVValue.MakeArray(convertedActions));
            }
            return component;
        }
        /* Look Component */
        if (className == "CLookComponentUpdater")
        {
            component.AddProperty("m_bNetworkLookTarget", compiledComponent.GetIntegerProperty("m_bNetworkLookTarget") > 0);

            var lookHeadingParam = compiledComponent.GetSubCollection("m_hLookHeading");
            var lookHeadingType = lookHeadingParam.GetStringProperty("m_type");
            var lookHeadingIndex = lookHeadingParam.GetIntegerProperty("m_index");
            component.AddProperty("m_lookHeadingID", ParameterIDFromIndex(lookHeadingType, lookHeadingIndex));

            var lookHeadingVelocityParam = compiledComponent.GetSubCollection("m_hLookHeadingVelocity");
            var lookHeadingVelocityType = lookHeadingVelocityParam.GetStringProperty("m_type");
            var lookHeadingVelocityIndex = lookHeadingVelocityParam.GetIntegerProperty("m_index");
            component.AddProperty("m_lookHeadingVelocityID", ParameterIDFromIndex(lookHeadingVelocityType, lookHeadingVelocityIndex));

            var lookPitchParam = compiledComponent.GetSubCollection("m_hLookPitch");
            var lookPitchType = lookPitchParam.GetStringProperty("m_type");
            var lookPitchIndex = lookPitchParam.GetIntegerProperty("m_index");
            component.AddProperty("m_lookPitchID", ParameterIDFromIndex(lookPitchType, lookPitchIndex));

            var lookDirectionParam = compiledComponent.GetSubCollection("m_hLookDirection");
            var lookDirectionType = lookDirectionParam.GetStringProperty("m_type");
            var lookDirectionIndex = lookDirectionParam.GetIntegerProperty("m_index");
            component.AddProperty("m_lookDirectionID", ParameterIDFromIndex(lookDirectionType, lookDirectionIndex));

            var lookTargetParam = compiledComponent.GetSubCollection("m_hLookTarget");
            var lookTargetType = lookTargetParam.GetStringProperty("m_type");
            var lookTargetIndex = lookTargetParam.GetIntegerProperty("m_index");
            component.AddProperty("m_lookTargetID", ParameterIDFromIndex(lookTargetType, lookTargetIndex));

            var lookTargetWorldSpaceParam = compiledComponent.GetSubCollection("m_hLookTargetWorldSpace");
            var lookTargetWorldSpaceType = lookTargetWorldSpaceParam.GetStringProperty("m_type");
            var lookTargetWorldSpaceIndex = lookTargetWorldSpaceParam.GetIntegerProperty("m_index");
            component.AddProperty("m_lookTargetWorldSpaceID", ParameterIDFromIndex(lookTargetWorldSpaceType, lookTargetWorldSpaceIndex));

            return component;
        }
        /* Slope Component */
        if (className == "CSlopeComponentUpdater")
        {
            var slopeangleParam = compiledComponent.GetSubCollection("m_hSlopeAngle");
            var slopeangleType = slopeangleParam.GetStringProperty("m_type");
            var slopeangleIndex = slopeangleParam.GetIntegerProperty("m_index");
            component.AddProperty("m_slopeAngleID", ParameterIDFromIndex(slopeangleType, slopeangleIndex));

            var slopeanglefrontParam = compiledComponent.GetSubCollection("m_hSlopeAngleFront");
            var slopeanglefrontType = slopeanglefrontParam.GetStringProperty("m_type");
            var slopeanglefrontIndex = slopeanglefrontParam.GetIntegerProperty("m_index");
            component.AddProperty("m_slopeAngleFrontID", ParameterIDFromIndex(slopeanglefrontType, slopeanglefrontIndex));

            var slopeanglesideParam = compiledComponent.GetSubCollection("m_hSlopeAngleSide");
            var slopeanglesideType = slopeanglesideParam.GetStringProperty("m_type");
            var slopeanglesideIndex = slopeanglesideParam.GetIntegerProperty("m_index");
            component.AddProperty("m_slopeAngleSideID", ParameterIDFromIndex(slopeanglesideType, slopeanglesideIndex));

            var slopeheadingParam = compiledComponent.GetSubCollection("m_hSlopeHeading");
            var slopeheadingType = slopeheadingParam.GetStringProperty("m_type");
            var slopeheadingIndex = slopeheadingParam.GetIntegerProperty("m_index");
            component.AddProperty("m_slopeHeadingID", ParameterIDFromIndex(slopeheadingType, slopeheadingIndex));

            var slopenormalParam = compiledComponent.GetSubCollection("m_hSlopeNormal");
            var slopenormalType = slopenormalParam.GetStringProperty("m_type");
            var slopenormalIndex = slopenormalParam.GetIntegerProperty("m_index");
            component.AddProperty("m_slopeNormalID", ParameterIDFromIndex(slopenormalType, slopenormalIndex));

            var slopenormalWorldSpaceParam = compiledComponent.GetSubCollection("m_hSlopeNormal_WorldSpace");
            var slopenormalWorldSpaceType = slopenormalWorldSpaceParam.GetStringProperty("m_type");
            var slopenormalWorldSpaceIndex = slopenormalWorldSpaceParam.GetIntegerProperty("m_index");
            component.AddProperty("m_slopeNormal_WorldSpaceID", ParameterIDFromIndex(slopenormalWorldSpaceType, slopenormalWorldSpaceIndex));

            return component;
        }
        /* Ragdoll Component */
        if (className == "CRagdollComponentUpdater")
        {
            if (compiledComponent.ContainsKey("m_weightLists"))
            {
                var weightLists = compiledComponent.GetArray("m_weightLists");
                var boneNames = compiledComponent.GetArray<string>("m_boneNames");

                var convertedWeightLists = new List<KVObject>();
                foreach (var weightList in weightLists)
                {
                    var weightListName = weightList.GetStringProperty("m_name");
                    var weightArray = weightList.GetFloatArray("m_weights");
                    if (string.IsNullOrEmpty(weightListName) || weightArray.Length == 0)
                    {
                        continue;
                    }
                    var weightListNode = MakeNode("CRigidBodyWeightList",
                        ("m_name", weightListName)
                    );
                    var weightsArray = new KVObject(null, isArray: true, weightArray.Length);
                    for (int i = 0; i < weightArray.Length; i++)
                    {
                        var weightDefinition = new KVObject(null, 2);
                        string boneName = i < boneNames.Length ? boneNames[i] : $"bone_{i}";

                        weightDefinition.AddProperty("m_name", boneName);
                        weightDefinition.AddProperty("m_flWeight", weightArray[i]);
                        weightsArray.AddItem(weightDefinition);
                    }
                    weightListNode.AddProperty("m_weights", weightsArray);
                    convertedWeightLists.Add(weightListNode);
                }
                component.AddProperty("m_weightLists", KVValue.MakeArray(convertedWeightLists.ToArray()));
            }
            component.AddProperty("m_flSpringFrequencyMin", compiledComponent.GetFloatProperty("m_flSpringFrequencyMin"));
            component.AddProperty("m_flSpringFrequencyMax", compiledComponent.GetFloatProperty("m_flSpringFrequencyMax"));
            if (compiledComponent.ContainsKey("m_flMaxStretch"))
            {
                component.AddProperty("m_flMaxStretch", compiledComponent.GetFloatProperty("m_flMaxStretch"));
            }
            if (compiledComponent.ContainsKey("m_bSolidCollisionAtZeroWeight"))
            {
                component.AddProperty("m_bSolidCollisionAtZeroWeight", compiledComponent.GetIntegerProperty("m_bSolidCollisionAtZeroWeight") > 0);
            }
            return component;
        }
        /* Damped Value Component */
        if (className == "CDampedValueComponentUpdater")
        {
            component.AddProperty("m_name", compiledComponent.GetProperty<string>("m_name"));
            if (compiledComponent.ContainsKey("m_items"))
            {
                var items = compiledComponent.GetArray("m_items");
                var convertedItems = items.Select(item =>
                {
                    var newItem = new KVObject(null, 6);

                    var paramIn = item.GetSubCollection("m_hParamIn");
                    var paramInType = paramIn.GetStringProperty("m_type");
                    var paramInIndex = paramIn.GetIntegerProperty("m_index");
                    var paramOut = item.GetSubCollection("m_hParamOut");
                    var paramOutType = paramOut.GetStringProperty("m_type");
                    var paramOutIndex = paramOut.GetIntegerProperty("m_index");

                    string valueType = paramInType == "ANIMPARAM_VECTOR" ? "VectorParameter" : "FloatParameter";
                    newItem.AddProperty("m_valueType", valueType);

                    if (valueType == "FloatParameter")
                    {
                        newItem.AddProperty("m_floatParamIn", ParameterIDFromIndex(paramInType, paramInIndex));
                        newItem.AddProperty("m_floatParamOut", ParameterIDFromIndex(paramOutType, paramOutIndex));
                        newItem.AddProperty("m_vectorParamIn", MakeNodeIdObjectValue(-1));
                        newItem.AddProperty("m_vectorParamOut", MakeNodeIdObjectValue(-1));
                    }
                    else
                    {
                        newItem.AddProperty("m_floatParamIn", MakeNodeIdObjectValue(-1));
                        newItem.AddProperty("m_floatParamOut", MakeNodeIdObjectValue(-1));
                        newItem.AddProperty("m_vectorParamIn", ParameterIDFromIndex(paramInType, paramInIndex));
                        newItem.AddProperty("m_vectorParamOut", ParameterIDFromIndex(paramOutType, paramOutIndex));
                    }
                    newItem.AddProperty("m_damping", item.GetSubCollection("m_damping"));
                    return newItem;
                });
                component.AddProperty("m_items", KVValue.MakeArray(convertedItems.ToArray()));
            }
            return component;
        }
        /* VR Input Component */
        if (className == "CVRInputComponentUpdater")
        {
            string[] paramProperties =
            {
                "m_FingerCurl_Thumb",
                "m_FingerCurl_Index",
                "m_FingerCurl_Middle",
                "m_FingerCurl_Ring",
                "m_FingerCurl_Pinky",
                "m_FingerSplay_Thumb_Index",
                "m_FingerSplay_Index_Middle",
                "m_FingerSplay_Middle_Ring",
                "m_FingerSplay_Ring_Pinky"
            };
            foreach (var paramName in paramProperties)
            {
                if (compiledComponent.ContainsKey(paramName))
                {
                    var paramHandle = compiledComponent.GetSubCollection(paramName);
                    var paramType = paramHandle.GetStringProperty("m_type");
                    var paramIndex = paramHandle.GetIntegerProperty("m_index");
                    component.AddProperty(paramName, ParameterIDFromIndex(paramType, paramIndex));
                }
            }

            return component;
        }
        /* State Machine Component */
        if (className == "CStateMachineComponentUpdater")
        {
            component.AddProperty("m_sName", compiledComponent.GetProperty<string>("m_name"));
            if (compiledComponent.ContainsKey("m_stateMachine"))
            {
                var compiledStateMachine = compiledComponent.GetSubCollection("m_stateMachine");
                var states = ConvertStateMachine(compiledStateMachine, null, null, isComponent: true);
                component.AddProperty("m_states", KVValue.MakeArray(states));
            }
            return component;
        }

        foreach (var (key, value) in compiledComponent.Properties)
        {
            if (key is "_class" or "m_paramHandles" or "m_name" or "m_id" or "m_bStartEnabled" or "m_networkMode")
            {
                continue;
            }
            if (key is "m_motors")
            {
                var motors = compiledComponent.GetArray("m_motors");
                var convertedMotors = motors.Select(motor =>
                {
                    var motorClassName = motor.GetStringProperty("_class");
                    var newMotorClassName = motorClassName.Replace("Updater", string.Empty, StringComparison.Ordinal);
                    var newMotor = MakeNode(newMotorClassName);

                    foreach (var (motorKey, motorValue) in motor.Properties)
                    {
                        if (motorKey is "_class")
                        {
                            continue;
                        }
                        if (motorKey.EndsWith("Param", StringComparison.Ordinal) && motorValue.Value is KVObject paramRef)
                        {
                            var paramType = paramRef.GetStringProperty("m_type");
                            var paramIndex = paramRef.GetIntegerProperty("m_index");
                            var newKey = motorKey.Replace("h", "").Replace("Param", "Param");
                            newMotor.AddProperty(newKey, ParameterIDFromIndex(paramType, paramIndex));
                            continue;
                        }
                        newMotor.AddProperty(motorKey, motorValue);
                    }
                    return newMotor;
                });
                component.AddProperty(key, KVValue.MakeArray(convertedMotors));
                continue;
            }

            component.AddProperty(key, value);
        }

        if (compiledComponent.ContainsKey("m_paramHandles"))
        {
            var paramHandles = compiledComponent.GetArray("m_paramHandles");
            var paramIDs = paramHandles.Select(handle =>
            {
                var paramRef = (KVObject)handle;
                var paramType = paramRef.GetStringProperty("m_type");
                var paramIndex = paramRef.GetIntegerProperty("m_index");
                return ParameterIDFromIndex(paramType, paramIndex);
            });
            component.AddProperty("m_paramIDs", KVValue.MakeArray(paramIDs));
        }
        return component;
    }
    KVObject ConvertToUncompiled(KVObject compiledNode)
    {
        var className = compiledNode.GetProperty<string>("_class");
        className = className.Replace("UpdateNode", string.Empty, StringComparison.Ordinal);

        var newClass = className + "AnimNode";
        var node = MakeNode(newClass);

        var children = compiledNode.GetArray("m_children");
        var inputNodeIds = children?.Select(child => child.GetIntegerProperty("m_nodeIndex")).ToArray();

        /* Preferably the node position algorithm should be similar to the AG2 graph viewer node placement algorithm instead of random + left shift */
        /* Along with decompilation of the nodes into groups based on m_nCount = in m_nodePath / m_nodeIndexMap */
        if (className == "CRoot")
        {
            node.AddProperty("m_vecPosition", MakeVector2(0.0f, 0.0f));
            nodePositionOffset = 0;
        }
        else
        {
            float xPosition = -150.0f * (nodePositionOffset + 1);
            float yPosition = 100.0f * (nodePositionOffset + 1);
            var random = new Random(nodePositionOffset);
            yPosition += random.Next(-50, 51);
            node.AddProperty("m_vecPosition", MakeVector2(xPosition, yPosition));
            nodePositionOffset++;
        }
        foreach (var (key, value) in compiledNode.Properties)
        {
            if (key is "_class"
                    or "m_nodePath"
            )
            {
                continue;
            }

            var newKey = key;
            var subCollection = new Lazy<KVObject>(() => (KVObject)value.Value!);

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
                    node.AddProperty($"m_{source.ToLowerInvariant()}ParamID", ParameterIDFromIndex(type, paramIndex));
                    continue;
                }
                if (key is "m_flBlendTime")
                {
                    var convertedBlendDuration = ConvertBlendDuration(subCollection.Value);
                    node.AddProperty("m_blendDuration", convertedBlendDuration);
                    continue;
                }
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
                // m_hSequence uses a clip number from the vmdl's ASEQ/m_localSequenceNameArray block.
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

                    if (inputNodeIds is not null)
                    {
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
                    }
                    continue;
                }
            }

            if (key is "m_children")
            {
                if (inputNodeIds is not null)
                {
                    node.AddProperty(key, KVValue.MakeArray(inputNodeIds.Select(MakeInputConnection)));
                }
                continue;
            }

            if (key is "m_tags")
            {
                if (className is "CSequence" or "CCycleControlClip" or "CBlend2D")
                {
                    try
                    {
                        var compiledTagSpans = compiledNode.GetArray("m_tags");
                        var tagSpans = new List<KVObject>();

                        foreach (var compiledTagSpan in compiledTagSpans)
                        {
                            var tagIndex = compiledTagSpan.GetIntegerProperty("m_tagIndex");
                            var startCycle = compiledTagSpan.GetFloatProperty("m_startCycle");
                            var endCycle = compiledTagSpan.GetFloatProperty("m_endCycle");
                            var duration = endCycle - startCycle;
                            var tagId = -1L;
                            if (tagIndex >= 0 && tagIndex < Tags.Length)
                            {
                                tagId = Tags[tagIndex].GetSubCollection("m_tagID").GetIntegerProperty("m_id");
                            }
                            var tagSpan = MakeNode("CAnimTagSpan");
                            tagSpan.AddProperty("m_id", MakeNodeIdObjectValue(tagId));
                            tagSpan.AddProperty("m_fStartCycle", startCycle);
                            tagSpan.AddProperty("m_fDuration", duration);
                            tagSpans.Add(tagSpan);
                        }

                        node.AddProperty("m_tagSpans", KVValue.MakeArray(tagSpans.ToArray()));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error converting tag spans for {className}: {ex.Message}");
                        node.AddProperty("m_tagSpans", KVValue.MakeArray(Array.Empty<KVObject>()));
                    }
                    continue;
                }
                else
                {
                    try
                    {
                        var tagIds = compiledNode.GetIntegerArray(key);
                        node.AddProperty(key, KVValue.MakeArray(tagIds.Select(MakeNodeIdObjectValue)));
                        continue;
                    }
                    catch (InvalidCastException)
                    {
                        Console.WriteLine(className + " m_tags is in unexpected format");
                        continue;
                    }
                }
            }

            if (key is "m_paramSpans")
            {
                try
                {
                    var compiledParamSpans = compiledNode.GetSubCollection("m_paramSpans");
                    if (compiledParamSpans != null && compiledParamSpans.ContainsKey("m_spans"))
                    {
                        var compiledSpans = compiledParamSpans.GetArray("m_spans");
                        var paramSpans = new List<KVObject>();
                        foreach (var compiledSpan in compiledSpans)
                        {
                            var paramSpan = MakeNode("CAnimParamSpan");
                            if (compiledSpan.ContainsKey("m_samples"))
                            {
                                paramSpan.AddProperty("m_samples", compiledSpan.GetProperty<KVObject>("m_samples"));
                            }
                            if (compiledSpan.ContainsKey("m_hParam"))
                            {
                                var paramHandle = compiledSpan.GetSubCollection("m_hParam");
                                var paramType = paramHandle.GetStringProperty("m_type");
                                var paramIndex = paramHandle.GetIntegerProperty("m_index");
                                var paramIdValue = ParameterIDFromIndex(paramType, paramIndex);
                                paramSpan.AddProperty("m_id", paramIdValue);
                            }
                            if (compiledSpan.ContainsKey("m_flStartCycle"))
                            {
                                paramSpan.AddProperty("m_flStartCycle", compiledSpan.GetFloatProperty("m_flStartCycle"));
                            }
                            if (compiledSpan.ContainsKey("m_flEndCycle"))
                            {
                                paramSpan.AddProperty("m_flEndCycle", compiledSpan.GetFloatProperty("m_flEndCycle"));
                            }
                            paramSpans.Add(paramSpan);
                        }
                        node.AddProperty("m_paramSpans", KVValue.MakeArray(paramSpans.ToArray()));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error converting param spans for {className}: {ex.Message}");
                    node.AddProperty("m_paramSpans", KVValue.MakeArray(Array.Empty<KVObject>()));
                }
                continue;
            }

            if (key is "m_paramIndex")
            {
                var paramRef = subCollection.Value;
                var paramType = paramRef.GetStringProperty("m_type");
                var paramIndex = paramRef.GetIntegerProperty("m_index");
                node.AddProperty("m_paramID", ParameterIDFromIndex(paramType, paramIndex));
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

            var states = ConvertStateMachine(stateMachine, stateData, transitionData, isComponent: false);
            node.AddProperty("m_states", KVValue.MakeArray(states));
        }

        return node;
    }

    private KVValue ParameterIDFromIndex(string paramType, long paramIndex, bool requireFloat = false)
    {
        if (paramIndex == 255)
        {
            return MakeNodeIdObjectValue(-1);
        }

        string uncompiledType = paramType.Replace("ANIMPARAM_", "");
        int currentCount = 0;
        for (int i = 0; i < Parameters.Length; i++)
        {
            var parameter = Parameters[i];
            var paramClass = parameter.GetStringProperty("_class");
            string paramTypeName = paramClass switch
            {
                "CFloatAnimParameter" => "FLOAT",
                "CEnumAnimParameter" => "ENUM",
                "CBoolAnimParameter" => "BOOL",
                "CIntAnimParameter" => "INTEGER",
                "CVectorAnimParameter" => "VECTOR",
                "CQuaternionAnimParameter" => "QUATERNION",
                "CSymbolAnimParameter" => "SYMBOL",
                _ => paramClass.Replace("C", "").Replace("AnimParameter", "").ToUpper(System.Globalization.CultureInfo.CurrentCulture)
            };
            if (paramTypeName == uncompiledType)
            {
                if (currentCount == paramIndex)
                {
                    long id = parameter.GetSubCollection("m_id").GetIntegerProperty("m_id");
                    return MakeNodeIdObjectValue(id);
                }
                currentCount++;
            }
        }
        if (requireFloat && uncompiledType != "FLOAT")
        {
            foreach (var parameter in Parameters)
            {
                if (parameter.GetStringProperty("_class") == "CFloatAnimParameter")
                {
                    long id = parameter.GetSubCollection("m_id").GetIntegerProperty("m_id");
                    return MakeNodeIdObjectValue(id);
                }
            }
        }
        return MakeNodeIdObjectValue(-1);
    }
    private KVValue ParameterIDFromIndexForFloat(long paramIndex)
    {
        return ParameterIDFromIndex("ANIMPARAM_FLOAT", paramIndex, requireFloat: true);
    }
}
