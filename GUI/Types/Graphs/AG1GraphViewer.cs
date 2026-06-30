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
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelData.Attachments;
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

    private readonly VrfGuiContext fileLoader;
    private Resource? modelResource;
    private bool modelResourceLoaded;
    private Dictionary<int, string>? sequenceNamesCache;
    private Dictionary<int, string>? weightListNamesCache;
    private string[]? boneNamesCache;
    private Dictionary<string, Attachment>? modelAttachments;
    private string[]? ikChainNamesCache;
    private string[]? footNamesCache;
    private Dictionary<string, List<string>>? ikChainBonesCache;

    // Fixed colors per node type (AG1 specific)
    private static SKColor NodeColor { get; set; } = new SKColor(60, 60, 60);
    //Blend
    private static SKColor BlendColor { get; set; } = new SKColor(45, 102, 82);
    private static SKColor AddSubtractColor { get; set; } = new SKColor(126, 60, 55);
    private static SKColor AimLeanMatrixColor { get; set; } = new SKColor(99, 43, 87);
    // Constraints and Procedural
    private static SKColor ConstraintLightGreenColor { get; set; } = new SKColor(91, 143, 56);
    private static SKColor ConstraintDarkGreenColor { get; set; } = new SKColor(44, 106, 31);
    // System
    private static SKColor SystemYellowColor { get; set; } = new SKColor(78, 73, 29);
    private static SKColor SystemDarkBlueColor { get; set; } = new SKColor(24, 54, 72);
    private static SKColor SystemGrayColor { get; set; } = new SKColor(65, 90, 114);
    // Main
    private static SKColor PoseColor { get; set; } = new SKColor(173, 255, 47);          // Default fallback

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
        return TagClassColors.TryGetValue(className, out var color) ? color : new SKColor(100, 200, 200);
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
        ["CSequenceUpdateNode"] = new SKColor(62, 43, 89),
        ["CChoiceUpdateNode"] = new SKColor(102, 111, 49),
        ["CMotionMatchingUpdateNode"] = new SKColor(127, 145, 218),
        ["CSelectorUpdateNode"] = new SKColor(34, 84, 135),
        ["CSingleFrameUpdateNode"] = new SKColor(111, 53, 195),
        ["CDirectionalBlendUpdateNode"] = BlendColor,
        ["CBlendUpdateNode"] = BlendColor,
        ["CBlend2DUpdateNode"] = new SKColor(7, 62, 42),
        ["CAddUpdateNode"] = AddSubtractColor,
        ["CSubtractUpdateNode"] = AddSubtractColor,
        ["CAimMatrixUpdateNode"] = AimLeanMatrixColor,
        ["CLeanMatrixUpdateNode"] = AimLeanMatrixColor,
        ["CBoneMaskUpdateNode"] = new SKColor(116, 51, 75),
        ["CCycleControlUpdateNode"] = new SKColor(108, 138, 61),
        ["CCycleControlClipUpdateNode"] = new SKColor(87, 52, 127),
        ["CFollowAttachmentUpdateNode"] = new SKColor(0, 161, 143),
        ["CFollowPathUpdateNode"] = new SKColor(27, 61, 82),
        ["CFootPinningUpdateNode"] = ConstraintLightGreenColor,
        ["CLookAtUpdateNode"] = ConstraintLightGreenColor,
        ["CHitReactUpdateNode"] = ConstraintLightGreenColor,
        ["CFootLockUpdateNode"] = ConstraintLightGreenColor,
        ["CJiggleBoneUpdateNode"] = new SKColor(87, 175, 122),
        ["CSolveIKChainUpdateNode"] = ConstraintDarkGreenColor,
        ["CTwoBoneIKUpdateNode"] = ConstraintDarkGreenColor,
        ["CSpeedScaleUpdateNode"] = ConstraintDarkGreenColor,
        ["CChoreoUpdateNode"] = new SKColor(123, 78, 35),
        ["CDirectPlaybackUpdateNode"] = SystemYellowColor,
        ["CFootStepTriggerUpdateNode"] = SystemYellowColor,
        ["CInputStreamUpdateNode"] = new SKColor(166, 0, 207),
        ["CMoverUpdateNode"] = SystemDarkBlueColor,
        ["CStopAtGoalUpdateNode"] = SystemDarkBlueColor,
        ["CPathHelperUpdateNode"] = SystemGrayColor,
        ["CSetFacingUpdateNode"] = SystemGrayColor,
        ["CSlowDownOnSlopesUpdateNode"] = new SKColor(26, 59, 18),
        ["CSkeletalInputUpdateNode"] = new SKColor(15, 101, 144),
        ["CTurnHelperUpdateNode"] = new SKColor(32, 30, 78),
        ["CRootUpdateNode"] = new SKColor(149, 121, 65),
        ["CStateMachineUpdateNode"] = new SKColor(44, 57, 89),
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
        ["CAimMatrixUpdateNode"] = new(StringComparer.Ordinal) { "m_opFixedSettings", "m_target", "m_paramIndex", "m_hSequence", "m_bResetChild", "m_bLockWhenWaning" },
        ["CFollowAttachmentUpdateNode"] = new(StringComparer.Ordinal) { "m_opFixedData" },
        ["CFootPinningUpdateNode"] = new(StringComparer.Ordinal) { "m_poseOpFixedData", "m_eTimingSource", "m_params", "m_bResetChild" },
    };

    private static readonly string[] ChildProperties = { "m_pChildNode", "m_pChild1", "m_pChild2", "m_pChild" };

    public AG1GraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, KVObject data)
        : base(vrfGuiContext, rendererContext, CreateAndConfigureNodeGraph(data, out var graphDef))
    {
        fileLoader = vrfGuiContext;
        animGraphData = graphDef;

        var isUncompiledAnimationGraph = animGraphData.GetStringProperty("_class") == "CAnimationGraph";
        if (isUncompiledAnimationGraph)
        {
            compiledNodes = Array.Empty<KVObject>();
            return;
        }

        LoadParameters();
        LoadTags();
        LoadComponents();

        var nodesContainer = ResolveContainer("m_nodes");
        compiledNodes = nodesContainer?.GetArray("m_nodes") ?? Array.Empty<KVObject>();

        CreateGraph();
        AddParameterAndTagNodes();
        AddComponentNodes();
        nodeGraph.LayoutNodes();
    }

    /// <summary>
    /// Returns the container (shared data if present, otherwise the graph root) that holds the given key.
    /// </summary>
    private KVObject? ResolveContainer(string key)
    {
        if (animGraphData.ContainsKey("m_pSharedData"))
        {
            var sharedData = animGraphData.GetSubCollection("m_pSharedData");
            if (sharedData.ContainsKey(key))
                return sharedData;
        }

        return animGraphData.ContainsKey(key) ? animGraphData : null;
    }

    private void LoadParameters()
    {
        parameterIndexToName.Clear();
        parameterIndexToObject.Clear();
        typeToParameters.Clear();

        var paramListUpdater = ResolveContainer("m_pParamListUpdater")?.GetSubCollection("m_pParamListUpdater");
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

        var tagManager = ResolveContainer("m_pTagManagerUpdater")?.GetSubCollection("m_pTagManagerUpdater");
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

        var componentUpdaters = ResolveContainer("m_components");
        if (componentUpdaters == null)
            return;

        components.AddRange(componentUpdaters.GetArray("m_components"));
    }

    private Resource? LoadModel()
    {
        if (modelResourceLoaded)
            return modelResource;
        modelResourceLoaded = true;

        var modelName = animGraphData.GetStringProperty("m_modelName");
        if (string.IsNullOrEmpty(modelName))
            return null;

        modelResource = fileLoader.LoadFileCompiled(modelName);
        return modelResource;
    }

    private Dictionary<int, string> LoadSequenceNames()
    {
        if (sequenceNamesCache != null)
            return sequenceNamesCache;

        sequenceNamesCache = new Dictionary<int, string>();
        var modelRes = LoadModel();
        if (modelRes == null)
            return sequenceNamesCache;

        var aseqBlock = modelRes.GetBlockByType(BlockType.ASEQ);
        if (aseqBlock is KeyValuesOrNTRO kv)
        {
            var data = kv.Data;
            if (data is KVObject kvData)
            {
                if (kvData.ContainsKey("m_localSequenceNameArray"))
                {
                    var names = kvData.GetArray<string>("m_localSequenceNameArray");
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(names[i]))
                            sequenceNamesCache[i] = names[i];
                    }
                }
                else if (kvData.GetStringProperty("m_sName")?.Contains("embedded_sequence_data") == true)
                {
                }
            }
        }

        if (modelRes.DataBlock is Model modelData)
        {
            var animations = modelData.GetReferencedAnimations(fileLoader);
            var index = sequenceNamesCache.Count;
            foreach (var anim in animations)
            {
                if (!string.IsNullOrEmpty(anim.Name) && !sequenceNamesCache.ContainsValue(anim.Name))
                {
                    sequenceNamesCache[index++] = anim.Name;
                }
            }
        }

        return sequenceNamesCache;
    }

    private Dictionary<int, string> LoadWeightListNames()
    {
        if (weightListNamesCache != null)
            return weightListNamesCache;

        weightListNamesCache = new Dictionary<int, string>();
        var modelRes = LoadModel();
        if (modelRes == null)
        {
            weightListNamesCache[0] = "default";
            return weightListNamesCache;
        }

        var aseqBlock = modelRes.GetBlockByType(BlockType.ASEQ);
        if (aseqBlock is KeyValuesOrNTRO kv)
        {
            var data = kv.Data;
            if (data is KVObject kvData && kvData.ContainsKey("m_localBoneMaskArray"))
            {
                var masks = kvData.GetArray("m_localBoneMaskArray");
                for (int i = 0; i < masks.Count; i++)
                {
                    var name = masks[i].GetStringProperty("m_sName");
                    if (!string.IsNullOrEmpty(name))
                        weightListNamesCache[i] = name;
                    else if (i == 0)
                        weightListNamesCache[i] = "default";
                    else
                        weightListNamesCache[i] = $"weightlist_{i}";
                }
            }
        }

        if (!weightListNamesCache.ContainsKey(0))
            weightListNamesCache[0] = "default";

        return weightListNamesCache;
    }

    private string[] LoadBoneNames()
    {
        if (boneNamesCache != null)
            return boneNamesCache;

        var modelRes = LoadModel();
        if (modelRes?.DataBlock is Model modelData)
        {
            boneNamesCache = modelData.Skeleton.Bones.Select(b => b.Name).ToArray();
        }
        else
        {
            boneNamesCache = Array.Empty<string>();
        }
        return boneNamesCache;
    }

    private string[] LoadIKChainNames()
    {
        if (ikChainNamesCache != null)
            return ikChainNamesCache;

        var modelRes = LoadModel();
        if (modelRes?.DataBlock is not Model modelData)
        {
            ikChainNamesCache = Array.Empty<string>();
            return ikChainNamesCache;
        }

        var keyvalues = modelData.KeyValues;
        if (keyvalues.ContainsKey("ikdata"))
        {
            var ikdata = keyvalues.GetSubCollection("ikdata");
            if (ikdata.ContainsKey("m_IKChains"))
            {
                var chains = ikdata.GetArray("m_IKChains");
                ikChainNamesCache = chains
                    .Select(c => c.GetStringProperty("m_Name"))
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToArray();
                return ikChainNamesCache;
            }
        }

        ikChainNamesCache = Array.Empty<string>();
        return ikChainNamesCache;
    }

    private Dictionary<string, List<string>> LoadIKChainBonesFromModel()
    {
        if (ikChainBonesCache != null)
            return ikChainBonesCache;

        ikChainBonesCache = new Dictionary<string, List<string>>();
        var modelRes = LoadModel();
        if (modelRes?.DataBlock is Model modelData)
        {
            var keyvalues = modelData.KeyValues;
            if (keyvalues.ContainsKey("ikdata"))
            {
                var ikdata = keyvalues.GetSubCollection("ikdata");
                if (ikdata.ContainsKey("m_IKChains"))
                {
                    var chains = ikdata.GetArray("m_IKChains");
                    foreach (var chain in chains)
                    {
                        var name = chain.GetStringProperty("m_Name");
                        if (string.IsNullOrEmpty(name))
                            continue;

                        var boneList = new List<string>();
                        if (chain.ContainsKey("m_Joints"))
                        {
                            foreach (var joint in chain.GetArray("m_Joints"))
                            {
                                if (joint.ContainsKey("m_Bone"))
                                {
                                    var boneName = joint.GetSubCollection("m_Bone").GetStringProperty("m_Name");
                                    if (!string.IsNullOrEmpty(boneName))
                                        boneList.Add(boneName);
                                }
                            }
                        }
                        ikChainBonesCache[name] = boneList;
                    }
                }
            }
        }
        return ikChainBonesCache;
    }

    private string GetIKChainNameByBoneIndices(int fixedBoneIndex, int middleBoneIndex, int endBoneIndex)
    {
        var fixedBoneName = GetBoneName(fixedBoneIndex);
        var middleBoneName = GetBoneName(middleBoneIndex);
        var endBoneName = GetBoneName(endBoneIndex);

        if (string.IsNullOrEmpty(fixedBoneName) || string.IsNullOrEmpty(middleBoneName) || string.IsNullOrEmpty(endBoneName))
            return string.Empty;

        var chains = LoadIKChainBonesFromModel();
        foreach (var (chainName, bones) in chains)
        {
            if (bones.Count == 3 && bones[0] == fixedBoneName && bones[1] == middleBoneName && bones[2] == endBoneName)
                return chainName;
        }
        return string.Empty;
    }

    private string[] LoadFootNames()
    {
        if (footNamesCache != null)
            return footNamesCache;

        var modelRes = LoadModel();
        if (modelRes?.DataBlock is not Model modelData)
        {
            footNamesCache = Array.Empty<string>();
            return footNamesCache;
        }

        var keyvalues = modelData.KeyValues;
        var footNames = new List<string>();
        if (keyvalues.ContainsKey("FeetSettings"))
        {
            var feetSettings = keyvalues.GetSubCollection("FeetSettings");
            foreach (var (footKey, _) in feetSettings.Children)
            {
                if (!string.IsNullOrEmpty(footKey) && footKey != "_class")
                {
                    footNames.Add(footKey);
                }
            }
        }
        footNamesCache = footNames.ToArray();
        return footNamesCache;
    }

    private Dictionary<string, Attachment> LoadModelAttachments()
    {
        if (modelAttachments != null)
            return modelAttachments;

        var modelRes = LoadModel();
        if (modelRes?.DataBlock is Model modelData)
        {
            modelAttachments = modelData.Attachments ?? new Dictionary<string, Attachment>();
        }
        else
        {
            modelAttachments = new Dictionary<string, Attachment>();
        }
        return modelAttachments;
    }

    private string GetIKChainName(int index)
    {
        var names = LoadIKChainNames();
        return index >= 0 && index < names.Length ? names[index] : $"ikchain_{index}";
    }

    private string GetFootName(int index)
    {
        var names = LoadFootNames();
        return index >= 0 && index < names.Length ? names[index] : $"foot_{index}";
    }

    private string FindMatchingAttachmentName(KVObject compiledAttachment)
    {
        if (compiledAttachment == null)
            return string.Empty;

        if (compiledAttachment.ContainsKey("m_attachmentName"))
            return compiledAttachment.GetStringProperty("m_attachmentName");
        if (compiledAttachment.ContainsKey("m_name"))
            return compiledAttachment.GetStringProperty("m_name");

        var attachments = LoadModelAttachments();
        if (attachments.Count == 0)
            return string.Empty;

        if (!compiledAttachment.ContainsKey("m_influenceIndices"))
            return string.Empty;

        var influenceIndices = compiledAttachment.GetArray<int>("m_influenceIndices");
        var influenceRotations = compiledAttachment.GetArray("m_influenceRotations").Select(v => v.ToQuaternion()).ToArray();
        var influenceOffsets = compiledAttachment.GetArray("m_influenceOffsets").Select(v => v.ToVector3()).ToArray();
        var influenceWeights = compiledAttachment.GetArray<double>("m_influenceWeights");
        var influenceCount = compiledAttachment.GetInt32Property("m_numInfluences");

        if (influenceCount == 0 || influenceIndices.Length < influenceCount)
            return string.Empty;

        var influences = new Attachment.Influence[influenceCount];
        var boneNames = LoadBoneNames();
        for (var i = 0; i < influenceCount; i++)
        {
            var boneIndex = influenceIndices[i];
            var boneName = (boneIndex >= 0 && boneIndex < boneNames.Length) ? boneNames[boneIndex] : $"bone_{boneIndex}";
            influences[i] = new Attachment.Influence
            {
                Name = boneName,
                Rotation = influenceRotations[i],
                Offset = influenceOffsets[i],
                Weight = (float)influenceWeights[i]
            };
        }

        const float epsilon = 0.001f;
        foreach (var (name, attachment) in attachments)
        {
            if (attachment.Length != influenceCount)
                continue;

            var posDiff = Vector3.DistanceSquared(attachment[0].Offset, influences[0].Offset);
            if (posDiff > epsilon)
                continue;

            var dot = Quaternion.Dot(attachment[0].Rotation, influences[0].Rotation);
            if (Math.Abs(Math.Abs(dot) - 1.0f) > epsilon)
                continue;

            return name;
        }

        return string.Empty;
    }

    private string GetSequenceName(int index)
    {
        var names = LoadSequenceNames();
        return names.TryGetValue(index, out var name) ? name : $"sequence_{index}";
    }

    private string GetWeightListName(int index)
    {
        var names = LoadWeightListNames();
        return names.TryGetValue(index, out var name) ? name : $"weightlist_{index}";
    }

    private string GetBoneName(int index)
    {
        var names = LoadBoneNames();
        return index >= 0 && index < names.Length ? names[index] : $"bone_{index}";
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

    /// <summary>
    /// Extracts a child node index from a child reference, which may be either an inline
    /// collection holding <c>m_nodeIndex</c> or a bare integer. Returns -1 when absent.
    /// </summary>
    private static int GetChildNodeIndex(KVObject child)
    {
        if (child.ValueType == KVValueType.Collection && child.ContainsKey("m_nodeIndex"))
            return child.GetInt32Property("m_nodeIndex");
        if (child.ValueType == KVValueType.Int32)
            return child.ToInt32();
        return -1;
    }

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

            var connections = new List<(int childIdx, string label)>();

            void AddConnection(int idx, string label)
            {
                if (idx >= 0)
                    connections.Add((idx, label));
            }

            if (compiledNode.ContainsKey("m_children"))
            {
                var children = compiledNode.GetArray("m_children");
                foreach (var child in children)
                {
                    int idx = GetChildNodeIndex(child);
                    AddConnection(idx, $"Child {idx}");
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
                        AddConnection(idx, $"Child {idx}");
                    }
                }
            }

            if (className == "CBlend2DUpdateNode" && compiledNode.ContainsKey("m_items"))
            {
                var items = compiledNode.GetArray("m_items");
                for (int itemIdx = 0; itemIdx < items.Count; itemIdx++)
                {
                    var item = items[itemIdx];
                    if (item.ContainsKey("m_pChild"))
                    {
                        var childRef = item.GetSubCollection("m_pChild");
                        if (childRef.ContainsKey("m_nodeIndex"))
                        {
                            int idx = childRef.GetInt32Property("m_nodeIndex");
                            if (idx >= 0)
                            {
                                int seqIdx = item.GetInt32Property("m_hSequence", -1);
                                string seqDisplay = seqIdx >= 0 ? GetSequenceName(seqIdx) : "None";
                                var pos = ParseVector2(item, "m_vPos");
                                string label = $"Item {itemIdx} (Seq: {seqDisplay}) ({pos.x:F1}, {pos.y:F1})";
                                AddConnection(idx, label);
                            }
                        }
                    }
                }
            }

            if (className == "CStateMachineUpdateNode" && compiledNode.ContainsKey("m_stateData"))
            {
                var stateDataArray = compiledNode.GetArray("m_stateData");
                var stateMachine = compiledNode.GetSubCollection("m_stateMachine");
                if (stateMachine != null && stateMachine.ContainsKey("m_states"))
                {
                    var states = stateMachine.GetArray("m_states");
                    for (int s = 0; s < stateDataArray.Count; s++)
                    {
                        var stateData = stateDataArray[s];
                        if (stateData.ContainsKey("m_pChild"))
                        {
                            var childRef = stateData.GetSubCollection("m_pChild");
                            if (childRef.ContainsKey("m_nodeIndex"))
                            {
                                int idx = childRef.GetInt32Property("m_nodeIndex");
                                if (idx >= 0)
                                {
                                    string stateName = states[s].GetStringProperty("m_name", $"State {s}");
                                    AddConnection(idx, stateName);
                                }
                            }
                        }
                    }
                }
            }

            if (className == "CChoiceUpdateNode" && compiledNode.ContainsKey("m_children"))
            {
                var children = compiledNode.GetArray("m_children");
                float[]? weights = compiledNode.ContainsKey("m_weights") ? compiledNode.GetFloatArray("m_weights") : null;
                float[]? blendTimes = compiledNode.ContainsKey("m_blendTimes") ? compiledNode.GetFloatArray("m_blendTimes") : null;

                var newConnections = new List<(int, string)>();
                for (int c = 0; c < children.Count; c++)
                {
                    int idx = GetChildNodeIndex(children[c]);
                    if (idx >= 0)
                    {
                        float w = (weights != null && c < weights.Length) ? weights[c] : 1.0f;
                        float bt = (blendTimes != null && c < blendTimes.Length) ? blendTimes[c] : 0.0f;
                        newConnections.Add((idx, $"Item {c} (W:{w:F2} BT:{bt:F2})"));
                    }
                }
                connections = newConnections;
            }

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
                    if (paramObj != null && paramObj.GetStringProperty("_class") == "CEnumAnimParameter" && paramObj.ContainsKey("m_enumOptions"))
                    {
                        var enumOptions = paramObj.GetArray<string>("m_enumOptions");
                        if (enumOptions.Length > 0 && compiledNode.ContainsKey("m_children"))
                        {
                            var children = compiledNode.GetArray("m_children");
                            var newConnections = new List<(int, string)>();
                            for (int c = 0; c < children.Count; c++)
                            {
                                int idx = GetChildNodeIndex(children[c]);
                                if (idx >= 0)
                                {
                                    string label = (c < enumOptions.Length) ? enumOptions[c] : $"Option {c}";
                                    newConnections.Add((idx, label));
                                }
                            }
                            connections = newConnections;
                        }
                    }
                }
            }

            foreach (var (childIdx, label) in connections)
            {
                if (!nodeMap.TryGetValue(childIdx, out var childNode))
                    continue;

                if (!childNode.Sockets.OfType<SocketOut>().Any())
                {
                    var outSocket = new SocketOut(typeof(Pose), string.Empty, childNode);
                    childNode.Sockets.Add(outSocket);
                }

                var inputSocket = new SocketIn(typeof(Pose), label, parentNode, hub: true);
                parentNode.Sockets.Add(inputSocket);

                var childOutput = childNode.Sockets.OfType<SocketOut>().First();
                nodeGraph.Connect(childOutput, inputSocket);
            }

            parentNode.Calculate();
        }
        nodeGraph.LayoutNodes();
    }

    /// <summary>
    /// Formats an index-to-name lookup as "Label: name (idx N)", or "Label: None" for negative indices.
    /// </summary>
    private static string FormatIndexed(string label, int index, Func<int, string> resolver)
        => index >= 0 ? $"{label}: {resolver(index)} (idx {index})" : $"{label}: None";

    /// <summary>
    /// Adds a "Label: name (idx N)" text line for an integer property naming a resolvable resource.
    /// </summary>
    private static void AddIndexedName(Node node, KVObject obj, string key, string label, Func<int, string> resolver)
    {
        if (obj.ContainsKey(key))
            node.AddText(FormatIndexed(label, obj.GetInt32Property(key), resolver));
    }

    private static void ApplyNetworkMode(Node node, KVObject obj)
    {
        if (obj.GetStringProperty("m_networkMode", "").Equals("ClientSimulate", StringComparison.Ordinal))
            node.SetBaseColor(new SKColor(118, 75, 140));
    }

    private static string FormatDamping(KVObject damping)
    {
        var speedFunc = damping.GetStringProperty("m_speedFunction", "Unknown");
        var speedScale = damping.GetFloatProperty("m_fSpeedScale", 1.0f);
        return $"{speedFunc} (scale {speedScale:F2})";
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

        if (className is "CSequenceUpdateNode" or "CSingleFrameUpdateNode" or "CCycleControlClipUpdateNode")
        {
            if (compiledNode.ContainsKey("m_hSequence"))
            {
                int seqIdx = compiledNode.GetInt32Property("m_hSequence");
                if (seqIdx >= 0)
                {
                    var seqName = GetSequenceName(seqIdx);
                    if (!string.IsNullOrEmpty(seqName) && !seqName.StartsWith("sequence_"))
                    {
                        nodeName = seqName;
                    }
                }
            }
        }

        var node = new Node(compiledNode)
        {
            Name = $"({index}) {nodeName}",
            NodeType = displayName,
        };

        if (className != null && ClassColor.TryGetValue(className, out var color))
            node.HeaderColor = color;
        else
            node.HeaderColor = PoseColor;

        ApplyNetworkMode(node, compiledNode);

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

            if (key == "m_hSequence")
            {
                node.AddText(FormatIndexed("Sequence", kv.Value.ToInt32(), GetSequenceName));
                continue;
            }

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

        if (className == "CSequenceUpdateNode")
        {
            if (compiledNode.ContainsKey("m_duration"))
                node.AddText($"Duration: {compiledNode.GetFloatProperty("m_duration"):F2}");
            if (compiledNode.ContainsKey("m_playbackSpeed"))
                node.AddText($"Speed: {compiledNode.GetFloatProperty("m_playbackSpeed"):F2}");
            if (compiledNode.ContainsKey("m_bLoop"))
                node.AddText($"Loop: {compiledNode.GetBooleanProperty("m_bLoop")}");
        }

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
                    node.AddText($"Damping: {FormatDamping(damping)}");
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
                    string seqDisplay = seqIdx >= 0 ? GetSequenceName(seqIdx) : "None";
                    var pos = ParseVector2(item, "m_vPos");
                    float dur = item.GetFloatProperty("m_flDuration");
                    node.AddText($"  [{i}] Seq: {seqDisplay} ({pos.x:F1}, {pos.y:F1}) dur={dur:F2}s");
                }
            }
        }

        if (className == "CBoneMaskUpdateNode")
        {
            AddIndexedName(node, compiledNode, "m_nWeightListIndex", "Bone Mask", GetWeightListName);
            if (compiledNode.ContainsKey("m_blendSpace"))
                node.AddText($"Blend Space: {compiledNode.GetStringProperty("m_blendSpace")}");
            if (compiledNode.ContainsKey("m_flRootMotionBlend"))
                node.AddText($"Root Motion Blend: {compiledNode.GetFloatProperty("m_flRootMotionBlend"):F2}");
            if (compiledNode.ContainsKey("m_bUseBlendScale"))
                node.AddText($"Use Blend Scale: {compiledNode.GetBooleanProperty("m_bUseBlendScale")}");
        }

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

        if (className == "CTwoBoneIKUpdateNode" && compiledNode.ContainsKey("m_opFixedData"))
        {
            var opFixedData = compiledNode.GetSubCollection("m_opFixedData");

            if (opFixedData.ContainsKey("m_endEffectorType"))
                node.AddText($"End Effector Type: {opFixedData.GetStringProperty("m_endEffectorType")}");

            if (opFixedData.ContainsKey("m_targetType"))
                node.AddText($"Target Type: {opFixedData.GetStringProperty("m_targetType")}");

            int fixedIdx = opFixedData.GetInt32Property("m_nFixedBoneIndex", -1);
            int middleIdx = opFixedData.GetInt32Property("m_nMiddleBoneIndex", -1);
            int endIdx = opFixedData.GetInt32Property("m_nEndBoneIndex", -1);
            if (fixedIdx >= 0 && middleIdx >= 0 && endIdx >= 0)
            {
                var chainName = GetIKChainNameByBoneIndices(fixedIdx, middleIdx, endIdx);
                if (!string.IsNullOrEmpty(chainName))
                    node.AddText($"IK Chain: {chainName}");
            }

            // Attachments
            //if (opFixedData.ContainsKey("m_endEffectorAttachment"))
            //{
            //var attachObj = opFixedData.GetSubCollection("m_endEffectorAttachment");
            //var name = FindMatchingAttachmentName(attachObj);
            //node.AddText($"End Effector Attachment: {(!string.IsNullOrEmpty(name) ? name : "(unresolved)")}");
            //}
            //if (opFixedData.ContainsKey("m_targetAttachment"))
            //{
            //var attachObj = opFixedData.GetSubCollection("m_targetAttachment");
            //var name = FindMatchingAttachmentName(attachObj);
            //node.AddText($"Target Attachment: {(!string.IsNullOrEmpty(name) ? name : "(unresolved)")}");
            //}

            if (opFixedData.ContainsKey("m_hPositionParam"))
            {
                var handle = opFixedData.GetSubCollection("m_hPositionParam");
                node.AddText($"Position Param: {GetParameterDescriptionFromHandle(handle)}");
            }
            if (opFixedData.ContainsKey("m_hRotationParam"))
            {
                var handle = opFixedData.GetSubCollection("m_hRotationParam");
                node.AddText($"Rotation Param: {GetParameterDescriptionFromHandle(handle)}");
            }

            string[] extraProps = { "m_bAlwaysUseFallbackHinge", "m_vLsFallbackHingeAxis", "m_bMatchTargetOrientation", "m_bConstrainTwist", "m_flMaxTwist" };
            foreach (var prop in extraProps)
            {
                if (opFixedData.ContainsKey(prop))
                {
                    var val = opFixedData[prop];
                    if (!val.IsCollection && !val.IsArray)
                        node.AddText($"{prop}: {val}");
                }
            }
        }
        if (className == "CAimMatrixUpdateNode")
        {
            if (compiledNode.ContainsKey("m_target"))
                node.AddText($"Target: {compiledNode.GetStringProperty("m_target")}");

            AddIndexedName(node, compiledNode, "m_hSequence", "Sequence", GetSequenceName);

            if (compiledNode.ContainsKey("m_bResetChild"))
                node.AddText($"Reset Child: {compiledNode.GetBooleanProperty("m_bResetChild")}");

            if (compiledNode.ContainsKey("m_bLockWhenWaning"))
                node.AddText($"Lock When Waning: {compiledNode.GetBooleanProperty("m_bLockWhenWaning")}");

            if (compiledNode.ContainsKey("m_opFixedSettings"))
            {
                var settings = compiledNode.GetSubCollection("m_opFixedSettings");

                if (settings.ContainsKey("m_attachment"))
                {
                    var attachObj = settings.GetSubCollection("m_attachment");
                    var name = FindMatchingAttachmentName(attachObj);
                    node.AddText($"Attachment: {(!string.IsNullOrEmpty(name) ? name : "(unresolved)")}");
                }

                if (settings.ContainsKey("m_damping"))
                {
                    node.AddText($"Damping: {FormatDamping(settings.GetSubCollection("m_damping"))}");
                }

                if (settings.ContainsKey("m_eBlendMode"))
                    node.AddText($"Blend Mode: {settings.GetStringProperty("m_eBlendMode")}");

                if (settings.ContainsKey("m_flMaxYawAngle"))
                    node.AddText($"Max Yaw Angle: {settings.GetFloatProperty("m_flMaxYawAngle"):F2}");

                if (settings.ContainsKey("m_flMaxPitchAngle"))
                    node.AddText($"Max Pitch Angle: {settings.GetFloatProperty("m_flMaxPitchAngle"):F2}");

                AddIndexedName(node, settings, "m_nBoneMaskIndex", "Bone Mask", GetWeightListName);

                if (settings.ContainsKey("m_bTargetIsPosition"))
                    node.AddText($"Target Is Position: {settings.GetBooleanProperty("m_bTargetIsPosition")}");

                if (settings.ContainsKey("m_bUseBiasAndClamp"))
                    node.AddText($"Use Bias And Clamp: {settings.GetBooleanProperty("m_bUseBiasAndClamp")}");

                if (settings.ContainsKey("m_flBiasAndClampYawOffset"))
                    node.AddText($"Bias/Clamp Yaw Offset: {settings.GetFloatProperty("m_flBiasAndClampYawOffset"):F2}");

                if (settings.ContainsKey("m_flBiasAndClampPitchOffset"))
                    node.AddText($"Bias/Clamp Pitch Offset: {settings.GetFloatProperty("m_flBiasAndClampPitchOffset"):F2}");

                if (settings.ContainsKey("m_biasAndClampBlendCurve"))
                {
                    var curve = settings.GetSubCollection("m_biasAndClampBlendCurve");
                    var cp1 = curve.GetFloatProperty("m_flControlPoint1", 0f);
                    var cp2 = curve.GetFloatProperty("m_flControlPoint2", 1f);
                    node.AddText($"Bias/Clamp Curve: ({cp1:F2}, {cp2:F2})");
                }
            }
        }
        if (className == "CFollowAttachmentUpdateNode" && compiledNode.ContainsKey("m_opFixedData"))
        {
            var opFixedData = compiledNode.GetSubCollection("m_opFixedData");

            AddIndexedName(node, opFixedData, "m_boneIndex", "Bone", GetBoneName);

            if (opFixedData.ContainsKey("m_attachment"))
            {
                var attachObj = opFixedData.GetSubCollection("m_attachment");
                var name = FindMatchingAttachmentName(attachObj);
                node.AddText($"Attachment: {(!string.IsNullOrEmpty(name) ? name : "(unresolved)")}");
            }

            if (opFixedData.ContainsKey("m_bMatchTranslation"))
                node.AddText($"Match Translation: {opFixedData.GetBooleanProperty("m_bMatchTranslation")}");
            if (opFixedData.ContainsKey("m_bMatchRotation"))
                node.AddText($"Match Rotation: {opFixedData.GetBooleanProperty("m_bMatchRotation")}");
        }

        if (className == "CFootPinningUpdateNode")
        {
            if (compiledNode.ContainsKey("m_eTimingSource"))
                node.AddText($"Timing Source: {compiledNode.GetStringProperty("m_eTimingSource")}");
            if (compiledNode.ContainsKey("m_bResetChild"))
                node.AddText($"Reset Child: {compiledNode.GetBooleanProperty("m_bResetChild")}");

            if (compiledNode.ContainsKey("m_params"))
            {
                var paramsArray = compiledNode.GetArray("m_params");
                if (paramsArray != null && paramsArray.Count > 0)
                {
                    var paramDescs = paramsArray.Select(h => GetParameterDescriptionFromHandle(h));
                    node.AddText($"Params: {string.Join(", ", paramDescs)}");
                }
            }

            if (compiledNode.ContainsKey("m_poseOpFixedData"))
            {
                var poseData = compiledNode.GetSubCollection("m_poseOpFixedData");

                if (poseData.ContainsKey("m_flBlendTime"))
                    node.AddText($"Blend Time: {poseData.GetFloatProperty("m_flBlendTime"):F2}");
                if (poseData.ContainsKey("m_flLockBreakDistance"))
                    node.AddText($"Lock Break Distance: {poseData.GetFloatProperty("m_flLockBreakDistance"):F2}");
                if (poseData.ContainsKey("m_flMaxLegTwist"))
                    node.AddText($"Max Leg Twist: {poseData.GetFloatProperty("m_flMaxLegTwist"):F2}");

                AddIndexedName(node, poseData, "m_nHipBoneIndex", "Hip Bone", GetBoneName);

                if (poseData.ContainsKey("m_bApplyLegTwistLimits"))
                    node.AddText($"Apply Leg Twist Limits: {poseData.GetBooleanProperty("m_bApplyLegTwistLimits")}");
                if (poseData.ContainsKey("m_bApplyFootRotationLimits"))
                    node.AddText($"Apply Foot Rotation Limits: {poseData.GetBooleanProperty("m_bApplyFootRotationLimits")}");

                if (poseData.ContainsKey("m_footInfo"))
                {
                    var footInfoArray = poseData.GetArray("m_footInfo");
                    if (footInfoArray.Count > 0)
                    {
                        node.AddText($"Feet ({footInfoArray.Count}):");
                        for (int i = 0; i < footInfoArray.Count; i++)
                        {
                            var foot = footInfoArray[i];
                            var footLines = new List<string>();

                            if (foot.ContainsKey("m_nFootIndex"))
                                footLines.Add(FormatIndexed("Foot", foot.GetInt32Property("m_nFootIndex"), GetFootName));
                            if (foot.ContainsKey("m_nTargetBoneIndex"))
                                footLines.Add(FormatIndexed("Target Bone", foot.GetInt32Property("m_nTargetBoneIndex"), GetBoneName));
                            if (foot.ContainsKey("m_nAnkleBoneIndex"))
                                footLines.Add(FormatIndexed("Ankle Bone", foot.GetInt32Property("m_nAnkleBoneIndex"), GetBoneName));
                            if (foot.ContainsKey("m_nIKAnchorBoneIndex"))
                                footLines.Add(FormatIndexed("IK Anchor Bone", foot.GetInt32Property("m_nIKAnchorBoneIndex"), GetBoneName));
                            if (foot.ContainsKey("m_ikChainIndex"))
                                footLines.Add(FormatIndexed("IK Chain", foot.GetInt32Property("m_ikChainIndex"), GetIKChainName));
                            if (foot.ContainsKey("m_nTagIndex"))
                                footLines.Add(FormatIndexed("Tag", foot.GetInt32Property("m_nTagIndex"), GetTagName));
                            if (foot.ContainsKey("m_flMaxIKLength"))
                                footLines.Add($"Max IK Length: {foot.GetFloatProperty("m_flMaxIKLength"):F2}");
                            if (foot.ContainsKey("m_flMaxRotationLeft"))
                                footLines.Add($"Max Rotation Left: {foot.GetFloatProperty("m_flMaxRotationLeft"):F2}");
                            if (foot.ContainsKey("m_flMaxRotationRight"))
                                footLines.Add($"Max Rotation Right: {foot.GetFloatProperty("m_flMaxRotationRight"):F2}");

                            node.AddText($"  [{i}] {string.Join(", ", footLines)}");
                        }
                    }
                }
            }
        }

        nodeGraph.AddNode(node);
        return node;
    }

    private static SKColor GetParameterTypeColor(string type)
    {
        return type switch
        {
            "BOOL" => new SKColor(25, 78, 101),
            "INT" => new SKColor(145, 102, 54),
            "FLOAT" => new SKColor(163, 171, 37),
            "ENUM" => new SKColor(89, 27, 111),
            "VECTOR" => new SKColor(26, 122, 59),
            "QUATERNION" => new SKColor(123, 58, 60),
            "SYMBOL" => new SKColor(200, 200, 200),
            "VIRTUAL" => new SKColor(150, 150, 150),
            _ => new SKColor(128, 128, 128),
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

            ApplyNetworkMode(node, comp);

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
            BaseColor = NodeColor;
            TextColor = new SKColor(230, 230, 230);
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

    public override void Dispose()
    {
        modelResource?.Dispose();
        modelResource = null;
        modelResourceLoaded = false;

        base.Dispose();
        GC.SuppressFinalize(this);
    }

    private struct Pose { }
}
