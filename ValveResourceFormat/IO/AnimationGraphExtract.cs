using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;
using System.Collections.Generic;

namespace ValveResourceFormat.IO;

/// <summary>
/// Extracts and converts animation graph resources to editable format.
/// </summary>
public class AnimationGraphExtract
{
    private readonly BinaryKV3 resourceData;
    private KVObject graph => resourceData.Data;
    private readonly string? outputFileName;
    private int nodePositionOffset = 0;

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

        KVObject scriptManager = null;
        if (data.ContainsKey("m_scriptManager"))
        {
            scriptManager = data.GetSubCollection("m_scriptManager");
        }

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

        var nodeManager = MakeListNode("CAnimNodeManager", "m_nodes");
        var componentList = new List<KVObject>();
        if (data.ContainsKey("m_components"))
        {
            var compiledComponents = data.GetArray("m_components");
            foreach (var compiledComponent in compiledComponents)
            {
                try
                {
                    var componentData = ConvertComponent(compiledComponent, scriptManager);
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
        var source = paramId == 4294967295 ? "Constant" : "Parameter";
        blendDuration.AddProperty("m_eSource", source);

        return blendDuration;
    }
    private KVObject ConvertComponent(KVObject compiledComponent, KVObject scriptManager)
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
                var stateMachine = compiledComponent.GetSubCollection("m_stateMachine");
                var states = ConvertStateMachineStates(stateMachine, scriptManager);
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

    private KVObject[] ConvertStateMachineStates(KVObject compiledStateMachine, KVObject scriptManager)
    {
        Console.WriteLine($"Converting state machine, scriptManager is null: {scriptManager == null}");
        var compiledStates = compiledStateMachine.GetArray("m_states");
        var compiledTransitions = compiledStateMachine.GetArray("m_transitions");

        // Parse scripts first to get condition mapping
        var scriptConditions = new Dictionary<long, List<KVObject[]>>();
        if (scriptManager != null && scriptManager.ContainsKey("m_scriptInfo"))
        {
            var scriptInfos = scriptManager.GetArray("m_scriptInfo");
            Console.WriteLine($"Found {scriptInfos.Length} scripts in script manager");
            for (int scriptIndex = 0; scriptIndex < scriptInfos.Length; scriptIndex++)
            {
                var scriptInfo = scriptInfos[scriptIndex];
                var code = scriptInfo.GetStringProperty("m_code");
                Console.WriteLine($"Script {scriptIndex}: {code}");
                var conditions = ParseScriptConditions(code);
                scriptConditions[scriptIndex] = conditions;
            }
        }

        var states = new KVObject[compiledStates.Length];

        // First pass: create states
        for (int i = 0; i < compiledStates.Length; i++)
        {
            var compiledState = compiledStates[i];
            var stateId = compiledState.GetSubCollection("m_stateID");
            var stateName = compiledState.GetStringProperty("m_name");
            var isStart = compiledState.GetIntegerProperty("m_bIsStartState") > 0;
            var isEnd = compiledState.GetIntegerProperty("m_bIsEndState") > 0;
            var isPassthrough = compiledState.GetIntegerProperty("m_bIsPassthrough") > 0;

            var state = MakeNode("CAnimComponentState");
            state.AddProperty("m_name", stateName);
            state.AddProperty("m_stateID", stateId);
            state.AddProperty("m_bIsStartState", isStart);
            state.AddProperty("m_bIsEndtState", isEnd); // Note: typo in source format
            state.AddProperty("m_bIsPassthrough", isPassthrough);

            // Create transitions
            if (compiledState.ContainsKey("m_transitionIndices"))
            {
                var transitionIndices = compiledState.GetIntegerArray("m_transitionIndices");
                var transitions = new List<KVObject>();

                // Get script index for this state
                var scriptIndex = compiledState.GetSubCollection("m_hScript").GetIntegerProperty("m_id");

                // Get conditions for this script
                var conditionsForState = scriptConditions.ContainsKey(scriptIndex) ?
                    scriptConditions[scriptIndex] : new List<KVObject[]>();

                for (int ti = 0; ti < transitionIndices.Length; ti++)
                {
                    var transitionIndex = transitionIndices[ti];
                    if (transitionIndex >= 0 && transitionIndex < compiledTransitions.Length)
                    {
                        var compiledTransition = compiledTransitions[transitionIndex];
                        var transition = CreateTransition(compiledTransition, compiledStates, ti, conditionsForState);
                        transitions.Add(transition);
                    }
                }

                state.AddProperty("m_transitions", KVValue.MakeArray(transitions.ToArray()));
            }

            state.AddProperty("m_actions", KVValue.MakeArray(Array.Empty<KVObject>()));

            states[i] = state;
        }

        return states;
    }

    private KVObject CreateTransition(KVObject compiledTransition, KVObject[] compiledStates, int conditionIndex, List<KVObject[]> conditionsForState)
    {
        var srcStateIndex = compiledTransition.GetIntegerProperty("m_srcStateIndex");
        var destStateIndex = compiledTransition.GetIntegerProperty("m_destStateIndex");
        var disabled = compiledTransition.GetIntegerProperty("m_bDisabled") > 0;

        var srcStateId = compiledStates[srcStateIndex].GetSubCollection("m_stateID");
        var destStateId = compiledStates[destStateIndex].GetSubCollection("m_stateID");

        var transition = MakeNode("CAnimComponentStateTransition");
        transition.AddProperty("m_srcState", srcStateId);
        transition.AddProperty("m_destState", destStateId);
        transition.AddProperty("m_bDisabled", disabled);

        var conditionList = MakeNode("CConditionContainer");
        var conditionsArray = new KVObject(null, isArray: true, 0);

        if (conditionIndex < conditionsForState.Count)
        {
            var conditionGroup = conditionsForState[conditionIndex];
            if (conditionGroup != null)
            {
                foreach (var condition in conditionGroup)
                {
                    if (condition != null)
                    {
                        conditionsArray.AddItem(condition);
                    }
                }
            }
        }
        conditionList.AddProperty("m_conditions", conditionsArray);
        transition.AddProperty("m_conditionList", conditionList);
        return transition;
    }

    private List<KVObject[]> ParseScriptConditions(string code)
    {
        var result = new List<KVObject[]>();
        if (string.IsNullOrEmpty(code))
        {
            return result;
        }
        var cleanCode = code.Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", "");
        try
        {
            // Parse the entire expression recursively
            ParseTernaryBranches(cleanCode, result, 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing script code: {ex.Message}");
            Console.WriteLine($"Code: {cleanCode}");
        }
        return result;
    }

    private void ParseTernaryBranches(string expression, List<KVObject[]> result, int startDepth)
    {
        // Base case: empty or just (-1)
        if (string.IsNullOrEmpty(expression) || expression == "(-1)")
        {
            return;
        }

        Console.WriteLine($"Parsing expression at depth {startDepth}: {expression}");

        // Extract the outermost condition
        int depth = 0;
        int conditionStart = -1;
        int conditionEnd = -1;

        for (int i = 0; i < expression.Length; i++)
        {
            if (expression[i] == '(')
            {
                if (depth == 0)
                {
                    conditionStart = i;
                }
                depth++;
            }
            else if (expression[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    conditionEnd = i;
                    break;
                }
            }
        }

        if (conditionStart == -1 || conditionEnd == -1)
        {
            Console.WriteLine($"No complete condition found in: {expression}");
            return;
        }

        // Extract condition without outer parentheses
        string condition = expression.Substring(conditionStart + 1, conditionEnd - conditionStart - 1);
        Console.WriteLine($"Extracted condition: {condition}");

        // Find the question mark after this condition
        int questionMarkPos = expression.IndexOf('?', conditionEnd);
        if (questionMarkPos == -1)
        {
            Console.WriteLine($"No question mark found after condition in: {expression}");
            return;
        }

        // Find the matching colon for this ternary
        depth = 0;
        int colonPos = -1;
        for (int i = questionMarkPos; i < expression.Length; i++)
        {
            if (expression[i] == '(')
            {
                depth++;
            }
            else if (expression[i] == ')')
            {
                depth--;
            }
            else if (expression[i] == ':' && depth == 0)
            {
                colonPos = i;
                break;
            }
        }

        if (colonPos == -1)
        {
            Console.WriteLine($"No colon found in: {expression}");
            return;
        }

        // Extract the transition index
        string indexStr = expression.Substring(questionMarkPos + 1, colonPos - questionMarkPos - 1);
        if (!int.TryParse(indexStr, out int transitionIndex))
        {
            Console.WriteLine($"Could not parse transition index from: {indexStr}");
            return;
        }

        Console.WriteLine($"Transition index: {transitionIndex}");

        // Parse the condition
        var parsedCondition = ParseCondition(condition);
        if (parsedCondition != null)
        {
            // Ensure we have enough slots
            while (result.Count <= transitionIndex)
            {
                result.Add(Array.Empty<KVObject>());
            }

            result[transitionIndex] = [parsedCondition];
            Console.WriteLine($"Added condition for transition {transitionIndex}");
        }

        // Parse the false branch (everything after the colon)
        string falseBranch = expression.Substring(colonPos + 1);
        Console.WriteLine($"False branch: {falseBranch}");

        // Recursively parse the false branch
        ParseTernaryBranches(falseBranch, result, startDepth + 1);
    }

    private void ParseTernaryExpression(string expression, List<KVObject[]> result)
    {
        // Base case: (-1)
        if (expression == "(-1)")
        {
            return;
        }

        // Find the condition part (first balanced parentheses)
        int depth = 0;
        int conditionEnd = -1;
        for (int i = 0; i < expression.Length; i++)
        {
            if (expression[i] == '(')
            {
                depth++;
            }
            else if (expression[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    conditionEnd = i;
                    break;
                }
            }
        }

        if (conditionEnd == -1) return;

        // Extract condition (without outer parentheses)
        var conditionStr = expression.Substring(1, conditionEnd - 1);

        // Find the question mark
        var questionMarkPos = expression.IndexOf('?', conditionEnd);
        if (questionMarkPos == -1) return;

        // Find the colon after the index
        var colonPos = expression.IndexOf(':', questionMarkPos);
        if (colonPos == -1) return;

        // Extract transition index
        var indexStr = expression.Substring(questionMarkPos + 1, colonPos - questionMarkPos - 1);
        if (!int.TryParse(indexStr, out int transitionIndex))
        {
            return;
        }

        // Parse condition
        var condition = ParseCondition(conditionStr);

        // Ensure we have enough slots in the result
        while (result.Count <= transitionIndex)
        {
            result.Add(Array.Empty<KVObject>());
        }

        result[transitionIndex] = [condition];

        // Parse the rest recursively
        var rest = expression.Substring(colonPos + 1);
        ParseTernaryExpression(rest, result);
    }

    private KVObject ParseCondition(string condition)
    {
        if (string.IsNullOrEmpty(condition))
        {
            return null;
        }

        // Check for tag condition first
        if (condition.Contains("IsTagActive"))
        {
            return ParseTagCondition(condition);
        }

        // Handle parameter conditions
        return ParseParameterCondition(condition);
    }

    private KVObject ParseTagCondition(string condition)
    {
        if (string.IsNullOrEmpty(condition))
        {
            Console.WriteLine("Tag condition is null or empty");
            return null;
        }

        // Example: IsTagActive(TAG_TEST_TAG) or !IsTagActive(TAG_TEST_TAG)
        var isNegated = condition.StartsWith('!');
        var conditionToParse = isNegated ? condition.Substring(1) : condition;

        Console.WriteLine($"Parsing tag condition: '{condition}', isNegated: {isNegated}");

        var start = conditionToParse.IndexOf('(');
        var end = conditionToParse.LastIndexOf(')');

        if (start == -1 || end == -1 || start >= end)
        {
            Console.WriteLine($"Invalid tag condition format: {condition}");
            return null;
        }

        var tagName = conditionToParse.Substring(start + 1, end - start - 1);
        Console.WriteLine($"Raw tag name: '{tagName}'");

        // Normalize tag name: remove "TAG_" prefix and replace underscores with spaces
        var normalizedTagName = tagName;
        if (normalizedTagName.StartsWith("TAG_"))
        {
            normalizedTagName = normalizedTagName["TAG_".Length..];
            Console.WriteLine($"After removing TAG_ prefix: '{normalizedTagName}'");
        }
        normalizedTagName = normalizedTagName.Replace('_', ' ');
        Console.WriteLine($"Normalized tag name: '{normalizedTagName}'");

        // Find tag by name - try normalized name first, then original
        var tag = Tags.FirstOrDefault(t =>
            string.Equals(t.GetStringProperty("m_name"), normalizedTagName, StringComparison.OrdinalIgnoreCase));

        if (tag == null)
        {
            Console.WriteLine($"Tag '{normalizedTagName}' not found, trying original name '{tagName}'");
            tag = Tags.FirstOrDefault(t =>
                string.Equals(t.GetStringProperty("m_name"), tagName, StringComparison.OrdinalIgnoreCase));
        }

        if (tag == null)
        {
            Console.WriteLine($"Tag '{tagName}' (normalized: '{normalizedTagName}') not found in Tags array (count: {Tags.Length})");
            foreach (var t in Tags)
            {
                Console.WriteLine($"  Available: '{t.GetStringProperty("m_name")}'");
            }
            return null;
        }

        var tagId = tag.GetSubCollection("m_tagID").GetIntegerProperty("m_id");
        Console.WriteLine($"Found tag: name='{tag.GetStringProperty("m_name")}', id={tagId}");

        var tagCondition = MakeNode("CTagCondition");
        tagCondition.AddProperty("m_tagID", MakeNodeIdObjectValue(tagId));
        tagCondition.AddProperty("m_comparisonValue", !isNegated);

        Console.WriteLine($"Successfully created tag condition");
        return tagCondition;
    }

    private KVObject ParseParameterCondition(string condition)
    {
        if (string.IsNullOrEmpty(condition))
        {
            return null;
        }

        // Find operator
        string[] operators = ["==", "!=", "<", ">", "<=", ">="];
        string foundOperator = null;
        int operatorPos = -1;

        foreach (var op in operators)
        {
            operatorPos = condition.IndexOf(op);
            if (operatorPos != -1)
            {
                foundOperator = op;
                break;
            }
        }

        if (foundOperator == null || operatorPos == -1)
        {
            return null;
        }

        var paramName = condition.Substring(0, operatorPos).Trim();
        var valuePart = condition.Substring(operatorPos + foundOperator.Length).Trim();

        // Remove parentheses from value if present
        if (valuePart.StartsWith('(') && valuePart.EndsWith(')'))
        {
            valuePart = valuePart[1..^1];
        }

        // Normalize parameter name: replace underscores with spaces
        var normalizedParamName = paramName.Replace('_', ' ');
        Console.WriteLine($"Looking for parameter: '{paramName}' (normalized to '{normalizedParamName}')");

        // Find parameter - try normalized name first, then original
        var parameter = Parameters.FirstOrDefault(p =>
            string.Equals(p.GetStringProperty("m_name"), normalizedParamName, StringComparison.OrdinalIgnoreCase));

        if (parameter == null)
        {
            // Try the original name as fallback
            parameter = Parameters.FirstOrDefault(p =>
                string.Equals(p.GetStringProperty("m_name"), paramName, StringComparison.OrdinalIgnoreCase));
        }

        if (parameter == null)
        {
            Console.WriteLine($"Parameter '{paramName}' (normalized: '{normalizedParamName}') not found in Parameters array");
            return null;
        }

        var paramId = parameter.GetSubCollection("m_id").GetIntegerProperty("m_id");
        var paramType = parameter.GetStringProperty("m_eType");

        // Map operator
        var operatorMap = new Dictionary<string, string>
        {
            ["=="] = "COMPARISON_EQUALS",
            ["!="] = "COMPARISON_NOT_EQUALS",
            [">"] = "COMPARISON_GREATER",
            ["<"] = "COMPARISON_LESS",
            [">="] = "COMPARISON_GREATER_OR_EQUAL",
            ["<="] = "COMPARISON_LESS_OR_EQUAL",
        };

        var comparisonOp = operatorMap.GetValueOrDefault(foundOperator, "COMPARISON_EQUALS");

        // Parse value based on parameter type
        var typedValue = CreateTypedValue(valuePart, paramType, parameter);
        var comparisonString = GetComparisonString(valuePart, paramType, parameter);

        var paramCondition = MakeNode("CParameterCondition");
        paramCondition.AddProperty("m_paramID", MakeNodeIdObjectValue(paramId));
        paramCondition.AddProperty("m_comparisonOp", comparisonOp);
        paramCondition.AddProperty("m_comparisonValue", typedValue);
        paramCondition.AddProperty("m_comparisonString", comparisonString);

        return paramCondition;
    }

    private KVObject CreateTypedValue(string valueString, string paramType, KVObject parameter)
    {
        var typedValue = new KVObject(null, 2);

        int typeCode = paramType switch
        {
            "BOOL" => 1,
            "INTEGER" => 2,
            "STRING" => 3,
            "FLOAT" => 4,
            "ENUM" => 2, // ENUM uses integer type
            "VECTOR" => 5,
            "QUATERNION" => 6,
            _ => 0
        };

        object data;

        try
        {
            data = paramType switch
            {
                "BOOL" => valueString == "1",
                "INTEGER" => int.Parse(valueString),
                "FLOAT" => float.Parse(valueString),
                "ENUM" => int.Parse(valueString),
                _ => valueString
            };
        }
        catch
        {
            Console.WriteLine($"Error parsing value '{valueString}' for type '{paramType}'");
            data = paramType == "BOOL" ? false : 0;
        }

        typedValue.AddProperty("m_nType", typeCode);
        typedValue.AddProperty("m_data", data);

        return typedValue;
    }

    private string GetComparisonString(string valueString, string paramType, KVObject parameter)
    {
        Console.WriteLine($"Getting comparison string for value '{valueString}', type '{paramType}'");

        if (paramType == "BOOL")
        {
            return valueString == "1" ? "true" : "false";
        }

        if (paramType == "ENUM")
        {
            return GetEnumComparisonString(valueString, parameter);
        }

        return valueString;
    }

    private string GetEnumComparisonString(string valueString, KVObject parameter)
    {
        Console.WriteLine($"Getting enum comparison string for '{valueString}'");

        if (!parameter.ContainsKey("m_enumOptions"))
        {
            Console.WriteLine($"No enum options found for parameter '{parameter.GetStringProperty("m_name")}'");
            return valueString;
        }

        var enumOptions = parameter.GetArray<string>("m_enumOptions");
        if (!int.TryParse(valueString, out int enumValue))
        {
            Console.WriteLine($"Could not parse enum value '{valueString}' as integer");
            return valueString;
        }

        if (enumValue >= 0 && enumValue < enumOptions.Length)
        {
            var optionName = enumOptions[enumValue];
            var paramName = parameter.GetStringProperty("m_name").ToUpper();
            var result = $"ENUM_{paramName}_{optionName.ToUpper()}";
            Console.WriteLine($"Enum comparison string: {result}");
            return result;
        }

        Console.WriteLine($"Enum value {enumValue} out of range (0-{enumOptions.Length - 1})");
        return valueString;
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
                    or "m_paramSpans" // todo
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
                float stateX = -150.0f * (nodePositionOffset + 1);
                float stateY = 100.0f * (nodePositionOffset + 1);
                var random = new Random(nodePositionOffset);
                stateY += random.Next(-50, 51);
                stateNode.AddProperty("m_vecPosition", MakeVector2(stateX, stateY));
                nodePositionOffset++;

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
                    float transitionX = -150.0f * (nodePositionOffset + 1);
                    float transitionY = 100.0f * (nodePositionOffset + 1);
                    var transRandom = new Random(nodePositionOffset);
                    transitionY += transRandom.Next(-50, 51);
                    transitionNode.AddProperty("m_vecPosition", MakeVector2(transitionX, transitionY));
                    nodePositionOffset++;

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
                        float actionX = -150.0f * (nodePositionOffset + 1);
                        float actionY = 100.0f * (nodePositionOffset + 1);
                        var actionRandom = new Random(nodePositionOffset);
                        actionY += actionRandom.Next(-50, 51);
                        uncompiledAction.AddProperty("m_vecPosition", MakeVector2(actionX, actionY));
                        nodePositionOffset++;

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
                                    var paramRef = action.GetSubCollection("m_hParam");
                                    var paramType = paramRef.GetStringProperty("m_type");
                                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                                    actionData.AddProperty("m_param", ParameterIDFromIndex(paramType, paramIndex));
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
                "CSymbolAnimParameter" => "SYMBOL",
                _ => paramClass.Replace("C", "").Replace("AnimParameter", "").ToUpper()
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
