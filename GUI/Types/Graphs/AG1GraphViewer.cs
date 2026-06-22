using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using GUI.Utils;
using SkiaSharp;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Graphs;

/// <summary>
/// Graph viewer for compiled AG1 (Animgraph1) files.
/// </summary>
internal class AG1GraphViewer : GLNodeGraphViewer
{
    private readonly KVObject animGraphData;
    private readonly IReadOnlyList<KVObject> compiledNodes;
    private readonly Dictionary<int, Node> nodeMap = new();
    private readonly Dictionary<int, KVObject> parameterIndexToObject = new();
    private readonly Dictionary<int, string> parameterIndexToName = new();
    private readonly Dictionary<KVObject, int> parameterObjectToIndex = new();
    private readonly Dictionary<string, List<KVObject>> typeToParameters = new();
    private List<KVObject> tags = new();
    private Dictionary<int, string> tagIndexToName = new();
    private List<KVObject> components = new();

    // Fixed colors per node type (AG1 specific)
    private static SKColor NodeColor { get; set; } = new SKColor(60, 60, 60);
    private static SKColor NodeTextColor { get; set; } = new SKColor(230, 230, 230);
    private static readonly SKColor ClientSimulateColor = new SKColor(118, 75, 140);

    //Animation
    private static SKColor SequenceColor { get; set; } = new SKColor(62, 43, 89);
    private static SKColor SingleFrameColor { get; set; } = new SKColor(111, 53, 195);
    private static SKColor ChoiceColor { get; set; } = new SKColor(102, 111, 49);
    private static SKColor SelectorColor { get; set; } = new SKColor(34, 84, 135);
    private static SKColor MotionMatchingColor { get; set; } = new SKColor(127, 145, 218);
    //Blend
    private static SKColor BlendColor { get; set; } = new SKColor(45, 102, 82);
    private static SKColor Blend2DColor { get; set; } = new SKColor(7, 62, 42);
    private static SKColor BoneMaskColor { get; set; } = new SKColor(116, 51, 75);
    private static SKColor AddSubtractColor { get; set; } = new SKColor(126, 60, 55);
    private static SKColor AimLeanMatrixColor { get; set; } = new SKColor(99, 43, 87);
    // Constraints and Procedural
    private static SKColor CycleControlColor { get; set; } = new SKColor(108, 138, 61);
    private static SKColor CycleControlClipColor { get; set; } = new SKColor(87, 52, 127);
    private static SKColor FollowAttachmentColor { get; set; } = new SKColor(0, 161, 143);
    private static SKColor FollowPathColor { get; set; } = new SKColor(27, 61, 82);
    private static SKColor ConstraintLightGreenColor { get; set; } = new SKColor(91, 143, 56);
    private static SKColor JiggleBoneColor { get; set; } = new SKColor(87, 175, 122);
    private static SKColor ConstraintDarkGreenColor { get; set; } = new SKColor(44, 106, 31);
    // System
    private static SKColor ChoreoColor { get; set; } = new SKColor(123, 78, 35);
    private static SKColor SystemYellowColor { get; set; } = new SKColor(78, 73, 29);
    private static SKColor InputStreamColor { get; set; } = new SKColor(166, 0, 207);
    private static SKColor SystemDarkBlueColor { get; set; } = new SKColor(24, 54, 72);
    private static SKColor SystemGrayColor { get; set; } = new SKColor(65, 90, 114);
    private static SKColor SlowDownOnSlopesColor { get; set; } = new SKColor(26, 59, 18);
    private static SKColor SteamVRSkeletalInputColor { get; set; } = new SKColor(15, 101, 144);
    private static SKColor TurnHelperColor { get; set; } = new SKColor(32, 30, 78);
    // Main
    private static SKColor StateMachineColor { get; set; } = new SKColor(44, 57, 89);
    private static SKColor FinalPoseColor { get; set; } = new SKColor(149, 121, 65);
    private static SKColor PoseColor { get; set; } = new SKColor(173, 255, 47);          // Default fallback

    private static readonly SKColor ParamBoolColor = new SKColor(25, 78, 101);
    private static readonly SKColor ParamIntColor = new SKColor(145, 102, 54);
    private static readonly SKColor ParamFloatColor = new SKColor(163, 171, 37);
    private static readonly SKColor ParamEnumColor = new SKColor(89, 27, 111);
    private static readonly SKColor ParamVectorColor = new SKColor(26, 122, 59);
    private static readonly SKColor ParamQuaternionColor = new SKColor(123, 58, 60);
    private static readonly SKColor ParamSymbolColor = new SKColor(200, 200, 200);
    private static readonly SKColor ParamVirtualColor = new SKColor(150, 150, 150);
    private static readonly SKColor ParamUnknownColor = new SKColor(128, 128, 128);
    private static readonly SKColor TagColor = new SKColor(100, 200, 200);

    private static readonly Dictionary<string, SKColor> TagClassColors = new(StringComparer.Ordinal)
    {
        ["CAudioAnimTag"] = new SKColor(141, 59, 9),
        ["CBodyGroupAnimTag"] = new SKColor(26, 122, 59),
        ["CClothSettingsAnimTag"] = new SKColor(117, 124, 136),
        ["CFootFallAnimTag"] = new SKColor(150, 255, 150),
        ["CFootstepLandedAnimTag"] = new SKColor(71, 71, 34),
        ["CMaterialAttributeAnimTag"] = new SKColor(16, 157, 205),
        ["CParticleAnimTag"] = new SKColor(118, 78, 153),
        ["CRagdollAnimTag"] = new SKColor(221, 209, 34),
        ["CSequenceFinishedAnimTag"] = new SKColor(200, 180, 255),
        ["CStringAnimTag"] = new SKColor(163, 22, 99),
        ["CTaskStatusAnimTag"] = new SKColor(38, 111, 118),
        ["CWarpSectionAnimTag"] = new SKColor(200, 220, 150),
        ["CMovementHandshakeAnimTag"] = new SKColor(38, 111, 118),
        ["CTaskHandshakeAnimTag"] = new SKColor(38, 111, 118),
    };

    private static readonly Dictionary<string, SKColor> ComponentClassColors = new(StringComparer.Ordinal)
    {
        ["CActionComponentUpdater"] = new SKColor(230, 180, 120),
        ["CAnimScriptComponentUpdater"] = new SKColor(180, 200, 230),
        ["CCPPScriptComponentUpdater"] = new SKColor(200, 180, 230),
        ["CDampedValueComponentUpdater"] = new SKColor(180, 230, 180),
        ["CDemoSettingsComponentUpdater"] = new SKColor(230, 200, 180),
        ["CLODComponentUpdater"] = new SKColor(200, 200, 200),
        ["CLookComponentUpdater"] = new SKColor(150, 200, 230),
        ["CMovementComponentUpdater"] = new SKColor(230, 180, 180),
        ["CPairedSequenceComponentUpdater"] = new SKColor(180, 180, 230),
        ["CRagdollComponentUpdater"] = new SKColor(200, 150, 150),
        ["CRemapValueComponentUpdater"] = new SKColor(150, 200, 150),
        ["CSlopeComponentUpdater"] = new SKColor(200, 200, 150),
        ["CStateMachineComponentUpdater"] = new SKColor(44, 57, 89),
    };

    private static SKColor GetComponentClassColor(string className)
    {
        return ComponentClassColors.TryGetValue(className, out var color) ? color : new SKColor(128, 128, 128);
    }

    private static SKColor GetTagClassColor(string className)
    {
        return TagClassColors.TryGetValue(className, out var color) ? color : TagColor;
    }

    // ---- Dictionaries for class-based lookups ----
    private static readonly Dictionary<string, string> ClassDisplayName = new(StringComparer.Ordinal)
    {
        ["CSequenceUpdateNode"] = "Sequence",
        ["CChoiceUpdateNode"] = "Choice",
        ["CMotionMatchingUpdateNode"] = "Motion Matching",
        ["CSelectorUpdateNode"] = "Selector",
        ["CSingleFrameUpdateNode"] = "Single Frame",
        ["CDirectionalBlendUpdateNode"] = "Directional Blend",
        ["CBlendUpdateNode"] = "Blend",
        ["CBlend2DUpdateNode"] = "Blend 2D",
        ["CAddUpdateNode"] = "Add",
        ["CSubtractUpdateNode"] = "Subtract",
        ["CAimMatrixUpdateNode"] = "Aim Matrix",
        ["CLeanMatrixUpdateNode"] = "Lean Matrix",
        ["CBoneMaskUpdateNode"] = "Bone Mask",
        ["CCycleControlUpdateNode"] = "Cycle Control",
        ["CCycleControlClipUpdateNode"] = "Cycle Control Clip",
        ["CFollowAttachmentUpdateNode"] = "Follow Attachment",
        ["CFollowPathUpdateNode"] = "Follow Path",
        ["CFootPinningUpdateNode"] = "Foot Pinning",
        ["CLookAtUpdateNode"] = "Look At",
        ["CHitReactUpdateNode"] = "Hit React",
        ["CFootLockUpdateNode"] = "Foot Lock",
        ["CJiggleBoneUpdateNode"] = "Jiggle Bone",
        ["CSolveIKChainUpdateNode"] = "Solve IK Chain",
        ["CTwoBoneIKUpdateNode"] = "Two Bone IK",
        ["CSpeedScaleUpdateNode"] = "Speed Scale",
        ["CChoreoUpdateNode"] = "Choreo",
        ["CDirectPlaybackUpdateNode"] = "Direct Playback",
        ["CFootStepTriggerUpdateNode"] = "Foot Step Trigger",
        ["CInputStreamUpdateNode"] = "Input Stream",
        ["CMoverUpdateNode"] = "Mover",
        ["CStopAtGoalUpdateNode"] = "Stop At Goal",
        ["CPathHelperUpdateNode"] = "Path Helper",
        ["CSetFacingUpdateNode"] = "Set Facing",
        ["CSlowDownOnSlopesUpdateNode"] = "Slow Down On Slopes",
        ["CSkeletalInputUpdateNode"] = "Skeletal Input",
        ["CTurnHelperUpdateNode"] = "Turn Helper",
        ["CRootUpdateNode"] = "Final Pose",
        ["CStateMachineUpdateNode"] = "State Machine",
    };

    private static readonly Dictionary<string, string> ComponentDisplayName = new(StringComparer.Ordinal)
    {
        ["CActionComponentUpdater"] = "Action Component",
        ["CAnimScriptComponentUpdater"] = "AnimScript Component",
        ["CCPPScriptComponentUpdater"] = "PPScript Component",
        ["CDampedValueComponentUpdater"] = "Damped Value Component",
        ["CDemoSettingsComponentUpdater"] = "Demo Settings Component",
        ["CLODComponentUpdater"] = "LOD Component",
        ["CLookComponentUpdater"] = "Look Component",
        ["CMovementComponentUpdater"] = "Movement Component",
        ["CPairedSequenceComponentUpdater"] = "Paired Sequence Component",
        ["CRagdollComponentUpdater"] = "Ragdoll Component",
        ["CRemapValueComponentUpdater"] = "Remap Value Component",
        ["CSlopeComponentUpdater"] = "Slope Component",
        ["CStateMachineComponentUpdater"] = "State Machine Component",
    };

    private static readonly Dictionary<string, string> TagDisplayName = new(StringComparer.Ordinal)
    {
        ["CAudioAnimTag"] = "Audio Tag",
        ["CBodyGroupAnimTag"] = "Body Group Tag",
        ["CClothSettingsAnimTag"] = "Cloth Settings Tag",
        ["CFootFallAnimTag"] = "FootFall Tag",
        ["CFootstepLandedAnimTag"] = "FootstepLanded Tag",
        ["CMaterialAttributeAnimTag"] = "Material Attribute Tag",
        ["CParticleAnimTag"] = "Particle Tag",
        ["CRagdollAnimTag"] = "Ragdoll Tag",
        ["CSequenceFinishedAnimTag"] = "Sequence Finished Tag",
        ["CStringAnimTag"] = "String/Internal Tag",
        ["CTaskStatusAnimTag"] = "Status Tag",
        ["CWarpSectionAnimTag"] = "Warp Section Tag",
        ["CMovementHandshakeAnimTag"] = "Movement Handshake Tag",
        ["CTaskHandshakeAnimTag"] = "Task Handshake Tag",
    };

    private static readonly Dictionary<string, string> ParameterTypeDisplayName = new(StringComparer.Ordinal)
    {
        ["BOOL"] = "Boolean",
        ["INT"] = "Integer",
        ["FLOAT"] = "Float",
        ["ENUM"] = "Enum",
        ["VECTOR"] = "Vector",
        ["QUATERNION"] = "Quaternion",
        ["SYMBOL"] = "Symbol",
        ["VIRTUAL"] = "Virtual",
        ["UNKNOWN"] = "Unknown",
    };

    private static readonly Dictionary<string, SKColor> ClassColor = new(StringComparer.Ordinal)
    {
        ["CSequenceUpdateNode"] = SequenceColor,
        ["CChoiceUpdateNode"] = ChoiceColor,
        ["CMotionMatchingUpdateNode"] = MotionMatchingColor,
        ["CSelectorUpdateNode"] = SelectorColor,
        ["CSingleFrameUpdateNode"] = SingleFrameColor,
        ["CDirectionalBlendUpdateNode"] = BlendColor,
        ["CBlendUpdateNode"] = BlendColor,
        ["CBlend2DUpdateNode"] = Blend2DColor,
        ["CAddUpdateNode"] = AddSubtractColor,
        ["CSubtractUpdateNode"] = AddSubtractColor,
        ["CAimMatrixUpdateNode"] = AimLeanMatrixColor,
        ["CLeanMatrixUpdateNode"] = AimLeanMatrixColor,
        ["CBoneMaskUpdateNode"] = BoneMaskColor,
        ["CCycleControlUpdateNode"] = CycleControlColor,
        ["CCycleControlClipUpdateNode"] = CycleControlClipColor,
        ["CFollowAttachmentUpdateNode"] = FollowAttachmentColor,
        ["CFollowPathUpdateNode"] = FollowPathColor,
        ["CFootPinningUpdateNode"] = ConstraintLightGreenColor,
        ["CLookAtUpdateNode"] = ConstraintLightGreenColor,
        ["CHitReactUpdateNode"] = ConstraintLightGreenColor,
        ["CFootLockUpdateNode"] = ConstraintLightGreenColor,
        ["CJiggleBoneUpdateNode"] = JiggleBoneColor,
        ["CSolveIKChainUpdateNode"] = ConstraintDarkGreenColor,
        ["CTwoBoneIKUpdateNode"] = ConstraintDarkGreenColor,
        ["CSpeedScaleUpdateNode"] = ConstraintDarkGreenColor,
        ["CChoreoUpdateNode"] = ChoreoColor,
        ["CDirectPlaybackUpdateNode"] = SystemYellowColor,
        ["CFootStepTriggerUpdateNode"] = SystemYellowColor,
        ["CInputStreamUpdateNode"] = InputStreamColor,
        ["CMoverUpdateNode"] = SystemDarkBlueColor,
        ["CStopAtGoalUpdateNode"] = SystemDarkBlueColor,
        ["CPathHelperUpdateNode"] = SystemGrayColor,
        ["CSetFacingUpdateNode"] = SystemGrayColor,
        ["CSlowDownOnSlopesUpdateNode"] = SlowDownOnSlopesColor,
        ["CSkeletalInputUpdateNode"] = SteamVRSkeletalInputColor,
        ["CTurnHelperUpdateNode"] = TurnHelperColor,
        ["CRootUpdateNode"] = FinalPoseColor,
        ["CStateMachineUpdateNode"] = StateMachineColor,
    };
    private static readonly HashSet<string> GlobalPropertySkips = new(StringComparer.Ordinal)
    {
        "m_name",
    };

    private static readonly Dictionary<string, string> PropertyDisplayNames = new(StringComparer.Ordinal)
    {
        ["m_networkMode"] = "NetworkMode",
    };

    // ---- Properties to skip in generic display for specific classes ----
    private static readonly Dictionary<string, HashSet<string>> ClassPropertySkips = new(StringComparer.Ordinal)
    {
        ["CChoiceUpdateNode"] = new(StringComparer.Ordinal) { "m_weights", "m_blendTimes", "m_choiceMethod", "m_blendMethod", "m_choiceChangeMethod", "m_bCrossFade", "m_bResetChosen", "m_bDontResetSameSelection" },
        ["CSelectorUpdateNode"] = new(StringComparer.Ordinal) { "m_hParameter", "m_param" },
        ["CSequenceUpdateNode"] = new(StringComparer.Ordinal) { "m_hSequence", "m_duration", "m_playbackSpeed", "m_bLoop" },
        ["CBlend2DUpdateNode"] = new(StringComparer.Ordinal) { "m_blendSourceX", "m_blendSourceY", "m_eBlendMode", "m_bLoop", "m_playbackSpeed", "m_damping", "m_items" },
        ["CBoneMaskUpdateNode"] = new(StringComparer.Ordinal) { "m_nWeightListIndex", "m_blendSpace", "m_flRootMotionBlend", "m_bUseBlendScale" },
        ["CStateMachineUpdateNode"] = new(StringComparer.Ordinal) { "m_stateMachine", "m_transitionData" },
    };

    private static readonly string[] ChildProperties = { "m_pChildNode", "m_pChild1", "m_pChild2", "m_pChild" };

    public AG1GraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, KVObject data)
        : base(vrfGuiContext, rendererContext, CreateAndConfigureNodeGraph(data, out var graphDef))
    {
        animGraphData = graphDef;

        LoadParameters();
        LoadTags();
        LoadComponents();

        if (animGraphData.ContainsKey("m_pSharedData"))
        {
            var sharedData = animGraphData.GetSubCollection("m_pSharedData");
            if (sharedData.ContainsKey("m_nodes"))
                compiledNodes = sharedData.GetArray("m_nodes");
            else
                compiledNodes = Array.Empty<KVObject>();
        }
        else if (animGraphData.ContainsKey("m_nodes"))
        {
            compiledNodes = animGraphData.GetArray("m_nodes");
        }
        else
        {
            compiledNodes = Array.Empty<KVObject>();
        }

        CreateGraph();
        AddParameterAndTagNodes();
        AddComponentNodes();
        nodeGraph.LayoutNodes();
    }

    private void LoadParameters()
    {
        parameterIndexToName.Clear();
        parameterIndexToObject.Clear();
        typeToParameters.Clear();

        KVObject? paramListUpdater = null;

        if (animGraphData.ContainsKey("m_pSharedData"))
        {
            var sharedData = animGraphData.GetSubCollection("m_pSharedData");
            if (sharedData.ContainsKey("m_pParamListUpdater"))
                paramListUpdater = sharedData.GetSubCollection("m_pParamListUpdater");
        }

        if (paramListUpdater == null && animGraphData.ContainsKey("m_pParamListUpdater"))
            paramListUpdater = animGraphData.GetSubCollection("m_pParamListUpdater");

        if (paramListUpdater == null)
            return;

        if (!paramListUpdater.ContainsKey("m_parameters"))
            return;

        var parameters = paramListUpdater.GetArray("m_parameters");
        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];
            var name = param.GetStringProperty("m_name");
            if (!string.IsNullOrEmpty(name))
            {
                parameterIndexToName[i] = name;
                parameterIndexToObject[i] = param;
                parameterObjectToIndex[param] = i;

                var className = param.GetStringProperty("_class");
                var type = ClassNameToParamType(className);
                if (!typeToParameters.TryGetValue(type, out var list))
                {
                    list = [];
                    typeToParameters[type] = list;
                }
                list.Add(param);
            }
        }
    }

    private void LoadTags()
    {
        tags.Clear();
        tagIndexToName.Clear();

        KVObject? tagManager = null;

        if (animGraphData.ContainsKey("m_pSharedData"))
        {
            var sharedData = animGraphData.GetSubCollection("m_pSharedData");
            if (sharedData.ContainsKey("m_pTagManagerUpdater"))
                tagManager = sharedData.GetSubCollection("m_pTagManagerUpdater");
        }

        if (tagManager == null && animGraphData.ContainsKey("m_pTagManagerUpdater"))
            tagManager = animGraphData.GetSubCollection("m_pTagManagerUpdater");

        if (tagManager == null)
            return;

        if (!tagManager.ContainsKey("m_tags"))
            return;

        var tagList = tagManager.GetArray("m_tags");
        for (int i = 0; i < tagList.Count; i++)
        {
            var tagObj = tagList[i];
            tags.Add(tagObj);
            var name = tagObj.GetStringProperty("m_name");
            if (!string.IsNullOrEmpty(name))
            {
                tagIndexToName[i] = name;
            }
        }
    }

    private string GetTagName(int tagIndex)
    {
        if (tagIndex < 0 || tagIndex >= tags.Count)
            return $"Tag {tagIndex}";
        return tagIndexToName.TryGetValue(tagIndex, out var name) ? name : $"Tag {tagIndex}";
    }

    private void LoadComponents()
    {
        components.Clear();

        KVObject? componentUpdaters = null;

        if (animGraphData.ContainsKey("m_pSharedData"))
        {
            var sharedData = animGraphData.GetSubCollection("m_pSharedData");
            if (sharedData.ContainsKey("m_components"))
                componentUpdaters = sharedData;
        }

        if (componentUpdaters == null && animGraphData.ContainsKey("m_components"))
            componentUpdaters = animGraphData;

        if (componentUpdaters == null)
            return;

        if (componentUpdaters.ContainsKey("m_components"))
        {
            var compList = componentUpdaters.GetArray("m_components");
            foreach (var comp in compList)
            {
                components.Add(comp);
            }
        }
    }

    private static string ClassNameToParamType(string className)
    {
        if (string.IsNullOrEmpty(className))
            return "UNKNOWN";

        return className switch
        {
            "CFloatAnimParameter" => "FLOAT",
            "CEnumAnimParameter" => "ENUM",
            "CBoolAnimParameter" => "BOOL",
            "CIntAnimParameter" => "INT",
            "CVectorAnimParameter" => "VECTOR",
            "CQuaternionAnimParameter" => "QUATERNION",
            "CSymbolAnimParameter" => "SYMBOL",
            "CVirtualAnimParameter" => "VIRTUAL",
            _ => className.Replace("AnimParameter", "").TrimStart('C').ToUpperInvariant(),
        };
    }

    private KVObject? ResolveParameterHandle(KVObject handle)
    {
        if (handle == null)
            return null;

        var type = handle.GetStringProperty("m_type");
        var index = handle.GetInt32Property("m_index", -1);

        if (string.IsNullOrEmpty(type) || index < 0 || index == 255)
            return null;

        var typeName = type.Replace("ANIMPARAM_", "", StringComparison.Ordinal);
        if (!typeToParameters.TryGetValue(typeName, out var paramList))
            return null;

        if (index < paramList.Count)
            return paramList[index];

        return null;
    }

    private string GetParameterDescriptionFromIndex(int index)
    {
        if (index < 0 || index == 255)
            return "None";

        if (parameterIndexToObject.TryGetValue(index, out var paramObj))
            return GetParameterDescriptionFromObject(paramObj);
        if (parameterIndexToName.TryGetValue(index, out var name))
            return name;
        return $"Param {index}";
    }

    private static string GetParameterDescriptionFromObject(KVObject paramObj)
    {
        var className = paramObj.GetStringProperty("_class");
        var typeName = ClassNameToParamType(className);
        var name = paramObj.GetStringProperty("m_name") ?? "Unnamed";

        var result = $"{name} ({typeName})";

        if (className == "CEnumAnimParameter" && paramObj.ContainsKey("m_enumOptions"))
        {
            var enumOptions = paramObj.GetArray<string>("m_enumOptions");
            if (enumOptions.Length > 0)
            {
                var optStr = string.Join(", ", enumOptions.Take(4));
                if (enumOptions.Length > 4)
                    optStr += ", ...";
                result += $" [ {optStr} ]";
            }
        }

        if (paramObj.ContainsKey("m_bDefaultValue"))
        {
            var def = paramObj.GetBooleanProperty("m_bDefaultValue");
            result += $" (default: {def})";
        }
        else if (paramObj.ContainsKey("m_fDefaultValue"))
        {
            var def = paramObj.GetFloatProperty("m_fDefaultValue");
            result += $" (default: {def:F2})";
        }
        else if (paramObj.ContainsKey("m_defaultValue"))
        {
            var def = paramObj["m_defaultValue"];
            if (def.ValueType == KVValueType.Int32 || def.ValueType == KVValueType.UInt32)
                result += $" (default: {def.ToInt32()})";
            else if (def.ValueType == KVValueType.Array && def.ToArray().Length > 0)
                result += $" (default: {string.Join(", ", def.ToArray().Select(v => v.ToString()))})";
        }

        return result;
    }

    private string GetParameterDescriptionFromHandle(KVObject handle)
    {
        if (handle == null)
            return "?";

        var paramObj = ResolveParameterHandle(handle);
        if (paramObj != null)
        {
            var desc = GetParameterDescriptionFromObject(paramObj);
            var type = handle.GetStringProperty("m_type");
            var index = handle.GetInt32Property("m_index", -1);
            return $"{desc} (handle: {type} idx {index})";
        }

        var typeRaw = handle.GetStringProperty("m_type", "Unknown");
        var indexRaw = handle.GetInt32Property("m_index", -1);
        return $"{typeRaw} idx {indexRaw}";
    }

    private void DisplayUniversalParameters(KVObject compiledNode, Node node)
    {
        string[] paramFields =
        {
            "m_paramIndex",
            "m_paramX",
            "m_paramY",
            "m_param",
            "m_hParameter",
            "m_hBlendParameter",
            "m_hParam",
        };

        foreach (string field in paramFields)
        {
            if (!compiledNode.ContainsKey(field))
                continue;

            var value = compiledNode[field];

            if (value.ValueType == KVValueType.Int32 || value.ValueType == KVValueType.UInt32)
            {
                int index = value.ToInt32();
                string display = GetParameterDescriptionFromIndex(index);
                node.AddText($"{field}: {display}");
                continue;
            }

            if (value.ValueType == KVValueType.Collection)
            {
                var handle = compiledNode.GetSubCollection(field);
                if (handle != null)
                {
                    string info = GetParameterDescriptionFromHandle(handle);
                    node.AddText($"{field}: {info}");
                }
                continue;
            }

            if (value.ValueType == KVValueType.String)
            {
                string name = value.ToString();
                if (!string.IsNullOrEmpty(name))
                    node.AddText($"{field}: {name}");
            }
        }
    }

    private static (float x, float y) ParseVector2(KVObject obj, string key)
    {
        if (!obj.ContainsKey(key))
            return (0f, 0f);

        var value = obj[key];

        if (value.ValueType == KVValueType.Array)
        {
            var array = obj.GetFloatArray(key);
            if (array != null && array.Length >= 2)
                return (array[0], array[1]);
        }

        if (value.ValueType == KVValueType.Collection)
        {
            var sub = obj.GetSubCollection(key);
            if (sub != null)
            {
                float x = sub.GetFloatProperty("0", 0f);
                float y = sub.GetFloatProperty("1", 0f);
                return (x, y);
            }
        }

        return (0f, 0f);
    }

    private static NodeGraphControl CreateAndConfigureNodeGraph(KVObject data, out KVObject graphDef)
    {
        graphDef = data;
        var nodeGraph = new NodeGraphControl
        {
            GridStyle = NodeGraphControl.EGridStyle.Checkerboard,
            CanvasBackgroundColor = new SKColor(40, 40, 40)
        };

        nodeGraph.GridColor = SKColors.White;

        if (Themer.CurrentTheme == Themer.AppTheme.Dark)
        {
            nodeGraph.CanvasBackgroundColor = ToSKColor(Themer.CurrentThemeColors.AppMiddle);
            NodeColor = ToSKColor(Themer.CurrentThemeColors.AppSoft);
            nodeGraph.GridColor = ToSKColor(Themer.CurrentThemeColors.ContrastSoft);
        }

        NodeGraphControl.AddTypeColorPair<Pose>(PoseColor);

        return nodeGraph;
    }

    private static SKColor ToSKColor(Color color) => new(color.R, color.G, color.B, color.A);
    private void CreateGraph()
    {
        if (compiledNodes == null || compiledNodes.Count == 0)
            return;

        for (int i = 0; i < compiledNodes.Count; i++)
        {
            var compiledNode = compiledNodes[i];
            var node = CreateNode(compiledNode, i);
            nodeMap[i] = node;
        }

        for (int i = 0; i < compiledNodes.Count; i++)
        {
            var compiledNode = compiledNodes[i];
            var parentNode = nodeMap[i];
            var className = compiledNode.GetStringProperty("_class");

            var childIndices = new HashSet<int>();

            if (compiledNode.ContainsKey("m_children"))
            {
                var children = compiledNode.GetArray("m_children");
                foreach (var child in children)
                {
                    if (child.ValueType == KVValueType.Collection && child.ContainsKey("m_nodeIndex"))
                    {
                        int idx = child.GetInt32Property("m_nodeIndex");
                        if (idx >= 0) childIndices.Add(idx);
                    }
                    else if (child.ValueType == KVValueType.Int32)
                    {
                        int idx = child.ToInt32();
                        if (idx >= 0) childIndices.Add(idx);
                    }
                }
            }

            foreach (var prop in ChildProperties)
            {
                if (compiledNode.ContainsKey(prop))
                {
                    var childRef = compiledNode[prop];
                    if (childRef.ValueType == KVValueType.Collection && childRef.ContainsKey("m_nodeIndex"))
                    {
                        int idx = childRef.GetInt32Property("m_nodeIndex");
                        if (idx >= 0) childIndices.Add(idx);
                    }
                }
            }

            if (className == "CBlend2DUpdateNode" && compiledNode.ContainsKey("m_items"))
            {
                var items = compiledNode.GetArray("m_items");
                foreach (var item in items)
                {
                    if (item.ContainsKey("m_pChild"))
                    {
                        var childRef = item.GetSubCollection("m_pChild");
                        if (childRef.ContainsKey("m_nodeIndex"))
                        {
                            int idx = childRef.GetInt32Property("m_nodeIndex");
                            if (idx >= 0) childIndices.Add(idx);
                        }
                    }
                }
            }

            if (className == "CStateMachineUpdateNode" && compiledNode.ContainsKey("m_stateData"))
            {
                var stateDataArray = compiledNode.GetArray("m_stateData");
                for (int s = 0; s < stateDataArray.Count; s++)
                {
                    var stateData = stateDataArray[s];
                    if (stateData.ContainsKey("m_pChild"))
                    {
                        var childRef = stateData.GetSubCollection("m_pChild");
                        if (childRef.ContainsKey("m_nodeIndex"))
                        {
                            int idx = childRef.GetInt32Property("m_nodeIndex");
                            if (idx >= 0) childIndices.Add(idx);
                        }
                    }
                }
            }

            Dictionary<int, (float weight, float blendTime)>? choiceWeightMap = null;
            if (className == "CChoiceUpdateNode")
            {
                float[]? weights = null;
                float[]? blendTimes = null;
                if (compiledNode.ContainsKey("m_weights"))
                    weights = compiledNode.GetFloatArray("m_weights");
                if (compiledNode.ContainsKey("m_blendTimes"))
                    blendTimes = compiledNode.GetFloatArray("m_blendTimes");

                choiceWeightMap = new Dictionary<int, (float, float)>();
                if (compiledNode.ContainsKey("m_children"))
                {
                    var children = compiledNode.GetArray("m_children");
                    for (int c = 0; c < children.Count; c++)
                    {
                        var child = children[c];
                        int idx = -1;
                        if (child.ValueType == KVValueType.Collection && child.ContainsKey("m_nodeIndex"))
                            idx = child.GetInt32Property("m_nodeIndex");
                        else if (child.ValueType == KVValueType.Int32)
                            idx = child.ToInt32();
                        if (idx >= 0)
                        {
                            float w = (weights != null && c < weights.Length) ? weights[c] : 1.0f;
                            float bt = (blendTimes != null && c < blendTimes.Length) ? blendTimes[c] : 0.0f;
                            choiceWeightMap[idx] = (w, bt);
                        }
                    }
                }
            }

            Dictionary<int, string>? selectorOptionMap = null;
            if (className == "CSelectorUpdateNode")
            {

                KVObject? paramHandle = null;
                if (compiledNode.ContainsKey("m_hParameter"))
                    paramHandle = compiledNode.GetSubCollection("m_hParameter");
                else if (compiledNode.ContainsKey("m_param"))
                    paramHandle = compiledNode.GetSubCollection("m_param");

                if (paramHandle != null)
                {
                    var paramObj = ResolveParameterHandle(paramHandle);
                    if (paramObj != null)
                    {
                        var paramClass = paramObj.GetStringProperty("_class");
                        if (paramClass == "CEnumAnimParameter" && paramObj.ContainsKey("m_enumOptions"))
                        {
                            var enumOptions = paramObj.GetArray<string>("m_enumOptions");
                            if (enumOptions.Length > 0)
                            {
                                selectorOptionMap = new Dictionary<int, string>();

                                var children = compiledNode.GetArray("m_children");
                                for (int c = 0; c < children.Count; c++)
                                {
                                    var child = children[c];
                                    int idx = -1;
                                    if (child.ValueType == KVValueType.Collection && child.ContainsKey("m_nodeIndex"))
                                        idx = child.GetInt32Property("m_nodeIndex");
                                    else if (child.ValueType == KVValueType.Int32)
                                        idx = child.ToInt32();
                                    if (idx >= 0)
                                    {
                                        string label = (c < enumOptions.Length) ? enumOptions[c] : $"Option {c}";
                                        selectorOptionMap[idx] = label;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            foreach (int childIdx in childIndices)
            {
                if (!nodeMap.TryGetValue(childIdx, out var childNode))
                    continue;

                if (!childNode.Sockets.OfType<SocketOut>().Any())
                {
                    var outSocket = new SocketOut(typeof(Pose), string.Empty, childNode);
                    childNode.Sockets.Add(outSocket);
                }

                string inputLabel = $"Child {childIdx}";

                if (className == "CStateMachineUpdateNode")
                {
                    if (compiledNode.ContainsKey("m_stateData") && compiledNode.ContainsKey("m_stateMachine"))
                    {
                        var stateDataArray = compiledNode.GetArray("m_stateData");
                        var stateMachine = compiledNode.GetSubCollection("m_stateMachine");
                        if (stateMachine.ContainsKey("m_states"))
                        {
                            var states = stateMachine.GetArray("m_states");
                            for (int s = 0; s < stateDataArray.Count; s++)
                            {
                                var stateData = stateDataArray[s];
                                if (stateData.ContainsKey("m_pChild"))
                                {
                                    var childRef = stateData.GetSubCollection("m_pChild");
                                    if (childRef.ContainsKey("m_nodeIndex") && childRef.GetInt32Property("m_nodeIndex") == childIdx)
                                    {
                                        inputLabel = states[s].GetStringProperty("m_name", $"State {s}");
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                else if (className == "CBlend2DUpdateNode" && compiledNode.ContainsKey("m_items"))
                {
                    var items = compiledNode.GetArray("m_items");
                    for (int itemIdx = 0; itemIdx < items.Count; itemIdx++)
                    {
                        var item = items[itemIdx];
                        if (item.ContainsKey("m_pChild"))
                        {
                            var childRef = item.GetSubCollection("m_pChild");
                            if (childRef.ContainsKey("m_nodeIndex") && childRef.GetInt32Property("m_nodeIndex") == childIdx)
                            {
                                int seqIdx = item.GetInt32Property("m_hSequence", -1);
                                var pos = ParseVector2(item, "m_vPos");
                                inputLabel = $"Item {itemIdx} (Seq {seqIdx}) ({pos.x:F1}, {pos.y:F1})";
                                break;
                            }
                        }
                    }
                }
                else if (className == "CChoiceUpdateNode" && choiceWeightMap != null && choiceWeightMap.TryGetValue(childIdx, out var choiceData))
                {
                    inputLabel = $"Item {childIdx} (W:{choiceData.weight:F2} BT:{choiceData.blendTime:F2})";
                }
                else if (className == "CSelectorUpdateNode" && selectorOptionMap != null && selectorOptionMap.TryGetValue(childIdx, out var optionName))
                {
                    inputLabel = optionName;
                }

                var inputSocket = new SocketIn(typeof(Pose), inputLabel, parentNode, hub: true);
                parentNode.Sockets.Add(inputSocket);

                var childOutput = childNode.Sockets.OfType<SocketOut>().First();
                nodeGraph.Connect(childOutput, inputSocket);
            }
            parentNode.Calculate();
        }
        nodeGraph.LayoutNodes();
    }

    private Node CreateNode(KVObject compiledNode, int index)
    {
        var className = compiledNode.GetStringProperty("_class");
        string displayName = ClassDisplayName.TryGetValue(className, out var display)
            ? display
            : className?.Replace("UpdateNode", "") ?? "Unknown";

        string nodeName = compiledNode.GetStringProperty("m_name");
        if (string.IsNullOrEmpty(nodeName))
            nodeName = displayName;

        var node = new Node(compiledNode)
        {
            Name = $"({index}) {nodeName}",
            NodeType = displayName,
        };

        if (className != null && ClassColor.TryGetValue(className, out var color))
            node.HeaderColor = color;
        else
            node.HeaderColor = PoseColor;

        string networkMode = compiledNode.GetStringProperty("m_networkMode", "");
        if (networkMode.Equals("ClientSimulate", StringComparison.Ordinal))
        {

            node.SetBaseColor(ClientSimulateColor);
        }

        HashSet<string>? skipKeys = null;
        if (className != null)
            ClassPropertySkips.TryGetValue(className, out skipKeys);

        foreach (var kv in compiledNode.Children)
        {
            string key = kv.Key;
            if (key == "_class" || key == "m_nodePath" || key == "m_children" || key.StartsWith("m_pChild") ||
                key == "m_tags" || key == "m_paramSpans" || key == "m_stateMachine" || key == "m_stateData" || key == "m_transitionData")
                continue;

            if (GlobalPropertySkips.Contains(key))
                continue;

            if (skipKeys != null && skipKeys.Contains(key))
                continue;

            if (kv.Value.ValueType == KVValueType.Collection || kv.Value.ValueType == KVValueType.Array)
                continue;

            string displayKey = PropertyDisplayNames.TryGetValue(key, out var friendly) ? friendly : key;
            string valueStr = kv.Value.ToString();
            if (!string.IsNullOrEmpty(valueStr))
                node.AddText($"{displayKey}: {valueStr}");
        }

        DisplayUniversalParameters(compiledNode, node);

        if (compiledNode.ContainsKey("m_nTagIndex"))
        {
            int tagIndex = compiledNode.GetInt32Property("m_nTagIndex");
            if (tagIndex >= 0)
            {
                var tagName = GetTagName(tagIndex);
                node.AddText($"Tag: {tagName} (idx {tagIndex})");
            }
        }

        if (compiledNode.ContainsKey("m_eTagBehavior"))
        {
            node.AddText($"Tag Behavior: {compiledNode.GetStringProperty("m_eTagBehavior")}");
        }

        if (compiledNode.ContainsKey("m_tags"))
        {
            var tagsValue = compiledNode["m_tags"];
            if (tagsValue.ValueType == KVValueType.Array)
            {
                var tagIndices = compiledNode.GetIntegerArray("m_tags");
                if (tagIndices.Length > 0)
                {
                    var tagNames = tagIndices.Select(idx => GetTagName((int)idx));
                    node.AddText($"Tags: [{string.Join(", ", tagNames)}]");
                }
            }
        }

        // Choice node
        if (className == "CChoiceUpdateNode")
        {
            if (compiledNode.ContainsKey("m_choiceMethod"))
                node.AddText($"ChoiceMethod: {compiledNode.GetStringProperty("m_choiceMethod")}");
            if (compiledNode.ContainsKey("m_blendMethod"))
                node.AddText($"BlendMethod: {compiledNode.GetStringProperty("m_blendMethod")}");
            if (compiledNode.ContainsKey("m_choiceChangeMethod"))
                node.AddText($"Choice ChangeMethod: {compiledNode.GetStringProperty("m_choiceChangeMethod")}");
            if (compiledNode.ContainsKey("m_bCrossFade"))
                node.AddText($"CrossFade: {compiledNode.GetBooleanProperty("m_bCrossFade")}");
            if (compiledNode.ContainsKey("m_bResetChosen"))
                node.AddText($"ResetChosen: {compiledNode.GetBooleanProperty("m_bResetChosen")}");
            if (compiledNode.ContainsKey("m_bDontResetSameSelection"))
                node.AddText($"DontResetSameSelection: {compiledNode.GetBooleanProperty("m_bDontResetSameSelection")}");
        }

        // Sequence node
        if (className == "CSequenceUpdateNode")
        {
            if (compiledNode.ContainsKey("m_hSequence"))
                node.AddText($"Sequence Index: {compiledNode.GetInt32Property("m_hSequence")}");
            if (compiledNode.ContainsKey("m_duration"))
                node.AddText($"Duration: {compiledNode.GetFloatProperty("m_duration"):F2}");
            if (compiledNode.ContainsKey("m_playbackSpeed"))
                node.AddText($"Speed: {compiledNode.GetFloatProperty("m_playbackSpeed"):F2}");
            if (compiledNode.ContainsKey("m_bLoop"))
                node.AddText($"Loop: {compiledNode.GetBooleanProperty("m_bLoop")}");
        }

        // Blend 2D node
        if (className == "CBlend2DUpdateNode")
        {
            if (compiledNode.ContainsKey("m_blendSourceX"))
                node.AddText($"Blend X: {compiledNode.GetStringProperty("m_blendSourceX")}");
            if (compiledNode.ContainsKey("m_blendSourceY"))
                node.AddText($"Blend Y: {compiledNode.GetStringProperty("m_blendSourceY")}");
            if (compiledNode.ContainsKey("m_eBlendMode"))
                node.AddText($"Mode: {compiledNode.GetStringProperty("m_eBlendMode")}");
            if (compiledNode.ContainsKey("m_bLoop"))
                node.AddText($"Loop: {compiledNode.GetBooleanProperty("m_bLoop")}");
            if (compiledNode.ContainsKey("m_playbackSpeed"))
                node.AddText($"Playback Speed: {compiledNode.GetFloatProperty("m_playbackSpeed"):F2}");

            if (compiledNode.ContainsKey("m_damping"))
            {
                var damping = compiledNode.GetSubCollection("m_damping");
                if (damping != null)
                {
                    string speedFunc = damping.GetStringProperty("m_speedFunction");
                    float speedScale = damping.GetFloatProperty("m_fSpeedScale");
                    node.AddText($"Damping: {speedFunc} (scale {speedScale:F2})");
                }
            }

            if (compiledNode.ContainsKey("m_items"))
            {
                var items = compiledNode.GetArray("m_items");
                node.AddText($"Items: {items.Count}");
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    int seqIdx = item.GetInt32Property("m_hSequence", -1);
                    var pos = ParseVector2(item, "m_vPos");
                    float dur = item.GetFloatProperty("m_flDuration");
                    node.AddText($"  [{i}] Seq {seqIdx} ({pos.x:F1}, {pos.y:F1}) dur={dur:F2}s");
                }
            }
        }

        // BoneMask node
        if (className == "CBoneMaskUpdateNode")
        {
            if (compiledNode.ContainsKey("m_nWeightListIndex"))
                node.AddText($"Weight List Index: {compiledNode.GetInt32Property("m_nWeightListIndex")}");
            if (compiledNode.ContainsKey("m_blendSpace"))
                node.AddText($"Blend Space: {compiledNode.GetStringProperty("m_blendSpace")}");
            if (compiledNode.ContainsKey("m_flRootMotionBlend"))
                node.AddText($"Root Motion Blend: {compiledNode.GetFloatProperty("m_flRootMotionBlend"):F2}");
            if (compiledNode.ContainsKey("m_bUseBlendScale"))
                node.AddText($"Use Blend Scale: {compiledNode.GetBooleanProperty("m_bUseBlendScale")}");
        }

        // State machine node
        if (className == "CStateMachineUpdateNode")
        {
            if (compiledNode.ContainsKey("m_stateMachine"))
            {
                var stateMachine = compiledNode.GetSubCollection("m_stateMachine");
                if (stateMachine.ContainsKey("m_states"))
                {
                    var states = stateMachine.GetArray("m_states");
                    node.AddText($"States: {states.Count}");
                    foreach (var state in states)
                    {
                        string name = state.GetStringProperty("m_name", "Unnamed");
                        bool isStart = state.GetIntegerProperty("m_bIsStartState") > 0;
                        node.AddText($"  {(isStart ? "*" : " ")} {name}");
                    }
                }
                if (stateMachine.ContainsKey("m_transitions"))
                    node.AddText($"Transitions: {stateMachine.GetArray("m_transitions").Count}");
            }
            if (compiledNode.ContainsKey("m_transitionData") && compiledNode.GetArray("m_transitionData").Count > 0)
            {
                var first = compiledNode.GetArray("m_transitionData")[0];
                if (first.ContainsKey("m_blendDuration"))
                {
                    var blend = first.GetSubCollection("m_blendDuration");
                    node.AddText($"Blend Duration (first): {blend.GetFloatProperty("m_constValue"):F2}");
                }
            }
        }
        if (className == "CSelectorUpdateNode")
        {
            if (compiledNode.ContainsKey("m_selectionSource"))
                node.AddText($"SelectionSource: {compiledNode.GetStringProperty("m_selectionSource")}");
        }

        nodeGraph.AddNode(node);
        return node;
    }

    private static SKColor GetParameterTypeColor(string type)
    {
        return type switch
        {
            "BOOL" => ParamBoolColor,
            "INT" => ParamIntColor,
            "FLOAT" => ParamFloatColor,
            "ENUM" => ParamEnumColor,
            "VECTOR" => ParamVectorColor,
            "QUATERNION" => ParamQuaternionColor,
            "SYMBOL" => ParamSymbolColor,
            "VIRTUAL" => ParamVirtualColor,
            _ => ParamUnknownColor,
        };
    }

    private void AddParameterAndTagNodes()
    {
        var typeOrder = new[] { "BOOL", "INT", "FLOAT", "ENUM", "VECTOR", "QUATERNION", "SYMBOL", "VIRTUAL", "UNKNOWN" };
        foreach (var type in typeOrder)
        {
            if (!typeToParameters.TryGetValue(type, out var list)) continue;
            if (list.Count == 0) continue;

            var ordered = list.OrderBy(p => parameterObjectToIndex.TryGetValue(p, out var idx) ? idx : int.MaxValue).ToList();
            var friendlyName = ParameterTypeDisplayName.TryGetValue(type, out var display) ? display : type;

            var node = new Node(null)
            {
                Name = $"{friendlyName} Parameters",
                NodeType = "Parameter Group",
                HeaderColor = GetParameterTypeColor(type),
            };
            foreach (var p in ordered)
                node.AddText(GetParameterDescriptionFromObject(p));
            nodeGraph.AddNode(node);
        }

        if (tags.Count > 0)
        {
            var tagIndexMap = tags.Select((t, i) => new { t, i }).ToDictionary(x => x.t, x => x.i);

            var tagGroups = tags.GroupBy(t => t.GetStringProperty("_class") ?? "Unknown");
            foreach (var group in tagGroups)
            {
                var className = group.Key;
                var friendlyName = TagDisplayName.TryGetValue(className, out var display) ? display : className;

                var ordered = group.OrderBy(t => tagIndexMap.TryGetValue(t, out var idx) ? idx : int.MaxValue).ToList();

                var node = new Node(null)
                {
                    Name = $"{friendlyName}",
                    NodeType = "Tag Group",
                    HeaderColor = GetTagClassColor(className),
                };
                foreach (var t in ordered)
                {
                    var name = t.GetStringProperty("m_name") ?? "Unnamed";
                    node.AddText(name);
                }
                nodeGraph.AddNode(node);
            }
        }
    }

    private void AddComponentNodes()
    {
        if (components.Count == 0)
            return;

        foreach (var comp in components)
        {
            var className = comp.GetStringProperty("_class") ?? "Unknown";
            var friendlyName = ComponentDisplayName.TryGetValue(className, out var display) ? display : className;
            var name = comp.GetStringProperty("m_name") ?? "";
            var displayName = string.IsNullOrEmpty(name) ? friendlyName : $"{friendlyName} ({name})";

            var node = new Node(null)
            {
                Name = displayName,
                NodeType = "Component",
                HeaderColor = GetComponentClassColor(className),
            };

            if (className == "CStateMachineComponentUpdater")
            {
                if (comp.ContainsKey("m_stateMachine"))
                {
                    var stateMachine = comp.GetSubCollection("m_stateMachine");
                    if (stateMachine.ContainsKey("m_states"))
                    {
                        var states = stateMachine.GetArray("m_states");
                        node.AddText($"States ({states.Count}):");
                        foreach (var state in states)
                        {
                            var stateName = state.GetStringProperty("m_name", "Unnamed");
                            node.AddText($"  {stateName}");
                        }
                    }
                }
            }

            foreach (var kv in comp.Children)
            {
                string key = kv.Key;
                if (key == "_class" || key == "m_stateMachine" || key == "m_name")
                    continue;
                if (kv.Value.ValueType == KVValueType.Collection || kv.Value.ValueType == KVValueType.Array)
                    continue;

                string displayKey = PropertyDisplayNames.TryGetValue(key, out var friendly) ? friendly : key;
                string valueStr = kv.Value.ToString();
                if (!string.IsNullOrEmpty(valueStr))
                    node.AddText($"{displayKey}: {valueStr}");
            }

            string networkMode = comp.GetStringProperty("m_networkMode", "");
            if (networkMode.Equals("ClientSimulate", StringComparison.Ordinal))
            {
                node.SetBaseColor(ClientSimulateColor);
            }

            nodeGraph.AddNode(node);
        }
    }

    #region Node class

    private class Node : AbstractNode
    {
        public KVObject? Data { get; set; }

        public Node(KVObject? data)
        {
            Data = data;
            BaseColor = new SKColor(61, 61, 61);
            TextColor = NodeTextColor;
            HeaderTextColor = new SKColor(255, 255, 255);
            HeaderTypeColor = new SKColor(255, 255, 255);
        }
        public void SetBaseColor(SKColor color)
        {
            BaseColor = color;
        }

        public void AddSpace() => CreateTextSocket<string>(string.Empty);
        public void AddText(string text) => CreateTextSocket<string>(text);

        private void CreateTextSocket<T>(string text)
        {
            var socket = new SocketIn(typeof(T), text, this, false)
            {
                DisplayOnly = true
            };
            Sockets.Add(socket);
        }
    }
    #endregion

    private struct Pose { }
}
