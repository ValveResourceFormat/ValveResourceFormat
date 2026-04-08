using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelData.Attachments;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Extracts and converts animation graph resources to editable format.
/// </summary>
public class AnimationGraphExtract : IDisposable
{
    private readonly BinaryKV3 resourceData;
    private KVObject Graph => resourceData.Data;
    private readonly string? outputFileName;
    private readonly IFileLoader fileLoader;
    private Dictionary<int, string>? weightListNamesCache;
    private Dictionary<int, string>? sequenceNamesCache;
    private Dictionary<long, KVObject>? compiledNodeIndexMap;
    private Dictionary<long, long>? nodeIndexToIdMap;
    private Dictionary<string, Attachment>? modelAttachments;
    private string[]? modelBoneNamesCache;
    private string[]? modelIKChainNamesCache;
    private string[]? modelFootNamesCache;
    private LookAtChainInfo[]? modelLookAtChainInfoCache;
    private Dictionary<string, List<string>>? modelIKChainBonesCache;
    private List<KVObject>? footPinningItems;
    private KVObject? scriptManager;

    private Resource? modelResourceCache;
    private bool modelResourceLoaded;

    private Resource? ModelResource
    {
        get
        {
            if (!modelResourceLoaded)
            {
                modelResourceLoaded = true;
                var modelName = Graph.GetStringProperty("m_modelName");
                if (!string.IsNullOrEmpty(modelName))
                {
                    modelResourceCache = fileLoader.LoadFileCompiled(modelName);
                }
            }
            return modelResourceCache;
        }
    }

    private Model? ModelData => ModelResource?.DataBlock as Model;

    private enum PropAction
    {
        Copy,
        Rename,
        ParamRef,
        InputConnection,
        BlendDuration,
        BlendCurve,
        TagIndex,
        TagBehavior,
        Skip
    }

    private static readonly Dictionary<string, Dictionary<string, (PropAction Action, string? OutputKey)>> PropertyMappings
        = new(StringComparer.Ordinal)
        {
            ["CMover"] = new(StringComparer.Ordinal)
            {
                ["m_pChildNode"] = (PropAction.InputConnection, null),
                ["m_hMoveVecParam"] = (PropAction.ParamRef, "m_moveVectorParam"),
                ["m_hMoveHeadingParam"] = (PropAction.ParamRef, "m_moveHeadingParam"),
                ["m_hTurnToFaceParam"] = (PropAction.ParamRef, "m_param"),
                ["m_facingTarget"] = (PropAction.Copy, null),
                ["m_flTurnToFaceOffset"] = (PropAction.Copy, null),
                ["m_flTurnToFaceLimit"] = (PropAction.Copy, null),
                ["m_bAdditive"] = (PropAction.Copy, null),
                ["m_bApplyMovement"] = (PropAction.Copy, null),
                ["m_bOrientMovement"] = (PropAction.Copy, null),
                ["m_bApplyRotation"] = (PropAction.Rename, "m_bTurnToFace"),
                ["m_bLimitOnly"] = (PropAction.Copy, null),
                ["m_damping"] = (PropAction.Copy, null),
            },
            ["CSelector"] = new(StringComparer.Ordinal)
            {
                ["m_eTagBehavior"] = (PropAction.TagBehavior, null),
                ["m_flBlendTime"] = (PropAction.BlendDuration, null),
                ["m_blendCurve"] = (PropAction.BlendCurve, null),
                ["m_nTagIndex"] = (PropAction.TagIndex, null),
                ["m_bResetOnChange"] = (PropAction.Copy, null),
                ["m_bLockWhenWaning"] = (PropAction.Copy, null),
                ["m_bSyncCyclesOnChange"] = (PropAction.Copy, null),
            },
            ["CBoneMask"] = new(StringComparer.Ordinal)
            {
                ["m_hBlendParameter"] = (PropAction.ParamRef, "m_blendParameter"),
            },
            ["CSequence"] = new(StringComparer.Ordinal)
            {
                ["m_duration"] = (PropAction.Skip, null),
            },
            ["CStateMachine"] = new(StringComparer.Ordinal)
            {
                ["m_stateMachine"] = (PropAction.Skip, null),
                ["m_stateData"] = (PropAction.Skip, null),
                ["m_transitionData"] = (PropAction.Skip, null),
            },
            ["CChoice"] = new(StringComparer.Ordinal)
            {
                ["m_weights"] = (PropAction.Skip, null),
                ["m_blendTimes"] = (PropAction.Skip, null),
            },
            ["CBlend"] = new(StringComparer.Ordinal)
            {
                ["m_targetValues"] = (PropAction.Skip, null),
                ["m_sortedOrder"] = (PropAction.Skip, null),
            },
            ["CAdd"] = new(StringComparer.Ordinal)
            {
                ["m_bResetChild1"] = (PropAction.Rename, "m_bResetBase"),
                ["m_bResetChild2"] = (PropAction.Rename, "m_bResetAdditive"),
            },
            ["CSubtract"] = new(StringComparer.Ordinal)
            {
                ["m_bResetChild1"] = (PropAction.Rename, "m_bResetBase"),
                ["m_bResetChild2"] = (PropAction.Rename, "m_bResetSubtract"),
            },
            ["CTurnHelper"] = new(StringComparer.Ordinal)
            {
                ["m_pChildNode"] = (PropAction.InputConnection, null),
                ["m_facingTarget"] = (PropAction.Copy, null),
                ["m_turnStartTimeOffset"] = (PropAction.Rename, "m_turnStartTime"),
                ["m_turnDuration"] = (PropAction.Copy, null),
                ["m_bMatchChildDuration"] = (PropAction.Copy, null),
                ["m_manualTurnOffset"] = (PropAction.Copy, null),
                ["m_bUseManualTurnOffset"] = (PropAction.Copy, null),
            },
            ["CAimMatrix"] = new(StringComparer.Ordinal)
            {
                ["m_target"] = (PropAction.Copy, null),
                ["m_paramIndex"] = (PropAction.ParamRef, "m_param"),
                ["m_bResetChild"] = (PropAction.Rename, "m_bResetBase"),
                ["m_bLockWhenWaning"] = (PropAction.Copy, null),
            },
            ["CDirectionalBlend"] = new(StringComparer.Ordinal)
            {
                ["m_paramIndex"] = (PropAction.ParamRef, "m_param"),
                ["m_blendValueSource"] = (PropAction.Copy, null),
                ["m_playbackSpeed"] = (PropAction.Copy, null),
                ["m_bLoop"] = (PropAction.Copy, null),
                ["m_bLockBlendOnReset"] = (PropAction.Copy, null),
                ["m_damping"] = (PropAction.Copy, null),
                ["m_duration"] = (PropAction.Skip, null),
            },
            ["CFollowAttachment"] = new(StringComparer.Ordinal)
            {
                ["m_pChildNode"] = (PropAction.InputConnection, null),
            },
            ["CFootAdjustment"] = new(StringComparer.Ordinal)
            {
                ["m_pChildNode"] = (PropAction.InputConnection, null),
                ["m_facingTarget"] = (PropAction.ParamRef, null),
                ["m_bResetChild"] = (PropAction.Copy, null),
                ["m_bAnimationDriven"] = (PropAction.Copy, null),
                ["m_flTurnTimeMin"] = (PropAction.Copy, null),
                ["m_flTurnTimeMax"] = (PropAction.Copy, null),
                ["m_flStepHeightMax"] = (PropAction.Copy, null),
                ["m_flStepHeightMaxAngle"] = (PropAction.Copy, null),
                ["m_hBasePoseCacheHandle"] = (PropAction.Skip, null),
            },
            ["CFootPinning"] = new(StringComparer.Ordinal)
            {
                ["m_pChildNode"] = (PropAction.InputConnection, null),
                ["m_eTimingSource"] = (PropAction.Copy, null),
                ["m_bResetChild"] = (PropAction.Copy, null),
            },
            ["CFootStepTrigger"] = new(StringComparer.Ordinal)
            {
                ["m_pChildNode"] = (PropAction.InputConnection, null),
                ["m_flTolerance"] = (PropAction.Copy, null),
            },
            ["CJiggleBone"] = new(StringComparer.Ordinal)
            {
                ["m_pChildNode"] = (PropAction.InputConnection, null),
            },
            ["CBlend2D"] = new(StringComparer.Ordinal)
            {
                ["m_paramX"] = (PropAction.ParamRef, null),
                ["m_paramY"] = (PropAction.ParamRef, null),
                ["m_eBlendMode"] = (PropAction.Copy, null),
                ["m_blendSourceX"] = (PropAction.Copy, null),
                ["m_blendSourceY"] = (PropAction.Copy, null),
            },
            ["CLookAt"] = new(StringComparer.Ordinal)
            {
                ["m_target"] = (PropAction.Copy, null),
                ["m_paramIndex"] = (PropAction.ParamRef, "m_param"),
                ["m_weightParamIndex"] = (PropAction.ParamRef, "m_weightParam"),
                ["m_bResetChild"] = (PropAction.Rename, "m_bResetBase"),
                ["m_bLockWhenWaning"] = (PropAction.Copy, null),
            },
            ["CHitReact"] = new(StringComparer.Ordinal)
            {
                ["m_networkMode"] = (PropAction.Copy, null),
                ["m_triggerParam"] = (PropAction.ParamRef, null),
                ["m_hitBoneParam"] = (PropAction.ParamRef, null),
                ["m_hitOffsetParam"] = (PropAction.ParamRef, null),
                ["m_hitDirectionParam"] = (PropAction.ParamRef, null),
                ["m_hitStrengthParam"] = (PropAction.ParamRef, null),
                ["m_flMinDelayBetweenHits"] = (PropAction.Copy, null),
                ["m_bResetChild"] = (PropAction.Rename, "m_bResetBase"),
            },
            ["CSolveIKChain"] = new(StringComparer.Ordinal)
            {
                ["m_networkMode"] = (PropAction.Copy, null),
                ["m_targetHandles"] = (PropAction.Skip, null),
            },
            ["CStanceOverride"] = new(StringComparer.Ordinal)
            {
                ["m_networkMode"] = (PropAction.Copy, null),
                ["m_hParameter"] = (PropAction.ParamRef, "m_blendParamID"),
                ["m_eMode"] = (PropAction.Copy, null),
                ["m_footStanceInfo"] = (PropAction.Skip, null),
            },
            ["CStopAtGoal"] = new(StringComparer.Ordinal)
            {
                ["m_networkMode"] = (PropAction.Copy, null),
                ["m_flOuterRadius"] = (PropAction.Copy, null),
                ["m_flInnerRadius"] = (PropAction.Copy, null),
                ["m_flMaxScale"] = (PropAction.Copy, null),
                ["m_flMinScale"] = (PropAction.Copy, null),
                ["m_damping"] = (PropAction.Copy, null),
            },
            ["CTargetWarp"] = new(StringComparer.Ordinal)
            {
                ["m_eAngleMode"] = (PropAction.Copy, null),
                ["m_hTargetPositionParameter"] = (PropAction.ParamRef, "m_targetPositionParamID"),
                ["m_hTargetUpVectorParameter"] = (PropAction.ParamRef, "m_targetUpVectorParamID"),
                ["m_hTargetFacePositionParameter"] = (PropAction.ParamRef, "m_targetFacePositionParamID"),
                ["m_hMoveHeadingParameter"] = (PropAction.ParamRef, "m_moveHeadingParamID"),
                ["m_hDesiredMoveHeadingParameter"] = (PropAction.ParamRef, "m_desiredMoveHeadingParamID"),
                ["m_eCorrectionMethod"] = (PropAction.Copy, null),
                ["m_eTargetWarpTimingMethod"] = (PropAction.Copy, null),
                ["m_bTargetFacePositionIsWorldSpace"] = (PropAction.Copy, null),
                ["m_bTargetPositionIsWorldSpace"] = (PropAction.Copy, null),
                ["m_bOnlyWarpWhenTagIsFound"] = (PropAction.Copy, null),
                ["m_bWarpOrientationDuringTranslation"] = (PropAction.Copy, null),
                ["m_bWarpAroundCenter"] = (PropAction.Copy, null),
                ["m_flMaxAngle"] = (PropAction.Copy, null),
                ["m_networkMode"] = (PropAction.Copy, null),
            },
            ["COrientationWarp"] = new(StringComparer.Ordinal)
            {
                ["m_eMode"] = (PropAction.Copy, null),
                ["m_hTargetParam"] = (PropAction.ParamRef, "m_targetParamID"),
                ["m_hTargetPositionParam"] = (PropAction.ParamRef, "m_targetPositionParamID"),
                ["m_hFallbackTargetPositionParam"] = (PropAction.ParamRef, "m_fallbackTargetPositionParamID"),
                ["m_eTargetOffsetMode"] = (PropAction.Copy, null),
                ["m_flTargetOffset"] = (PropAction.Copy, null),
                ["m_hTargetOffsetParam"] = (PropAction.ParamRef, "m_targetOffsetParamID"),
                ["m_damping"] = (PropAction.Copy, null),
                ["m_eRootMotionSource"] = (PropAction.Copy, null),
                ["m_flMaxRootMotionScale"] = (PropAction.Copy, null),
                ["m_bEnablePreferredRotationDirection"] = (PropAction.Copy, null),
                ["m_ePreferredRotationDirection"] = (PropAction.Copy, null),
                ["m_flPreferredRotationThreshold"] = (PropAction.Copy, null),
                ["m_networkMode"] = (PropAction.Copy, null),
            },
            ["CPairedSequence"] = new(StringComparer.Ordinal)
            {
                ["m_sPairedSequenceRole"] = (PropAction.Rename, "m_sPairedRole"),
                ["m_playbackSpeed"] = (PropAction.Rename, "m_flPlaybackSpeed"),
                ["m_bLoop"] = (PropAction.Copy, null),
                ["m_networkMode"] = (PropAction.Copy, null),
            },
            ["CFollowTarget"] = new(StringComparer.Ordinal)
            {
                ["m_networkMode"] = (PropAction.Copy, null),
            },
            ["CMotionMatching"] = new(StringComparer.Ordinal)
            {
                ["m_blendCurve"] = (PropAction.BlendCurve, null),
                ["m_distanceScale_Damping"] = (PropAction.Copy, null),
                ["m_nRandomSeed"] = (PropAction.Copy, null),
                ["m_flSampleRate"] = (PropAction.Copy, null),
                ["m_bSearchEveryTick"] = (PropAction.Copy, null),
                ["m_flSearchInterval"] = (PropAction.Copy, null),
                ["m_bSearchWhenClipEnds"] = (PropAction.Copy, null),
                ["m_bSearchWhenGoalChanges"] = (PropAction.Copy, null),
                ["m_flBlendTime"] = (PropAction.Copy, null),
                ["m_flSelectionThreshold"] = (PropAction.Copy, null),
                ["m_flReselectionTimeWindow"] = (PropAction.Copy, null),
                ["m_bLockClipWhenWaning"] = (PropAction.Copy, null),
                ["m_bEnableRotationCorrection"] = (PropAction.Copy, null),
                ["m_bGoalAssist"] = (PropAction.Copy, null),
                ["m_flGoalAssistDistance"] = (PropAction.Copy, null),
                ["m_flGoalAssistTolerance"] = (PropAction.Copy, null),
                ["m_bEnableDistanceScaling"] = (PropAction.Copy, null),
                ["m_flDistanceScale_OuterRadius"] = (PropAction.Copy, null),
                ["m_flDistanceScale_InnerRadius"] = (PropAction.Copy, null),
                ["m_flDistanceScale_MaxScale"] = (PropAction.Copy, null),
                ["m_flDistanceScale_MinScale"] = (PropAction.Copy, null),
                ["m_networkMode"] = (PropAction.Copy, null),
            },
            ["CAimCamera"] = new(StringComparer.Ordinal)
            {
                ["m_hParameterPosition"] = (PropAction.ParamRef, "m_parameterNamePosition"),
                ["m_hParameterOrientation"] = (PropAction.ParamRef, "m_parameterNameOrientation"),
                ["m_hParameterPelvisOffset"] = (PropAction.ParamRef, "m_parameterNamePelvisOffset"),
                ["m_hParameterCameraOnly"] = (PropAction.ParamRef, "m_parameterCameraOnly"),
                ["m_hParameterCameraClearanceDistance"] = (PropAction.ParamRef, "m_parameterCameraClearanceDistance"),
                ["m_hParameterWeaponDepenetrationDistance"] = (PropAction.ParamRef, "m_parameterWeaponDepenetrationDistance"),
                ["m_hParameterWeaponDepenetrationDelta"] = (PropAction.ParamRef, "m_parameterWeaponDepenetrationDelta"),
                ["m_networkMode"] = (PropAction.Copy, null),
            },
            ["CJumpHelper"] = new(StringComparer.Ordinal)
            {
                ["m_hTargetParam"] = (PropAction.ParamRef, "m_targetParamID"),
                ["m_flJumpStartCycle"] = (PropAction.Copy, null),
                ["m_flOriginalJumpDuration"] = (PropAction.Skip, null),
                ["m_flOriginalJumpMovement"] = (PropAction.Skip, null),
                ["m_bScaleSpeed"] = (PropAction.Copy, null),
                ["m_playbackSpeed"] = (PropAction.Copy, null),
                ["m_bLoop"] = (PropAction.Copy, null),
                ["m_eCorrectionMethod"] = (PropAction.Copy, null),
                ["m_duration"] = (PropAction.Skip, null),
            },
            ["CLeanMatrix"] = new(StringComparer.Ordinal)
            {
                ["m_paramIndex"] = (PropAction.ParamRef, "m_param"),
                ["m_verticalAxis"] = (PropAction.Rename, "m_verticalAxisDirection"),
                ["m_horizontalAxis"] = (PropAction.Rename, "m_horizontalAxisDirection"),
                ["m_damping"] = (PropAction.Copy, null),
                ["m_blendSource"] = (PropAction.Copy, null),
                ["m_flMaxValue"] = (PropAction.Copy, null),
                ["m_frameCorners"] = (PropAction.Skip, null),
                ["m_poses"] = (PropAction.Skip, null),
                ["m_nSequenceMaxFrame"] = (PropAction.Skip, null),
            },
            ["CFootLock"] = new(StringComparer.Ordinal)
            {
                ["m_hipShiftDamping"] = (PropAction.Copy, null),
                ["m_rootHeightDamping"] = (PropAction.Copy, null),
                ["m_flStrideCurveScale"] = (PropAction.Copy, null),
                ["m_flStrideCurveLimitScale"] = (PropAction.Copy, null),
                ["m_flStepHeightIncreaseScale"] = (PropAction.Copy, null),
                ["m_flStepHeightDecreaseScale"] = (PropAction.Copy, null),
                ["m_flHipShiftScale"] = (PropAction.Copy, null),
                ["m_flBlendTime"] = (PropAction.Copy, null),
                ["m_flMaxRootHeightOffset"] = (PropAction.Copy, null),
                ["m_flMinRootHeightOffset"] = (PropAction.Copy, null),
                ["m_flTiltPlanePitchSpringStrength"] = (PropAction.Copy, null),
                ["m_flTiltPlaneRollSpringStrength"] = (PropAction.Copy, null),
                ["m_bApplyFootRotationLimits"] = (PropAction.Copy, null),
                ["m_bApplyHipShift"] = (PropAction.Rename, "m_bEnableHipShift"),
                ["m_bModulateStepHeight"] = (PropAction.Copy, null),
                ["m_bResetChild"] = (PropAction.Copy, null),
                ["m_bEnableVerticalCurvedPaths"] = (PropAction.Copy, null),
                ["m_bEnableRootHeightDamping"] = (PropAction.Copy, null),
            },
        };

    private static readonly HashSet<string> NameToSNameClasses = new(StringComparer.Ordinal)
    {
        "CLeanMatrix", "CAdd", "CAimMatrix", "CBindPose", "CBlend2D", "CBlend", "CBoneMask",
        "CChoice", "CChoreo", "CCycleControl", "CCycleControlClip", "CDirectionalBlend", "CDirectPlayback",
        "CFollowAttachment", "CFollowPath", "CFootAdjustment", "CFootLock", "CFootStepTrigger", "CHitReact",
        "CInputStream", "CJiggleBone", "CLookAt", "CMotionMatching", "CMover", "CPathHelper", "CRagdoll",
        "CRoot", "CSelector", "CSequence", "CSetFacing", "CSingleFrame", "CSkeletalInput",
        "CSlowDownonSlopes", "CSolveIKChain", "CSpeedScale", "CStateMachine", "CStopAtGoal",
        "CSubtract", "CTurnHelper", "CTwoBoneIK", "CWayPointHelper", "CZeroPose", "CFootPinning",
        "CAimCamera", "CTargetWarp", "COrientationWarp", "CPairedSequence", "CFollowTarget"
    };

    private void HandleMappedProperty(
        KVObject node,
        KVObject compiledNode,
        string key,
        KVObject value,
        List<long> outConnections,
        PropAction action,
        string? outputKey)
    {
        var destKey = outputKey ?? key;
        switch (action)
        {
            case PropAction.Copy:
            case PropAction.Rename:
                node.Add(destKey, value);
                break;
            case PropAction.ParamRef:
                node.Add(destKey, ExtractParameterID(value));
                break;
            case PropAction.InputConnection:
                {
                    var nodeIndex = value.GetIntegerProperty("m_nodeIndex");
                    if (nodeIndexToIdMap?.TryGetValue(nodeIndex, out var nodeId) == true)
                    {
                        outConnections.Add(nodeId);
                        node.Add("m_inputConnection", MakeInputConnection(nodeId));
                    }
                    break;
                }
            case PropAction.BlendDuration:
                node.Add(destKey, ConvertBlendDuration(value));
                break;
            case PropAction.BlendCurve:
                node.Add(destKey, MakeBlendCurve(value));
                break;
            case PropAction.TagIndex:
                {
                    var tagIndex = compiledNode.GetIntegerProperty("m_nTagIndex");
                    node.Add("m_tag", MakeNodeIdObjectValue(GetTagIdFromIndex(tagIndex)));
                    if (tagIndex != -1 && !node.ContainsKey("m_selectionSource"))
                    {
                        node.Add("m_selectionSource", "SelectionSource_Tag");
                    }
                    break;
                }
            case PropAction.TagBehavior:
                node.Add("m_tagBehavior", value);
                break;
        }
    }


    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationGraphExtract"/> class.
    /// </summary>
    /// <param name="resource">The resource to extract from.</param>
    /// <param name="fileLoader"></param>
    public AnimationGraphExtract(Resource resource, IFileLoader fileLoader)
    {
        if (resource.DataBlock is not BinaryKV3 kv3)
        {
            throw new InvalidDataException("Resource data block is not a BinaryKV3");
        }

        resourceData = kv3;
        this.fileLoader = fileLoader;

        if (resource.FileName is not null)
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
        var isUncompiledAnimationGraph = Graph.GetStringProperty("_class") == "CAnimationGraph";
        var contentFile = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(isUncompiledAnimationGraph
                ? resourceData.Data.ToKV3String()
                : ToEditableAnimGraphVersion19()),
            FileName = outputFileName ?? "animgraph",
        };

        return contentFile;
    }

    /// <summary>
    /// Gets or sets the animation tags.
    /// </summary>
    public IReadOnlyList<KVObject> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the animation parameters.
    /// </summary>
    public IReadOnlyList<KVObject> Parameters { get; set; } = [];

    /// <summary>
    /// Builds the mapping from compiled node indices to their actual node IDs.
    /// </summary>
    private void BuildNodeIdMap(IReadOnlyList<KVObject> compiledNodes)
    {
        compiledNodeIndexMap = [];
        nodeIndexToIdMap = [];

        var assignedNodeIds = new HashSet<long>();
        var idCursor = GeneratedNodeIdMin;
        for (var i = 0; i < compiledNodes.Count; i++)
        {
            var compiledNode = compiledNodes[i];
            var nodeId = TryFindExistingNodeId(compiledNode, assignedNodeIds)
                ?? GenerateNewNodeId(assignedNodeIds, ref idCursor);

            assignedNodeIds.Add(nodeId);
            compiledNodeIndexMap[nodeId] = compiledNode;
            nodeIndexToIdMap[i] = nodeId;
        }
    }

    private static long? TryFindExistingNodeId(KVObject compiledNode, HashSet<long> assignedNodeIds)
    {
        var nodePath = compiledNode.GetSubCollection("m_nodePath");
        if (nodePath is null)
        {
            return null;
        }

        var path = nodePath.GetArray("m_path");
        var count = nodePath.GetIntegerProperty("m_nCount");
        if (count <= 0 || path is null || path.Count == 0)
        {
            return null;
        }

        for (var j = (int)count - 1; j >= 0; j--)
        {
            var id = path[j].GetIntegerProperty("m_id");
            if (id != uint.MaxValue)
            {
                return assignedNodeIds.Contains(id) ? null : id;
            }
        }
        return null;
    }

    private const long GeneratedNodeIdMin = 100_000_000L;
    private const long GeneratedNodeIdMax = 999_999_999L;

    private static long GenerateNewNodeId(HashSet<long> assignedNodeIds, ref long cursor)
    {
        if (cursor < GeneratedNodeIdMin)
        {
            cursor = GeneratedNodeIdMin;
        }
        while (cursor <= GeneratedNodeIdMax && assignedNodeIds.Contains(cursor))
        {
            cursor++;
        }
        if (cursor > GeneratedNodeIdMax)
        {
            throw new InvalidOperationException("Exhausted the generated node id range.");
        }
        return cursor++;
    }

    private sealed class LayoutNode(long id)
    {
        public long Id { get; } = id;
        public Vector2 Position { get; set; }
    }

    private sealed class LayoutConnection(LayoutNode source, LayoutNode target)
    {
        public LayoutNode Source { get; } = source;
        public LayoutNode Target { get; } = target;
    }

    private static void ApplyLayoutPositions(
        Dictionary<long, KVObject> createdNodes,
        Dictionary<long, LayoutNode> layoutNodes,
        List<LayoutConnection> connections)
    {
        if (layoutNodes.Count == 0)
        {
            return;
        }

        var nodeInputs = new Dictionary<LayoutNode, List<LayoutConnection>>();
        var nodeOutputs = new Dictionary<LayoutNode, List<LayoutConnection>>();

        foreach (var node in layoutNodes.Values)
        {
            nodeInputs[node] = [];
            nodeOutputs[node] = [];
        }

        foreach (var conn in connections)
        {
            nodeOutputs[conn.Source].Add(conn);
            nodeInputs[conn.Target].Add(conn);
        }

        var defaultSize = new Vector2(200f, 80f);

        GraphLayout.LayoutNodes(
            nodes: layoutNodes.Values,
            connections: connections,
            getPosition: n => n.Position,
            setPosition: (n, p) => n.Position = p,
            getSize: _ => defaultSize,
            getSourceNode: c => c.Source,
            getTargetNode: c => c.Target,
            getInputConnections: n => nodeInputs.GetValueOrDefault(n) ?? [],
            getOutputConnections: n => nodeOutputs.GetValueOrDefault(n) ?? []
        );

        foreach (var (nodeId, node) in createdNodes)
        {
            var pos = layoutNodes[nodeId].Position;
            node.Add("m_vecPosition", MakeVector2(pos.X, pos.Y));
        }
    }

    private string FindMatchingAttachmentName(KVObject compiledAttachment)
    {
        if (compiledAttachment is null)
        {
            return string.Empty;
        }

        // Try to get attachment name directly if stored as a string property
        if (compiledAttachment.ContainsKey("m_attachmentName"))
        {
            return compiledAttachment.GetStringProperty("m_attachmentName");
        }

        if (compiledAttachment.ContainsKey("m_name"))
        {
            return compiledAttachment.GetStringProperty("m_name");
        }

        if (modelAttachments == null || modelAttachments.Count == 0)
        {
            return string.Empty;
        }

        // not exactly the same keys as model attachment.
        var influenceIndices = compiledAttachment.GetArray<int>("m_influenceIndices");
        var influenceRotations = compiledAttachment.GetArray("m_influenceRotations").Select(v => v.ToQuaternion()).ToArray();
        var influenceOffsets = compiledAttachment.GetArray("m_influenceOffsets").Select(v => v.ToVector3()).ToArray();
        var influenceWeights = compiledAttachment.GetArray<double>("m_influenceWeights");

        var influenceCount = compiledAttachment.GetInt32Property("m_numInfluences");

        var influences = new Attachment.Influence[influenceCount];
        for (var i = 0; i < influenceCount; i++)
        {
            var boneName = GetBoneName(influenceIndices![i]);
            influences[i] = new Attachment.Influence
            {
                Name = boneName,
                Rotation = influenceRotations![i],
                Offset = influenceOffsets![i],
                Weight = (float)influenceWeights![i]
            };
        }

        foreach (var (name, attachment) in modelAttachments)
        {
            if (attachment.Length != influenceCount)
            {
                continue;
            }

            // Some rudamentary attachment matching
            const float epsilon = 0.001f;
            var posDiff = Vector3.DistanceSquared(attachment[0].Offset, influences[0].Offset);

            if (posDiff > epsilon)
            {
                continue;
            }

            var dot = Quaternion.Dot(attachment[0].Rotation, influences[0].Rotation);
            var absDot = Math.Abs(dot);

            if (Math.Abs(absDot - 1.0f) > epsilon)
            {
                continue;
            }

            return name;
        }

        return string.Empty;
    }

    private string[] LoadBoneNamesFromModel()
    {
        if (modelBoneNamesCache is not null)
        {
            return modelBoneNamesCache;
        }
        try
        {
            modelBoneNamesCache = ModelData?.Skeleton.Bones.Select(b => b.Name).ToArray() ?? [];
        }
        catch (Exception)
        {
            modelBoneNamesCache = [];
        }
        return modelBoneNamesCache;
    }
    private static string GetNameByIndex(string[] names, int index)
    {
        return index >= 0 && index < names.Length ? names[index] : string.Empty;
    }

    private static IReadOnlyList<KVObject>? GetIKChainsFromModel(Model? modelData)
    {
        if (modelData is null)
        {
            return null;
        }

        var keyvalues = modelData.KeyValues;
        if (!keyvalues.ContainsKey("ikdata"))
        {
            return null;
        }

        var ikdata = keyvalues.GetSubCollection("ikdata");
        return ikdata.ContainsKey("m_IKChains") ? ikdata.GetArray("m_IKChains") : null;
    }

    private string GetBoneName(int boneIndex)
    {
        return GetNameByIndex(LoadBoneNamesFromModel(), boneIndex);
    }
    private string[] LoadIKChainNamesFromModel()
    {
        if (modelIKChainNamesCache is not null)
        {
            return modelIKChainNamesCache;
        }
        try
        {
            modelIKChainNamesCache = GetIKChainsFromModel(ModelData)?
                .Select(c => c.GetStringProperty("m_Name"))
                .Where(n => !string.IsNullOrEmpty(n))
                .ToArray() ?? [];
        }
        catch (Exception)
        {
            modelIKChainNamesCache = [];
        }
        return modelIKChainNamesCache;
    }

    private Dictionary<string, List<string>> LoadIKChainBonesFromModel()
    {
        if (modelIKChainBonesCache is not null)
        {
            return modelIKChainBonesCache;
        }
        var chainBones = new Dictionary<string, List<string>>();
        try
        {
            var ikChains = GetIKChainsFromModel(ModelData);
            if (ikChains is null)
            {
                return chainBones;
            }

            foreach (var chain in ikChains)
            {
                var name = chain.GetStringProperty("m_Name");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var boneList = new List<string>();
                if (chain.ContainsKey("m_Joints"))
                {
                    foreach (var joint in chain.GetArray("m_Joints"))
                    {
                        if (joint.ContainsKey("m_Bone"))
                        {
                            var boneName = joint.GetSubCollection("m_Bone").GetStringProperty("m_Name");
                            if (!string.IsNullOrEmpty(boneName))
                            {
                                boneList.Add(boneName);
                            }
                        }
                    }
                }
                chainBones[name] = boneList;
            }
        }
        catch (Exception)
        {
            chainBones.Clear();
        }
        modelIKChainBonesCache = chainBones;
        return chainBones;
    }

    private string GetIKChainNameByBoneIndices(int fixedBoneIndex, int middleBoneIndex, int endBoneIndex)
    {
        var fixedBoneName = GetBoneName(fixedBoneIndex);
        var middleBoneName = GetBoneName(middleBoneIndex);
        var endBoneName = GetBoneName(endBoneIndex);

        if (string.IsNullOrEmpty(fixedBoneName) || string.IsNullOrEmpty(middleBoneName) || string.IsNullOrEmpty(endBoneName))
        {
            return string.Empty;
        }

        foreach (var (chainName, bones) in LoadIKChainBonesFromModel())
        {
            if (bones.Count == 3 && bones[0] == fixedBoneName && bones[1] == middleBoneName && bones[2] == endBoneName)
            {
                return chainName;
            }
        }
        return string.Empty;
    }
    private string GetIKChainName(int ikChainIndex)
    {
        return GetNameByIndex(LoadIKChainNamesFromModel(), ikChainIndex);
    }
    private string GetFootName(int footIndex)
    {
        return GetNameByIndex(LoadFootNamesFromModel(), footIndex);
    }

    private void AddFeetProperty(KVObject target, KVObject compiledSource)
    {
        if (!compiledSource.ContainsKey("m_footIndices"))
        {
            return;
        }

        var footIndices = compiledSource.GetIntegerArray("m_footIndices");
        var feetArray = KVObject.Array();
        foreach (var footIndex in footIndices)
        {
            var footName = GetFootName((int)footIndex);
            feetArray.Add(footName);
        }
        target.Add("m_feet", feetArray);
    }

    private string[] LoadFootNamesFromModel()
    {
        if (modelFootNamesCache is not null)
        {
            return modelFootNamesCache;
        }
        var footNames = new List<string>();
        try
        {
            var keyvalues = ModelData?.KeyValues;
            if (keyvalues?.ContainsKey("FeetSettings") == true)
            {
                foreach (var (footKey, _) in keyvalues.GetSubCollection("FeetSettings").Children)
                {
                    if (!string.IsNullOrEmpty(footKey) && footKey != "_class")
                    {
                        footNames.Add(footKey);
                    }
                }
            }
        }
        catch (Exception)
        {
            footNames.Clear();
        }
        modelFootNamesCache = [.. footNames];
        return modelFootNamesCache;
    }

    private sealed class LookAtChainInfo
    {
        public string Name { get; set; } = string.Empty;
        public string[] BoneNames { get; set; } = [];
        public float[] BoneWeights { get; set; } = [];
    }

    private string[] GetBoneNamesFromIndices(KVObject compiledBones)
    {
        if (compiledBones is null || !compiledBones.ContainsKey("m_bones"))
        {
            return [];
        }
        var compiledBonesArray = compiledBones.GetArray("m_bones");
        var boneNames = new string[compiledBonesArray.Count];

        for (var i = 0; i < compiledBonesArray.Count; i++)
        {
            var boneIndex = (int)compiledBonesArray[i].GetIntegerProperty("m_index");
            boneNames[i] = GetBoneName(boneIndex);
        }
        return boneNames;
    }
    private LookAtChainInfo[] LoadLookAtChainInfoFromModel()
    {
        if (modelLookAtChainInfoCache is not null)
        {
            return modelLookAtChainInfoCache;
        }
        var lookAtChains = new List<LookAtChainInfo>();
        try
        {
            var keyvalues = ModelData?.KeyValues;
            if (keyvalues?.ContainsKey("LookAtList") == true)
            {
                foreach (var (_, chainEntryValue) in keyvalues.GetSubCollection("LookAtList").Children)
                {
                    if (chainEntryValue.ValueType != KVValueType.Collection)
                    {
                        continue;
                    }
                    var chain = new LookAtChainInfo
                    {
                        Name = chainEntryValue.GetStringProperty("name"),
                    };
                    if (chainEntryValue.ContainsKey("bones"))
                    {
                        var bones = chainEntryValue.GetArray("bones");
                        chain.BoneNames = bones.Select(b => b.GetStringProperty("name")).ToArray();
                        chain.BoneWeights = bones.Select(b => b.GetFloatProperty("weight")).ToArray();
                    }
                    lookAtChains.Add(chain);
                }
            }
        }
        catch (Exception)
        {
            lookAtChains.Clear();
        }
        modelLookAtChainInfoCache = [.. lookAtChains];
        return modelLookAtChainInfoCache;
    }
    private string FindMatchingLookAtChainName(KVObject compiledBones)
    {
        if (compiledBones is null || !compiledBones.ContainsKey("m_bones"))
        {
            return string.Empty;
        }
        var lookAtChains = LoadLookAtChainInfoFromModel();
        if (lookAtChains.Length == 0)
        {
            return string.Empty;
        }
        var compiledBoneNames = GetBoneNamesFromIndices(compiledBones);

        foreach (var chain in lookAtChains)
        {
            if (chain.BoneNames.SequenceEqual(compiledBoneNames))
            {
                return chain.Name;
            }
        }

        // Fall back to unordered set equality
        var compiledSet = new HashSet<string>(compiledBoneNames);
        foreach (var chain in lookAtChains)
        {
            if (compiledSet.SetEquals(chain.BoneNames))
            {
                return chain.Name;
            }
        }
        return string.Empty;
    }
    /// <summary>
    /// Converts the compiled animation graph to editable version 19 format.
    /// </summary>
    /// <returns>The animation graph as a KV3 string in version 19 format.</returns>
    public string ToEditableAnimGraphVersion19()
    {
        modelAttachments = ModelData?.Attachments ?? [];

        var data = Graph.GetSubCollection("m_pSharedData");
        var compiledNodes = data.GetArray("m_nodes");
        BuildNodeIdMap(compiledNodes);

        var tagManager = data.GetSubCollection("m_pTagManagerUpdater");
        var paramListUpdater = data.GetSubCollection("m_pParamListUpdater");
        scriptManager = data.GetSubCollection("m_scriptManager");

        if (data.GetArray("m_managers") is { } managers)
        {
            tagManager = managers.FirstOrDefault(m => m.GetStringProperty("_class") == "CAnimTagManagerUpdater");
            paramListUpdater = managers.FirstOrDefault(m => m.GetStringProperty("_class") == "CAnimParameterListUpdater");
            scriptManager = managers.FirstOrDefault(m => m.GetStringProperty("_class") == "CAnimScriptManager");
        }

        if (tagManager is null || paramListUpdater is null)
        {
            throw new InvalidDataException("Missing tag manager or parameter list updater");
        }

        Tags = tagManager.GetArray("m_tags");
        Parameters = paramListUpdater.GetArray("m_parameters");

        var clipDataManager = tagManager.ContainsKey("sequence_tag_spans")
            ? ConvertClipDataManager(tagManager.GetArray("sequence_tag_spans"))
            : MakeNode("CAnimClipDataManager", ("m_itemTable", KVObject.Null()));

        var nodeManager = MakeListNode("CAnimNodeManager", "m_nodes");
        var componentList = new List<KVObject>();

        if (data.ContainsKey("m_components"))
        {
            var compiledComponents = data.GetArray("m_components");

            foreach (var compiledComponent in compiledComponents)
            {
                var componentData = ConvertComponent(compiledComponent);
                componentList.Add(componentData);
            }
        }

        var createdNodes = new Dictionary<long, KVObject>();
        var layoutNodes = new Dictionary<long, LayoutNode>();
        var nodeOutConnections = new Dictionary<long, List<long>>();

        for (var i = 0; i < compiledNodes.Count; i++)
        {
            var compiledNode = compiledNodes[i];

            if (nodeIndexToIdMap == null || !nodeIndexToIdMap.TryGetValue(i, out var nodeId))
            {
                continue;
            }

            var outConnections = new List<long>();
            var nodeData = ConvertToUncompiled(compiledNode, outConnections);
            nodeData.Add("m_nNodeID", MakeNodeIdObjectValue(nodeId));

            createdNodes[nodeId] = nodeData;
            layoutNodes[nodeId] = new LayoutNode(nodeId);
            nodeOutConnections[nodeId] = outConnections;
        }

        var connections = new List<LayoutConnection>();
        foreach (var (nodeId, outConns) in nodeOutConnections)
        {
            foreach (var targetId in outConns)
            {
                if (layoutNodes.TryGetValue(targetId, out var targetNode))
                {
                    connections.Add(new LayoutConnection(layoutNodes[nodeId], targetNode));
                }
            }
        }

        ApplyLayoutPositions(createdNodes, layoutNodes, connections);

        foreach (var (nodeId, nodeData) in createdNodes)
        {
            nodeManager.Children.Add(MakeNodeManagerEntry(nodeId, nodeData));
        }

        var localParameters = MakeArray(Parameters);
        var localTags = MakeArray(Tags);
        var componentManager = MakeNode("CAnimComponentManager");
        componentManager.Add("m_components", MakeArray(componentList));

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
                ("m_modelName", Graph.GetStringProperty("m_modelName")),
            ]);

        return kv.ToKV3String(format: KV3IDLookup.Get("animgraph19"));
    }

    private Dictionary<int, string> LoadWeightListNamesFromModel()
    {
        var weightListNames = new Dictionary<int, string>();
        try
        {
            var localBoneMaskArray = ModelResource is not null
                ? GetAseqDataFromResource(ModelResource)?.GetArray("m_localBoneMaskArray")
                : null;
            if (localBoneMaskArray is { Count: > 0 })
            {
                for (var i = 0; i < localBoneMaskArray.Count; i++)
                {
                    var weightListName = localBoneMaskArray[i].GetStringProperty("m_sName");
                    weightListNames[i] = !string.IsNullOrEmpty(weightListName)
                        ? weightListName
                        : i == 0 ? "default" : $"weightlist_{i}";
                }
            }
        }
        catch
        {
        }

        weightListNames.TryAdd(0, "default");
        return weightListNames;
    }

    private Dictionary<int, string> LoadSequenceNamesFromModel()
    {
        var sequenceNames = new Dictionary<int, string>();
        try
        {
            var modelResource = ModelResource;
            if (modelResource is null)
            {
                return sequenceNames;
            }

            var index = 0;
            var localSequenceNameArray = GetAseqDataFromResource(modelResource)?.GetArray<string>("m_localSequenceNameArray");
            if (localSequenceNameArray is not null)
            {
                foreach (var sequenceName in localSequenceNameArray)
                {
                    if (!string.IsNullOrEmpty(sequenceName))
                    {
                        sequenceNames[index++] = sequenceName;
                    }
                }
            }
            if (modelResource.DataBlock is Model modelData)
            {
                foreach (var animation in modelData.GetReferencedAnimations(fileLoader))
                {
                    if (!string.IsNullOrEmpty(animation.Name))
                    {
                        sequenceNames[index++] = animation.Name;
                    }
                }
            }
        }
        catch
        {
        }
        return sequenceNames;
    }

    private static KVObject? GetAseqDataFromResource(Resource modelResource)
    {
        if (!modelResource.ContainsBlockType(BlockType.ASEQ))
        {
            return null;
        }

        var aseqBlock = modelResource.GetBlockByType(BlockType.ASEQ);

        if (aseqBlock is not KeyValuesOrNTRO keyValuesOrNTRO)
        {
            return null;
        }

        var data = keyValuesOrNTRO.Data;

        if (data is not KVObject kvData)
        {
            return null;
        }

        if (kvData.ContainsKey("m_localBoneMaskArray") ||
            kvData.ContainsKey("m_localSequenceNameArray") ||
            kvData.GetStringProperty("m_sName")?.Contains("embedded_sequence_data", StringComparison.Ordinal) == true)
        {
            return kvData;
        }

        return kvData.ContainsKey("ASEQ")
            ? kvData.GetSubCollection("ASEQ")
            : null;
    }

    private KVObject ConvertClipDataManager(IReadOnlyList<KVObject> sequenceTagSpans)
    {
        var clipDataManager = MakeNode("CAnimClipDataManager");
        var itemTable = new KVObject();

        foreach (var sequenceSpan in sequenceTagSpans)
        {
            var sequenceName = sequenceSpan.GetStringProperty("m_sSequenceName");
            var compiledTagSpans = sequenceSpan.GetArray("m_tags");

            if (string.IsNullOrEmpty(sequenceName) || compiledTagSpans.Count == 0)
            {
                continue;
            }

            var clipData = MakeNode("CAnimClipData");
            clipData.Add("m_clipName", sequenceName);
            clipData.Add("m_tagSpans", ConvertTagSpansArray(compiledTagSpans));
            itemTable.Add(sequenceName, clipData);
        }

        clipDataManager.Add("m_itemTable", itemTable);
        return clipDataManager;
    }

    private static string ExtractCommonPrefix(string[] sequenceNames)
    {
        if (sequenceNames.Length != 8)
        {
            return string.Empty;
        }

        string[] directionalSuffixes =
        [
            "_n", "_nw", "_w", "_sw", "_s", "_se", "_e", "_ne",
            "_N", "_NW", "_W", "_SW", "_S", "_SE", "_E", "_NE",
        ];

        foreach (var seq in sequenceNames)
        {
            foreach (var suffix in directionalSuffixes)
            {
                if (!seq.EndsWith(suffix, StringComparison.Ordinal))
                {
                    continue;
                }

                var candidatePrefix = seq[..^suffix.Length];
                if (sequenceNames.All(s => s.StartsWith(candidatePrefix, StringComparison.Ordinal)
                    && directionalSuffixes.Contains(s[candidatePrefix.Length..])))
                {
                    return candidatePrefix;
                }
            }
        }
        return string.Empty;
    }
    private static KVObject MakeNodeIdObjectValue(long nodeId)
    {
        var nodeIdObject = new KVObject();
        nodeIdObject.Add("m_id", unchecked((uint)nodeId));
        return nodeIdObject;
    }

    private static KVObject MakeInputConnection(long nodeId)
    {
        var nodeIdObject = MakeNodeIdObjectValue(nodeId);
        var inputConnection = new KVObject();

        inputConnection.Add("m_nodeID", nodeIdObject);
        inputConnection.Add("m_outputID", nodeIdObject);

        return inputConnection;
    }

    private static KVObject MakeNodeManagerEntry(long nodeId, KVObject nodeData)
    {
        var entry = new KVObject();
        entry.Add("key", MakeNodeIdObjectValue(nodeId));
        entry.Add("value", nodeData);
        return entry;
    }

    private static void AddInputConnection(KVObject node, long childNodeId)
    {
        var inputConnection = MakeInputConnection(childNodeId);
        node.Add("m_inputConnection", inputConnection);
    }

    private static KVObject MakeVector2(float x, float y)
    {
        return MakeArray([(KVObject)x, (KVObject)y]);
    }

    private static KVObject MakeBlendCurve(KVObject? compiledCurve)
    {
        var blendCurve = MakeNode("CBlendCurve");
        blendCurve.Add("m_flControlPoint1", compiledCurve?.GetFloatProperty("m_flControlPoint1", 0.0f) ?? 0.0f);
        blendCurve.Add("m_flControlPoint2", compiledCurve?.GetFloatProperty("m_flControlPoint2", 1.0f) ?? 1.0f);
        return blendCurve;
    }

    private KVObject ConvertTagSpansArray(IReadOnlyList<KVObject> compiledTagSpans)
    {
        var tagSpans = new List<KVObject>();
        foreach (var compiledTagSpan in compiledTagSpans)
        {
            var tagIndex = compiledTagSpan.GetIntegerProperty("m_tagIndex");
            var startCycle = compiledTagSpan.GetFloatProperty("m_startCycle");
            var endCycle = compiledTagSpan.GetFloatProperty("m_endCycle");
            var duration = endCycle - startCycle;

            var tagSpan = MakeNode("CAnimTagSpan");
            tagSpan.Add("m_id", MakeNodeIdObjectValue(GetTagIdFromIndex(tagIndex)));
            tagSpan.Add("m_fStartCycle", startCycle);
            tagSpan.Add("m_fDuration", duration);
            tagSpans.Add(tagSpan);
        }
        return MakeArray(tagSpans);
    }

    private KVObject ExtractParameterID(KVObject paramHandle, bool requireFloat = false)
    {
        var paramType = paramHandle.GetStringProperty("m_type");
        var paramIndex = paramHandle.GetIntegerProperty("m_index");
        return ParameterIDFromIndex(paramType, paramIndex, requireFloat);
    }

    private KVObject ConvertParamSpans(KVObject? compiledParamSpans)
    {
        if (compiledParamSpans?.ContainsKey("m_spans") != true)
        {
            return KVObject.Array();
        }

        var paramSpans = new List<KVObject>();
        foreach (var compiledSpan in compiledParamSpans.GetArray("m_spans"))
        {
            var paramSpan = MakeNode("CAnimParamSpan");
            CopyIfPresent(compiledSpan, paramSpan, "m_samples");
            paramSpan.Add("m_id", compiledSpan.ContainsKey("m_hParam")
                ? ExtractParameterID(compiledSpan.GetSubCollection("m_hParam"))
                : MakeNodeIdObjectValue(-1));
            CopyIfPresent(compiledSpan, paramSpan, "m_flStartCycle");
            CopyIfPresent(compiledSpan, paramSpan, "m_flEndCycle");
            paramSpans.Add(paramSpan);
        }
        return MakeArray(paramSpans);
    }

    private long GetTagIdFromIndex(long tagIndex)
    {
        return tagIndex >= 0 && tagIndex < Tags.Count
            ? Tags[(int)tagIndex].GetSubCollection("m_tagID").GetIntegerProperty("m_id")
            : -1L;
    }

    private KVObject ConvertTagIndicesArray(long[] tagIndices)
    {
        var tagIds = tagIndices.Select(tagIndex =>
            MakeNodeIdObjectValue(GetTagIdFromIndex(tagIndex))
        ).ToArray();
        return MakeArray(tagIds);
    }

    private static KVObject MakeDefaultDamping()
    {
        var damping = MakeNode("CAnimInputDamping");
        damping.Add("m_speedFunction", "NoDamping");
        damping.Add("m_fSpeedScale", 1.0f);
        return damping;
    }

    private static void CopyIfPresent(KVObject source, KVObject target, string key, string? destKey = null)
    {
        if (source.ContainsKey(key))
        {
            target.Add(destKey ?? key, source[key]);
        }
    }

    private static void CopyBoolIfPresent(KVObject source, KVObject target, string key, string? destKey = null)
    {
        if (source.ContainsKey(key))
        {
            target.Add(destKey ?? key, source.GetIntegerProperty(key) > 0);
        }
    }

    private static void AddIfNotEmpty(KVObject target, string key, string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            target.Add(key, value);
        }
    }

    private string? TryGetScriptCode(long scriptIndex, string expectedScriptType)
    {
        if (scriptIndex < 0 || scriptManager is null)
        {
            return null;
        }
        var scriptInfoArray = scriptManager.GetArray("m_scriptInfo");
        if (scriptIndex >= scriptInfoArray.Count)
        {
            return null;
        }
        var scriptInfo = scriptInfoArray[(int)scriptIndex];
        if (scriptInfo.GetStringProperty("m_eScriptType") != expectedScriptType)
        {
            return null;
        }
        var scriptCode = scriptInfo.GetStringProperty("m_code");
        return string.IsNullOrEmpty(scriptCode) ? null : scriptCode;
    }

    private string? TryGetFuseGeneralScriptCode(long scriptIndex)
        => TryGetScriptCode(scriptIndex, "ANIMSCRIPT_FUSE_GENERAL");

    private bool TryAddInputConnectionFromRef(KVObject node, KVObject childRef, string? propertyName = null)
    {
        var nodeIndex = childRef.GetIntegerProperty("m_nodeIndex");
        if (nodeIndexToIdMap?.TryGetValue(nodeIndex, out var childNodeId) == true)
        {
            if (propertyName != null)
            {
                node.Add(propertyName, MakeInputConnection(childNodeId));
            }
            else
            {
                AddInputConnection(node, childNodeId);
            }
            return true;
        }
        return false;
    }

    private void AddFloatOrVectorParamPair(KVObject target, KVObject paramInHandle, KVObject paramOutHandle)
    {
        var isVector = paramInHandle.GetStringProperty("m_type") == "ANIMPARAM_VECTOR"
            || paramOutHandle.GetStringProperty("m_type") == "ANIMPARAM_VECTOR";
        target.Add("m_valueType", isVector ? "VectorParameter" : "FloatParameter");

        var noneId = MakeNodeIdObjectValue(-1);
        target.Add("m_floatParamIn", isVector ? noneId : ExtractParameterID(paramInHandle));
        target.Add("m_floatParamOut", isVector ? noneId : ExtractParameterID(paramOutHandle));
        target.Add("m_vectorParamIn", isVector ? ExtractParameterID(paramInHandle) : noneId);
        target.Add("m_vectorParamOut", isVector ? ExtractParameterID(paramOutHandle) : noneId);
    }

    private KVObject ConvertBlendDuration(KVObject compiledBlendDuration)
    {
        var paramIdValue = ExtractParameterID(compiledBlendDuration.GetSubCollection("m_hParam"), requireFloat: true);
        var isConstant = paramIdValue.GetIntegerProperty("m_id") == uint.MaxValue;

        var blendDuration = MakeNode("CFloatAnimValue");
        blendDuration.Add("m_flConstValue", compiledBlendDuration.GetFloatProperty("m_constValue"));
        blendDuration.Add("m_paramID", paramIdValue);
        blendDuration.Add("m_eSource", isConstant ? "Constant" : "Parameter");
        return blendDuration;
    }

    private string GetWeightListName(long weightListIndex)
    {
        weightListNamesCache ??= LoadWeightListNamesFromModel();

        return weightListNamesCache.TryGetValue((int)weightListIndex, out var name)
            ? name
            : weightListIndex == 0 ? "default" : $"weightlist_{weightListIndex}";
    }

    private string GetSequenceName(long sequenceIndex)
    {
        sequenceNamesCache ??= LoadSequenceNamesFromModel();
        return sequenceNamesCache.TryGetValue((int)sequenceIndex, out var name)
            ? name
            : $"sequence_{sequenceIndex}";
    }
    private KVObject[] ConvertStateMachine(KVObject compiledStateMachine, IReadOnlyList<KVObject>? stateDataArray, IReadOnlyList<KVObject>? transitionDataArray, bool isComponent = false)
    {
        var compiledStates = compiledStateMachine.GetArray("m_states");
        var compiledTransitions = compiledStateMachine.GetArray("m_transitions");
        var states = new KVObject[compiledStates.Count];

        var startStateIndex = -1;
        for (var i = 0; i < compiledStates.Count; i++)
        {
            if (compiledStates[i].GetIntegerProperty("m_bIsStartState") > 0)
            {
                startStateIndex = i;
                break;
            }
        }

        for (var i = 0; i < compiledStates.Count; i++)
        {
            var compiledState = compiledStates[i];
            var stateData = stateDataArray != null && i < stateDataArray.Count ? stateDataArray[i] : null;

            var stateNodeType = isComponent ? "CAnimComponentState" : "CAnimNodeState";
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
                var positionFromStart = i > startStateIndex ? i - startStateIndex : i + (compiledStates.Count - startStateIndex);
                stateX = 150.0f * positionFromStart + random.Next(-30, 31);
                stateY = 40.0f + random.Next(-10, 11);
            }

            stateNode.Add("m_position", MakeVector2(stateX, stateY));
            stateNode.Add("m_name", compiledState.GetStringProperty("m_name"));
            stateNode.Add("m_stateID", compiledState.GetSubCollection("m_stateID"));
            stateNode.Add("m_bIsStartState", compiledState.GetIntegerProperty("m_bIsStartState") > 0);
            stateNode.Add("m_bIsEndtState", compiledState.GetIntegerProperty("m_bIsEndState") > 0);
            stateNode.Add("m_bIsPassthrough", compiledState.GetIntegerProperty("m_bIsPassthrough") > 0);

            if (compiledState.ContainsKey("m_transitionIndices"))
            {
                var transitionIndices = compiledState.GetIntegerArray("m_transitionIndices");
                var transitions = new List<KVObject>();

                for (var transitionIndex = 0; transitionIndex < transitionIndices.Length; transitionIndex++)
                {
                    var globalTransitionIndex = transitionIndices[transitionIndex];
                    if (globalTransitionIndex < 0 || globalTransitionIndex >= compiledTransitions.Count)
                    {
                        continue;
                    }

                    var compiledTransition = compiledTransitions[(int)globalTransitionIndex];
                    var transitionData = transitionDataArray != null && globalTransitionIndex < transitionDataArray.Count
                        ? transitionDataArray[(int)globalTransitionIndex]
                        : null;

                    var transitionNodeType = isComponent ? "CAnimComponentStateTransition" : "CAnimNodeStateTransition";
                    var transitionNode = MakeNode(transitionNodeType);
                    var srcStateIndex = compiledTransition.GetIntegerProperty("m_srcStateIndex");
                    var destStateIndex = compiledTransition.GetIntegerProperty("m_destStateIndex");
                    var srcStateID = compiledStates[(int)srcStateIndex].GetSubCollection("m_stateID");
                    var destStateID = compiledStates[(int)destStateIndex].GetSubCollection("m_stateID");

                    transitionNode.Add("m_srcState", srcStateID);
                    transitionNode.Add("m_destState", destStateID);
                    transitionNode.Add("m_bDisabled", compiledTransition.GetIntegerProperty("m_bDisabled") > 0);

                    var conditionList = MakeNode("CConditionContainer");
                    var conditions = new List<KVObject>();

                    if (compiledState.ContainsKey("m_hScript"))
                    {
                        var scriptIndex = compiledState.GetSubCollection("m_hScript").GetIntegerProperty("m_id");
                        var scriptConditions = CreateConditionsFromScript(scriptIndex, transitionIndex);
                        if (scriptConditions != null)
                        {
                            conditions.AddRange(scriptConditions);
                        }
                    }

                    conditionList.Add("m_conditions", MakeArray(conditions));
                    transitionNode.Add("m_conditionList", conditionList);

                    if (!isComponent)
                    {
                        var nodeIndex = compiledTransition.GetIntegerProperty("m_nodeIndex");
                        if (nodeIndexToIdMap?.TryGetValue(nodeIndex, out var childNodeId) == true)
                        {
                            AddInputConnection(transitionNode, childNodeId);
                        }

                        if (transitionData is not null)
                        {
                            transitionNode.Add("m_bReset", transitionData.GetIntegerProperty("m_bReset") > 0);

                            if (transitionData.ContainsKey("m_resetCycleOption"))
                            {
                                transitionNode.Add("m_resetCycleOption", transitionData.GetIntegerProperty("m_resetCycleOption") switch
                                {
                                    1 => "SameCycleAsSource",
                                    2 => "InverseSourceCycle",
                                    3 => "FixedValue",
                                    4 => "SameTimeAsSource",
                                    _ => "Beginning",
                                });
                            }

                            if (transitionData.ContainsKey("m_blendDuration"))
                            {
                                transitionNode.Add("m_blendDuration",
                                    ConvertBlendDuration(transitionData.GetSubCollection("m_blendDuration")));
                            }

                            if (transitionData.ContainsKey("m_resetCycleValue"))
                            {
                                transitionNode.Add("m_flFixedCycleValue",
                                    ConvertBlendDuration(transitionData.GetSubCollection("m_resetCycleValue")));
                            }

                            transitionNode.Add("m_blendCurve",
                                MakeBlendCurve(transitionData.ContainsKey("m_curve") ? transitionData.GetSubCollection("m_curve") : null));
                        }
                    }

                    transitions.Add(transitionNode);
                }

                stateNode.Add("m_transitions", MakeArray(transitions));
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
                        var actionClassName = compiledActionData.GetStringProperty("_class");
                        var newActionClassName = actionClassName.Replace("Updater", string.Empty, StringComparison.Ordinal);
                        var actionData = MakeNode(newActionClassName);

                        if (compiledActionData.ContainsKey("m_hScript"))
                        {
                            var scriptCode = TryGetFuseGeneralScriptCode(compiledActionData.GetSubCollection("m_hScript").GetIntegerProperty("m_id"));
                            if (scriptCode != null)
                            {
                                actionData.Add("m_expression", scriptCode);
                            }
                        }

                        if (compiledActionData.ContainsKey("m_nTagIndex"))
                        {
                            actionData.Add("m_tag", MakeNodeIdObjectValue(GetTagIdFromIndex(compiledActionData.GetIntegerProperty("m_nTagIndex"))));
                        }

                        if (compiledActionData.ContainsKey("m_hParam"))
                        {
                            actionData.Add("m_param", ExtractParameterID(compiledActionData.GetSubCollection("m_hParam")));
                        }

                        CopyIfPresent(compiledActionData, actionData, "m_value");

                        foreach (var (childKey, childValue) in compiledActionData.Children)
                        {
                            if (childKey is "_class" or "m_nTagIndex" or "m_hParam" or "m_value" or "m_hScript" or "m_eScriptType" or "m_code")
                            {
                                continue;
                            }

                            actionData.Add(childKey, childValue);
                        }

                        action.Add("m_pAction", actionData);
                    }

                    if (compiledAction.ContainsKey("m_eBehavior"))
                    {
                        action.Add("m_eBehavior", compiledAction.GetStringProperty("m_eBehavior"));
                    }

                    actions.Add(action);
                }

                stateNode.Add("m_actions", MakeArray(actions));
            }

            if (!isComponent && stateData is not null)
            {
                TryAddInputConnectionFromRef(stateNode, stateData.GetSubCollection("m_pChild"));
                stateNode.Add("m_bIsRootMotionExclusive", stateData.GetIntegerProperty("m_bExclusiveRootMotion") > 0);
            }

            states[i] = stateNode;
        }

        return states;
    }


    private KVObject[]? CreateConditionsFromScript(long scriptIndex, int transitionIndex)
    {
        var scriptCode = TryGetScriptCode(scriptIndex, "ANIMSCRIPT_FUSE_STATEMACHINE");
        if (scriptCode is null)
        {
            return null;
        }

        var conditions = ParseConditionScript(scriptCode, transitionIndex);
        return conditions is { Count: > 0 } ? [.. conditions] : null;
    }

    private List<KVObject>? ParseConditionScript(string scriptCode, int targetTransitionIndex)
    {
        var conditions = new List<KVObject>();

        var trimmedScript = scriptCode.Trim();
        var conditionString = ExtractConditionFromTernary(trimmedScript, targetTransitionIndex);

        if (string.IsNullOrEmpty(conditionString))
        {
            return null;
        }

        var processedCondition = RemoveAllOuterParentheses(conditionString);
        return ParseConditionExpression(processedCondition);
    }

    private static string RemoveAllOuterParentheses(string condition)
    {
        var result = condition.Trim();

        while (result.StartsWith('(') && result.EndsWith(')'))
        {
            var parenCount = 0;
            var shouldRemove = true;

            for (var i = 0; i < result.Length; i++)
            {
                if (result[i] == '(')
                {
                    parenCount++;
                }
                else if (result[i] == ')')
                {
                    parenCount--;
                    if (parenCount == 0 && i < result.Length - 1)
                    {
                        shouldRemove = false;
                        break;
                    }
                }
            }

            if (shouldRemove && parenCount == 0)
            {
                result = result[1..^1].Trim();
            }
            else
            {
                break;
            }
        }

        return result;
    }

    private KVObject? ParseMultiComponentCondition(string conditionString)
    {
        var trimmedCondition = conditionString.Trim();
        trimmedCondition = RemoveAllOuterParentheses(trimmedCondition);

        var componentConditions = SplitByOperator(trimmedCondition, "&&");

        if (componentConditions.Count < 2)
        {
            return null;
        }

        var firstComponent = componentConditions[0].Trim();
        firstComponent = RemoveAllOuterParentheses(firstComponent);

        var dotIndex = firstComponent.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex == -1)
        {
            return null;
        }

        var paramName = firstComponent[..dotIndex].Trim();
        var (foundParam, paramId, paramClass) = FindParameterByName(paramName);

        if (foundParam == null)
        {
            return null;
        }

        string[] expectedComponents;
        int componentType;

        switch (paramClass)
        {
            case "CVectorAnimParameter":
                expectedComponents = ["x", "y", "z"];
                componentType = 5;
                break;

            case "CQuaternionAnimParameter":
                expectedComponents = ["x", "y", "z", "w"];
                componentType = 6;
                break;

            default:
                return null;
        }

        if (componentConditions.Count != expectedComponents.Length)
        {
            return null;
        }

        var componentValues = new float[expectedComponents.Length];
        var comparisonOp = "COMPARISON_EQUALS";
        var foundComponents = new HashSet<string>();

        foreach (var componentCondition in componentConditions)
        {
            var trimmedComponent = componentCondition.Trim();
            trimmedComponent = RemoveAllOuterParentheses(trimmedComponent);

            var (foundOp, leftSide, rightSide) = ParseOperatorExpression(trimmedComponent);

            if (foundOp == null || leftSide == null || rightSide == null)
            {
                continue;
            }

            comparisonOp = OperatorToComparisonOp(foundOp);

            var componentDotIndex = leftSide.IndexOf('.', StringComparison.Ordinal);
            if (componentDotIndex == -1)
            {
                continue;
            }

            var component = leftSide[(componentDotIndex + 1)..].Trim().ToLowerInvariant();
            if (!expectedComponents.Contains(component))
            {
                continue;
            }

            if (foundComponents.Contains(component))
            {
                continue;
            }

            foundComponents.Add(component);
            var cleanRightSide = rightSide.Trim('(', ')', ' ');
            if (!float.TryParse(cleanRightSide, CultureInfo.InvariantCulture, out var floatValue))
            {
                continue;
            }

            var componentIndex = Array.IndexOf(expectedComponents, component);
            if (componentIndex >= 0 && componentIndex < componentValues.Length)
            {
                componentValues[componentIndex] = floatValue;
            }
        }

        if (foundComponents.Count != expectedComponents.Length)
        {
            return null;
        }

        var paramCondition = MakeNode("CParameterCondition");
        paramCondition.Add("m_paramID", MakeNodeIdObjectValue(paramId));
        paramCondition.Add("m_comparisonOp", comparisonOp);
        paramCondition.Add("m_comparisonString", "");

        var comparisonValue = new KVObject();
        comparisonValue.Add("m_nType", componentType);

        var componentArray = KVObject.Array();
        for (var i = 0; i < componentValues.Length; i++)
        {
            componentArray.Add((float)componentValues[i]);
        }

        comparisonValue.Add("m_data", componentArray);
        paramCondition.Add("m_comparisonValue", comparisonValue);

        return paramCondition;
    }

    private List<KVObject>? ParseConditionExpression(string conditionExpression)
    {
        var conditions = new List<KVObject>();

        var multiComponentCondition = ParseMultiComponentCondition(conditionExpression);
        if (multiComponentCondition != null)
        {
            conditions.Add(multiComponentCondition);
            return conditions.Count > 0 ? conditions : null;
        }

        var andParts = SplitByOperator(conditionExpression, "&&");

        if (andParts.Count > 1)
        {
            foreach (var andPart in andParts)
            {
                var trimmedPart = andPart.Trim();
                var orParts = SplitByOperator(trimmedPart, "||");

                if (orParts.Count > 1)
                {
                    var orCondition = CreateOrCondition(orParts);
                    if (orCondition != null)
                    {
                        conditions.Add(orCondition);
                    }
                }
                else
                {
                    var condition = ParseAtomicCondition(trimmedPart);
                    if (condition != null)
                    {
                        conditions.Add(condition);
                    }
                }
            }
        }
        else
        {
            var trimmedExpression = conditionExpression.Trim();
            var orParts = SplitByOperator(trimmedExpression, "||");

            if (orParts.Count > 1)
            {
                var orCondition = CreateOrCondition(orParts);
                if (orCondition != null)
                {
                    conditions.Add(orCondition);
                }
            }
            else
            {
                var condition = ParseAtomicCondition(trimmedExpression);
                if (condition != null)
                {
                    conditions.Add(condition);
                }
            }
        }

        return conditions.Count > 0 ? conditions : null;
    }

    private static List<string> SplitByOperator(string expression, string op)
    {
        var result = new List<string>();
        var currentPart = new StringBuilder();
        var parenCount = 0;

        for (var i = 0; i < expression.Length; i++)
        {
            var c = expression[i];

            if (c == '(')
            {
                parenCount++;
            }
            else if (c == ')')
            {
                parenCount--;
            }

            if (parenCount == 0 && i + op.Length <= expression.Length)
            {
                var potentialOp = expression.Substring(i, op.Length);
                if (potentialOp == op)
                {
                    var trimmedPart = currentPart.ToString().Trim();
                    if (!string.IsNullOrEmpty(trimmedPart))
                    {
                        var cleanedPart = RemoveAllOuterParentheses(trimmedPart);
                        result.Add(cleanedPart);
                    }
                    currentPart.Clear();
                    i += op.Length - 1;
                    continue;
                }
            }

            currentPart.Append(c);
        }

        var finalPart = currentPart.ToString().Trim();
        if (!string.IsNullOrEmpty(finalPart))
        {
            var cleanedPart = RemoveAllOuterParentheses(finalPart);
            result.Add(cleanedPart);
        }

        return result;
    }

    private KVObject? CreateOrCondition(List<string> orParts)
    {
        if (orParts.Count < 2)
        {
            return null;
        }

        var orCondition = MakeNode("COrCondition");
        var subConditions = new List<KVObject>();

        foreach (var orPart in orParts)
        {
            var trimmedPart = orPart.Trim();
            var condition = ParseAtomicCondition(trimmedPart);
            if (condition != null)
            {
                subConditions.Add(condition);
            }
        }

        if (subConditions.Count == 0)
        {
            return null;
        }

        orCondition.Add("m_conditions", MakeArray(subConditions));
        return orCondition;
    }

    private KVObject? ParseAtomicCondition(string conditionString)
    {
        var trimmedCondition = conditionString.Trim();
        trimmedCondition = RemoveAllOuterParentheses(trimmedCondition);

        var multiComponentCondition = ParseMultiComponentCondition(trimmedCondition);
        if (multiComponentCondition != null)
        {
            return multiComponentCondition;
        }

        if (trimmedCondition.StartsWith("GetStateWeight(", StringComparison.Ordinal) || trimmedCondition.StartsWith("GetTotalTranslation_", StringComparison.Ordinal))
        {
            return ParseStateStatusCondition(trimmedCondition);
        }

        if (trimmedCondition.StartsWith("IsTagActive(", StringComparison.Ordinal) || trimmedCondition.StartsWith("!IsTagActive(", StringComparison.Ordinal))
        {
            return ParseTagCondition(trimmedCondition);
        }

        if (trimmedCondition.Contains("GetTimeTillFinished()", StringComparison.Ordinal))
        {
            return ParseFinishedCondition(trimmedCondition);
        }

        if (trimmedCondition.Contains("GetTimeInState()", StringComparison.Ordinal))
        {
            return ParseTimeCondition(trimmedCondition);
        }

        if (trimmedCondition.Contains("GetCycle()", StringComparison.Ordinal))
        {
            return ParseCycleCondition(trimmedCondition);
        }

        return ParseParameterCondition(trimmedCondition);
    }

    private KVObject? ParseStateStatusCondition(string conditionString)
    {
        var stateStatusCondition = MakeNode("CStateStatusCondition");

        var (comparisonOperator, leftSide, rightSide) = ParseOperatorExpression(conditionString);

        if (comparisonOperator == null || leftSide == null)
        {
            return null;
        }

        stateStatusCondition.Add("m_comparisonOp", OperatorToComparisonOp(comparisonOperator));

        var sourceValue = leftSide switch
        {
            var s when s.StartsWith("GetStateWeight(0)", StringComparison.Ordinal) => "SourceStateBlendWeight",
            var s when s.StartsWith("GetStateWeight(1)", StringComparison.Ordinal) => "TargetStateBlendWeight",
            var s when s.StartsWith("GetTotalTranslation_SourceState()", StringComparison.Ordinal) => "TotalTranslation_SourceState",
            var s when s.StartsWith("GetTotalTranslation_TargetState()", StringComparison.Ordinal) => "TotalTranslation_TargetState",
            _ => "SourceStateBlendWeight"
        };

        stateStatusCondition.Add("m_sourceValue", sourceValue);

        if (rightSide != null)
        {
            if (rightSide.StartsWith("GetStateWeight(", StringComparison.Ordinal) || rightSide.StartsWith("GetTotalTranslation_", StringComparison.Ordinal))
            {
                var comparisonStateValue = rightSide switch
                {
                    var s when s.StartsWith("GetStateWeight(0)", StringComparison.Ordinal) => "SourceStateBlendWeight",
                    var s when s.StartsWith("GetStateWeight(1)", StringComparison.Ordinal) => "TargetStateBlendWeight",
                    var s when s.StartsWith("GetTotalTranslation_SourceState()", StringComparison.Ordinal) => "TotalTranslation_SourceState",
                    var s when s.StartsWith("GetTotalTranslation_TargetState()", StringComparison.Ordinal) => "TotalTranslation_TargetState",
                    _ => "SourceStateBlendWeight"
                };

                stateStatusCondition.Add("m_comparisonValueType", "StateComparisonValue_StateValue");
                stateStatusCondition.Add("m_comparisonStateValue", comparisonStateValue);
                stateStatusCondition.Add("m_comparisonParamID", MakeNodeIdObjectValue(uint.MaxValue));
                stateStatusCondition.Add("m_comparisonFixedValue", 0.0f);
            }
            else if (rightSide.All(c => char.IsLetterOrDigit(c) || c == '_') && !rightSide.Contains('.', StringComparison.Ordinal))
            {
                return HandleParameterComparison(stateStatusCondition, rightSide, sourceValue);
            }
            else if (float.TryParse(rightSide, CultureInfo.InvariantCulture, out var floatValue))
            {
                stateStatusCondition.Add("m_comparisonValueType", "StateComparisonValue_FixedValue");
                stateStatusCondition.Add("m_comparisonFixedValue", floatValue);
                stateStatusCondition.Add("m_comparisonStateValue", sourceValue);
                stateStatusCondition.Add("m_comparisonParamID", MakeNodeIdObjectValue(uint.MaxValue));
            }
            else
            {
                return null;
            }
        }

        return stateStatusCondition;
    }

    private KVObject? ParseTagCondition(string conditionString)
    {
        var isTagActive = true;
        string tagName;

        if (conditionString.StartsWith("!IsTagActive(", StringComparison.Ordinal))
        {
            isTagActive = false;
            tagName = conditionString["!IsTagActive(".Length..^1].Trim();
        }
        else if (conditionString.StartsWith("IsTagActive(", StringComparison.Ordinal))
        {
            tagName = conditionString["IsTagActive(".Length..^1].Trim();
        }
        else
        {
            return null;
        }

        var tagCondition = MakeNode("CTagCondition");
        var tagId = FindTagIdByName(tagName);
        tagCondition.Add("m_tagID", MakeNodeIdObjectValue(tagId));
        tagCondition.Add("m_comparisonValue", isTagActive);
        return tagCondition;
    }

    private static readonly string[] ComparisonOperators = ["==", "!=", ">=", "<=", ">", "<"];

    private static string OperatorToComparisonOp(string op) => op switch
    {
        "==" => "COMPARISON_EQUALS",
        "!=" => "COMPARISON_NOT_EQUALS",
        ">" => "COMPARISON_GREATER",
        ">=" => "COMPARISON_GREATER_OR_EQUAL",
        "<" => "COMPARISON_LESS",
        "<=" => "COMPARISON_LESS_OR_EQUAL",
        _ => "COMPARISON_EQUALS"
    };

    private static (string? op, string? leftSide, string? rightSide) ParseOperatorExpression(string expression, string[]? operators = null)
    {
        operators ??= ComparisonOperators;
        foreach (var op in operators)
        {
            if (expression.Contains(op, StringComparison.Ordinal))
            {
                var parts = expression.Split(op, StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    return (op, parts[0], parts[1].Trim('(', ')', ' '));
                }
            }
        }
        return (null, null, null);
    }

    private static KVObject? ParseFinishedCondition(string conditionString)
    {
        var timeCondition = MakeNode("CFinishedCondition");

        var (op, _, rightSide) = ParseOperatorExpression(conditionString);

        if (op != null && rightSide != null && float.TryParse(rightSide, CultureInfo.InvariantCulture, out var value))
        {
            timeCondition.Add("m_option",
                op == "<=" && value != 0.0f || op == "==" && value != 0.0f
                    ? "FinishedConditionOption_OnAlmostFinished"
                    : "FinishedConditionOption_OnFinished");
            timeCondition.Add("m_bIsFinished", op != ">=" || value <= 0.0f);
        }
        else
        {
            timeCondition.Add("m_option", "FinishedConditionOption_OnFinished");
            timeCondition.Add("m_bIsFinished", true);
        }
        return timeCondition;
    }

    private static KVObject? ParseTimeCondition(string conditionString)
    {
        var (foundOp, _, rightSide) = ParseOperatorExpression(conditionString);

        if (foundOp != null && rightSide != null)
        {
            var timeCondition = MakeNode("CTimeCondition");
            timeCondition.Add("m_comparisonOp", OperatorToComparisonOp(foundOp));
            timeCondition.Add("m_comparisonString", rightSide);
            return timeCondition;
        }
        return null;
    }

    private static KVObject? ParseCycleCondition(string conditionString)
    {
        var (foundOp, _, rightSide) = ParseOperatorExpression(conditionString);

        if (foundOp != null && rightSide != null)
        {
            var cycleCondition = MakeNode("CCycleCondition");
            cycleCondition.Add("m_comparisonOp", OperatorToComparisonOp(foundOp));
            cycleCondition.Add("m_comparisonString", rightSide);

            if (float.TryParse(rightSide, CultureInfo.InvariantCulture, out var floatValue))
            {
                cycleCondition.Add("m_comparisonValue", floatValue);
            }
            else
            {
                cycleCondition.Add("m_comparisonValue", 0.0f);
            }

            cycleCondition.Add("m_comparisonValueType", "COMPARISONVALUETYPE_FIXEDVALUE");
            cycleCondition.Add("m_comparisonParamID", MakeNodeIdObjectValue(uint.MaxValue));
            return cycleCondition;
        }
        return null;
    }

    private KVObject? ParseParameterCondition(string conditionString)
    {
        var cleanedCondition = conditionString.Replace(" (", " ", StringComparison.Ordinal).Replace("( ", " ", StringComparison.Ordinal).Trim('(', ')').Trim();

        if (cleanedCondition.Contains('.', StringComparison.Ordinal) && (cleanedCondition.Contains(".x", StringComparison.Ordinal)
            || cleanedCondition.Contains(".y", StringComparison.Ordinal) || cleanedCondition.Contains(".z", StringComparison.Ordinal) || cleanedCondition.Contains(".w", StringComparison.Ordinal)))
        {
            return null;
        }

        var (foundOp, leftSide, rightSide) = ParseOperatorExpression(cleanedCondition);

        if (foundOp == null || leftSide == null || rightSide == null)
        {
            return null;
        }

        var (foundParam, paramId, paramClass) = FindParameterByName(leftSide);

        if (foundParam == null || paramClass == null)
        {
            return null;
        }

        var paramCondition = MakeNode("CParameterCondition");
        paramCondition.Add("m_paramID", MakeNodeIdObjectValue(paramId));
        paramCondition.Add("m_comparisonOp", OperatorToComparisonOp(foundOp));

        var comparisonValue = new KVObject();

        if (paramClass == "CEnumAnimParameter")
        {
            comparisonValue.Add("m_nType", 2);

            if (int.TryParse(rightSide, out var intValue))
            {
                comparisonValue.Add("m_data", intValue);

                if (foundParam.ContainsKey("m_enumOptions"))
                {
                    var enumOptions = foundParam.GetArray<string>("m_enumOptions");
                    if (intValue >= 0 && intValue < enumOptions.Length)
                    {
                        paramCondition.Add("m_comparisonString", enumOptions[intValue]);
                    }
                }
            }
        }
        else if (paramClass == "CFloatAnimParameter")
        {
            comparisonValue.Add("m_nType", 4);

            if (float.TryParse(rightSide, CultureInfo.InvariantCulture, out var floatValue))
            {
                comparisonValue.Add("m_data", floatValue);
            }
        }
        else if (paramClass == "CIntAnimParameter")
        {
            comparisonValue.Add("m_nType", 3);

            if (int.TryParse(rightSide, out var intValue))
            {
                comparisonValue.Add("m_data", intValue);
            }
        }
        else if (paramClass == "CBoolAnimParameter")
        {
            comparisonValue.Add("m_nType", 1);

            var boolValue = rightSide == "1" || rightSide.Equals("true", StringComparison.OrdinalIgnoreCase);
            comparisonValue.Add("m_data", boolValue);
            paramCondition.Add("m_comparisonString", boolValue ? "True" : "False");
        }
        else
        {
            comparisonValue.Add("m_nType", 4);

            if (float.TryParse(rightSide, CultureInfo.InvariantCulture, out var floatValue))
            {
                comparisonValue.Add("m_data", floatValue);
            }
        }

        paramCondition.Add("m_comparisonValue", comparisonValue);
        return paramCondition;
    }



    private KVObject HandleParameterComparison(KVObject stateStatusCondition, string paramName, string sourceValue)
    {
        var (foundParam, paramId, _) = FindParameterByName(paramName);

        if (foundParam != null)
        {
            stateStatusCondition.Add("m_comparisonValueType", "StateComparisonValue_Parameter");
            stateStatusCondition.Add("m_comparisonParamID", MakeNodeIdObjectValue(paramId));
            stateStatusCondition.Add("m_comparisonStateValue", sourceValue);
            stateStatusCondition.Add("m_comparisonFixedValue", 0.0f);
        }
        else
        {
            if (float.TryParse(paramName, CultureInfo.InvariantCulture, out var floatValue))
            {
                stateStatusCondition.Add("m_comparisonValueType", "StateComparisonValue_FixedValue");
                stateStatusCondition.Add("m_comparisonFixedValue", floatValue);
                stateStatusCondition.Add("m_comparisonStateValue", sourceValue);
                stateStatusCondition.Add("m_comparisonParamID", MakeNodeIdObjectValue(uint.MaxValue));
            }
        }

        return stateStatusCondition;
    }

    private (KVObject? param, long paramId, string? paramClass) FindParameterByName(string paramName)
    {
        string[] namesToTry = [paramName, paramName.Replace('_', ' ')];

        foreach (var comparison in new[] { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase })
        {
            foreach (var nameToTry in namesToTry)
            {
                foreach (var param in Parameters)
                {
                    if (string.Equals(param.GetStringProperty("m_name"), nameToTry, comparison))
                    {
                        return (param, GetParameterId(param), param.GetStringProperty("_class"));
                    }
                }
            }
        }

        // Recurse after stripping a component suffix like "param.x" -> "param"
        var dotIndex = paramName.IndexOf('.', StringComparison.Ordinal);
        return dotIndex != -1 ? FindParameterByName(paramName[..dotIndex]) : (null, -1, null);
    }

    private long FindTagIdByName(string tagName)
    {
        static long TagIdOf(KVObject tag) => tag.GetSubCollection("m_tagID").GetIntegerProperty("m_id");

        var strippedName = tagName.StartsWith("TAG_", StringComparison.Ordinal)
            ? tagName["TAG_".Length..]
            : tagName;

        var namesToTry = new[]
        {
            tagName,
            strippedName,
            tagName.Replace('_', ' '),
            strippedName.Replace('_', ' '),
            tagName.Replace(' ', '_'),
        }.Distinct().ToArray();

        foreach (var comparison in new[] { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase })
        {
            foreach (var nameVariant in namesToTry)
            {
                foreach (var tag in Tags)
                {
                    if (string.Equals(tag.GetStringProperty("m_name"), nameVariant, comparison))
                    {
                        return TagIdOf(tag);
                    }
                }
            }
        }

        // Fuzzy alphanumeric-only match as last resort
        var cleanInput = new string([.. strippedName.Where(char.IsLetterOrDigit)]).ToLowerInvariant();
        foreach (var tag in Tags)
        {
            var cleanCurrent = new string([.. tag.GetStringProperty("m_name").Where(char.IsLetterOrDigit)]).ToLowerInvariant();
            if (cleanCurrent == cleanInput
                || cleanCurrent.Contains(cleanInput, StringComparison.Ordinal)
                || cleanInput.Contains(cleanCurrent, StringComparison.Ordinal))
            {
                return TagIdOf(tag);
            }
        }

        return -1;
    }

    private static string ExtractConditionFromTernary(string script, int index)
    {
        var currentScript = script.Trim();
        var currentIndex = 0;

        while (currentIndex <= index)
        {
            var questionPos = FindMatchingQuestionMark(currentScript, 0);
            if (questionPos == -1)
            {
                return string.Empty;
            }

            if (currentIndex == index)
            {
                var condition = currentScript[..questionPos].Trim();
                return condition;
            }

            var colonPos = FindMatchingColon(currentScript, questionPos);
            if (colonPos == -1)
            {
                return string.Empty;
            }

            var afterColon = currentScript[(colonPos + 1)..].Trim();
            if (afterColon.StartsWith("(-1)", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            currentScript = afterColon;

            if (currentScript.StartsWith('('))
            {
                var matchingParen = FindMatchingParenthesis(currentScript, 0);
                if (matchingParen == currentScript.Length - 1)
                {
                    currentScript = currentScript[1..^1].Trim();
                }
            }

            currentIndex++;
        }

        return string.Empty;
    }

    private static int FindMatchingQuestionMark(string script, int startPos)
    {
        var parenCount = 0;
        for (var i = startPos; i < script.Length; i++)
        {
            var c = script[i];
            if (c == '(')
            {
                parenCount++;
            }
            else if (c == ')')
            {
                parenCount--;
            }
            else if (c == '?' && parenCount == 0)
            {
                return i;
            }
        }
        return -1;
    }

    private static int FindMatchingColon(string script, int questionPos)
    {
        var parenCount = 0;
        for (var i = questionPos + 1; i < script.Length; i++)
        {
            var c = script[i];
            if (c == '(')
            {
                parenCount++;
            }
            else if (c == ')')
            {
                parenCount--;
            }
            else if (c == ':' && parenCount == 0)
            {
                return i;
            }
        }
        return -1;
    }

    private static int FindMatchingParenthesis(string script, int startPos)
    {
        if (script[startPos] != '(')
        {
            return -1;
        }

        var parenCount = 1;
        for (var i = startPos + 1; i < script.Length; i++)
        {
            var c = script[i];
            if (c == '(')
            {
                parenCount++;
            }
            else if (c == ')')
            {
                parenCount--;
                if (parenCount == 0)
                {
                    return i;
                }
            }
        }
        return -1;
    }

    private KVObject ConvertComponent(KVObject compiledComponent)
    {
        var className = compiledComponent.GetStringProperty("_class");
        var newClassName = className.Replace("Updater", string.Empty, StringComparison.Ordinal);
        var component = MakeNode(newClassName);

        component.Add("m_group", "");
        component.Add("m_id", compiledComponent.GetSubCollection("m_id"));
        component.Add("m_bStartEnabled", compiledComponent.GetIntegerProperty("m_bStartEnabled") > 0);
        component.Add("m_nPriority", 100);

        if (compiledComponent.ContainsKey("m_networkMode"))
        {
            component.Add("m_networkMode", compiledComponent.GetStringProperty("m_networkMode"));
        }

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

                    foreach (var (actionChildKey, actionChildValue) in action.Children)
                    {
                        if (actionChildKey == "_class")
                        {
                            continue;
                        }

                        if (actionChildKey == "m_nTagIndex")
                        {
                            var tagIndex = action.GetIntegerProperty("m_nTagIndex");
                            newAction.Add("m_tag", MakeNodeIdObjectValue(GetTagIdFromIndex(tagIndex)));
                            continue;
                        }

                        if (actionChildKey == "m_hParam")
                        {
                            newAction.Add("m_param", ExtractParameterID(actionChildValue));
                            continue;
                        }

                        if (actionChildKey == "m_hScript")
                        {
                            var scriptCode = TryGetFuseGeneralScriptCode(actionChildValue.GetIntegerProperty("m_id"));
                            if (scriptCode != null)
                            {
                                newAction.Add("m_expression", scriptCode);
                            }
                            continue;
                        }

                        if (actionChildKey is "m_eScriptType" or "m_code")
                        {
                            continue;
                        }

                        newAction.Add(actionChildKey, actionChildValue);
                    }

                    return newAction;
                });

                component.Add("m_actions", MakeArray(convertedActions));
            }

            return component;
        }

        if (className == "CLookComponentUpdater")
        {
            component.Add("m_bNetworkLookTarget", compiledComponent.GetIntegerProperty("m_bNetworkLookTarget") > 0);
            component.Add("m_lookHeadingID", ExtractParameterID(compiledComponent.GetSubCollection("m_hLookHeading")));
            component.Add("m_lookHeadingVelocityID", ExtractParameterID(compiledComponent.GetSubCollection("m_hLookHeadingVelocity")));
            component.Add("m_lookPitchID", ExtractParameterID(compiledComponent.GetSubCollection("m_hLookPitch")));
            component.Add("m_lookDirectionID", ExtractParameterID(compiledComponent.GetSubCollection("m_hLookDirection")));
            component.Add("m_lookTargetID", ExtractParameterID(compiledComponent.GetSubCollection("m_hLookTarget")));
            component.Add("m_lookTargetWorldSpaceID", ExtractParameterID(compiledComponent.GetSubCollection("m_hLookTargetWorldSpace")));
            return component;
        }

        if (className == "CSlopeComponentUpdater")
        {
            component.Add("m_slopeAngleID", ExtractParameterID(compiledComponent.GetSubCollection("m_hSlopeAngle")));
            component.Add("m_slopeAngleFrontID", ExtractParameterID(compiledComponent.GetSubCollection("m_hSlopeAngleFront")));
            component.Add("m_slopeAngleSideID", ExtractParameterID(compiledComponent.GetSubCollection("m_hSlopeAngleSide")));
            component.Add("m_slopeHeadingID", ExtractParameterID(compiledComponent.GetSubCollection("m_hSlopeHeading")));
            component.Add("m_slopeNormalID", ExtractParameterID(compiledComponent.GetSubCollection("m_hSlopeNormal")));
            component.Add("m_slopeNormal_WorldSpaceID", ExtractParameterID(compiledComponent.GetSubCollection("m_hSlopeNormal_WorldSpace")));
            return component;
        }

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

                    var weightListNode = MakeNode("CRigidBodyWeightList", ("m_name", weightListName));
                    var weightsArray = KVObject.Array();

                    for (var i = 0; i < weightArray.Length; i++)
                    {
                        var weightDefinition = new KVObject();
                        var boneName = i < boneNames.Length ? boneNames[i] : $"bone_{i}";

                        weightDefinition.Add("m_name", boneName);
                        weightDefinition.Add("m_flWeight", weightArray[i]);
                        weightsArray.Add(weightDefinition);
                    }

                    weightListNode.Add("m_weights", weightsArray);
                    convertedWeightLists.Add(weightListNode);
                }

                component.Add("m_weightLists", MakeArray(convertedWeightLists));
            }

            component.Add("m_flSpringFrequencyMin", compiledComponent.GetFloatProperty("m_flSpringFrequencyMin"));
            component.Add("m_flSpringFrequencyMax", compiledComponent.GetFloatProperty("m_flSpringFrequencyMax"));
            CopyIfPresent(compiledComponent, component, "m_flMaxStretch");
            CopyBoolIfPresent(compiledComponent, component, "m_bSolidCollisionAtZeroWeight");

            return component;
        }

        if (className == "CDampedValueComponentUpdater")
        {
            component.Add("m_name", compiledComponent.GetStringProperty("m_name"));

            if (compiledComponent.ContainsKey("m_items"))
            {
                var items = compiledComponent.GetArray("m_items");
                var convertedItems = items.Select(item =>
                {
                    var newItem = new KVObject();
                    AddFloatOrVectorParamPair(newItem,
                        item.GetSubCollection("m_hParamIn"),
                        item.GetSubCollection("m_hParamOut"));
                    newItem.Add("m_damping", item.GetSubCollection("m_damping"));
                    return newItem;
                });

                component.Add("m_items", MakeArray(convertedItems));
            }

            return component;
        }

        if (className == "CVRInputComponentUpdater")
        {
            string[] paramProperties =
            [
                "m_FingerCurl_Thumb",
                "m_FingerCurl_Index",
                "m_FingerCurl_Middle",
                "m_FingerCurl_Ring",
                "m_FingerCurl_Pinky",
                "m_FingerSplay_Thumb_Index",
                "m_FingerSplay_Index_Middle",
                "m_FingerSplay_Middle_Ring",
                "m_FingerSplay_Ring_Pinky",
            ];

            foreach (var paramName in paramProperties)
            {
                if (compiledComponent.ContainsKey(paramName))
                {
                    component.Add(paramName, ExtractParameterID(compiledComponent.GetSubCollection(paramName)));
                }
            }

            return component;
        }

        if (className == "CRemapValueComponentUpdater")
        {
            component.Add("m_name", compiledComponent.GetStringProperty("m_name"));

            if (compiledComponent.ContainsKey("m_items"))
            {
                var items = compiledComponent.GetArray("m_items");
                var convertedItems = new List<KVObject>();

                foreach (var item in items)
                {
                    var newItem = new KVObject();
                    AddFloatOrVectorParamPair(newItem,
                        item.GetSubCollection("m_hParamIn"),
                        item.GetSubCollection("m_hParamOut"));

                    newItem.Add("m_flMinInputValue", item.GetFloatProperty("m_flMinInputValue"));
                    newItem.Add("m_flMaxInputValue", item.GetFloatProperty("m_flMaxInputValue"));
                    newItem.Add("m_flMinOutputValue", item.GetFloatProperty("m_flMinOutputValue"));
                    newItem.Add("m_flMaxOutputValue", item.GetFloatProperty("m_flMaxOutputValue"));

                    newItem.Add("m_floatParamNameIn", "");
                    newItem.Add("m_floatParamNameOut", "");
                    newItem.Add("m_vectorParamNameIn", "");
                    newItem.Add("m_vectorParamNameOut", "");

                    convertedItems.Add(newItem);
                }
                component.Add("m_items", MakeArray(convertedItems));
            }
            return component;
        }

        if (className == "CAnimScriptComponentUpdater")
        {
            component.Add("m_sName", compiledComponent.GetStringProperty("m_name"));
            component.Add("m_scriptFilename", "");

            return component;
        }

        if (className == "CCPPScriptComponentUpdater")
        {
            component.Add("m_sName", compiledComponent.GetStringProperty("m_name"));
            var scripts = compiledComponent.ContainsKey("m_scriptsToRun")
                ? compiledComponent.GetArray("m_scriptsToRun").Select(s => (KVObject)(s.GetStringProperty("") ?? string.Empty)).ToArray()
                : [];
            component.Add("m_scriptsToRun", MakeArray(scripts));
            return component;
        }

        if (className == "CStateMachineComponentUpdater")
        {
            component.Add("m_sName", compiledComponent.GetStringProperty("m_name"));

            if (compiledComponent.ContainsKey("m_stateMachine"))
            {
                var compiledStateMachine = compiledComponent.GetSubCollection("m_stateMachine");
                var states = ConvertStateMachine(compiledStateMachine, null, null, isComponent: true);
                component.Add("m_states", MakeArray(states));
            }

            return component;
        }

        foreach (var (childKey, childValue) in compiledComponent.Children)
        {
            if (childKey is "_class" or "m_paramHandles" or "m_name" or "m_id" or "m_bStartEnabled" or "m_networkMode")
            {
                continue;
            }

            if (childKey == "m_motors")
            {
                var motors = compiledComponent.GetArray("m_motors");
                var convertedMotors = motors.Select(motor =>
                {
                    var motorClassName = motor.GetStringProperty("_class");
                    var newMotorClassName = motorClassName.Replace("Updater", string.Empty, StringComparison.Ordinal);
                    var newMotor = MakeNode(newMotorClassName);

                    foreach (var (motorChildKey, motorChildValue) in motor.Children)
                    {
                        if (motorChildKey == "_class")
                        {
                            continue;
                        }

                        if (motorChildKey.EndsWith("Param", StringComparison.Ordinal) && motorChildValue.ValueType == KVValueType.Collection)
                        {
                            var newKey = motorChildKey.StartsWith("m_h", StringComparison.Ordinal)
                                ? "m_" + motorChildKey["m_h".Length..]
                                : motorChildKey;
                            newMotor.Add(newKey, ExtractParameterID(motorChildValue));
                            continue;
                        }

                        newMotor.Add(motorChildKey, motorChildValue);
                    }

                    return newMotor;
                });

                component.Add(childKey, MakeArray(convertedMotors));
                continue;
            }

            component.Add(childKey, childValue);
        }

        if (compiledComponent.ContainsKey("m_paramHandles"))
        {
            var paramIDs = compiledComponent.GetArray("m_paramHandles").Select(h => ExtractParameterID(h));
            component.Add("m_paramIDs", MakeArray(paramIDs));
        }

        return component;
    }

    private static IEnumerable<KVObject> EnumerateMotionNodeChildren(KVObject node)
    {
        var className = node.GetStringProperty("_class");

        if (className == "CMotionNodeBlend1D" && node.ContainsKey("m_blendItems"))
        {
            foreach (var blendItem in node.GetArray("m_blendItems"))
            {
                if (blendItem.ContainsKey("m_pChild"))
                {
                    yield return blendItem.GetSubCollection("m_pChild");
                }
            }
        }
        else if (className == "CMotionNode" && node.ContainsKey("m_pChild"))
        {
            yield return node.GetSubCollection("m_pChild");
        }
    }

    private List<KVObject> ConvertMotionNodeHierarchy(KVObject rootNode, List<long> motionParamIds)
    {
        var nodes = new List<KVObject>();
        var idMap = new Dictionary<long, long>();
        var assignedIds = new HashSet<long>();
        var idCursor = GeneratedNodeIdMin;

        // Pass 1: assign IDs in BFS order
        {
            var seen = new HashSet<long>();
            var queue = new Queue<KVObject>();
            queue.Enqueue(rootNode);

            while (queue.Count > 0)
            {
                var currentNode = queue.Dequeue();
                var compiledNodeId = currentNode.GetSubCollection("m_id").GetIntegerProperty("m_id");
                if (!seen.Add(compiledNodeId))
                {
                    continue;
                }

                var newId = GenerateNewNodeId(assignedIds, ref idCursor);
                assignedIds.Add(newId);
                idMap[compiledNodeId] = newId;

                foreach (var child in EnumerateMotionNodeChildren(currentNode))
                {
                    queue.Enqueue(child);
                }
            }
        }

        // Pass 2: convert nodes in BFS order
        {
            var seen = new HashSet<long>();
            var queue = new Queue<KVObject>();
            queue.Enqueue(rootNode);

            while (queue.Count > 0)
            {
                var currentNode = queue.Dequeue();
                var compiledNodeId = currentNode.GetSubCollection("m_id").GetIntegerProperty("m_id");
                if (!seen.Add(compiledNodeId))
                {
                    continue;
                }

                var newNodeId = idMap[compiledNodeId];
                var uncompiledNode = ConvertMotionNode(currentNode, motionParamIds, idMap);
                uncompiledNode.Add("m_nNodeID", MakeNodeIdObjectValue(newNodeId));
                nodes.Add(MakeNodeManagerEntry(newNodeId, uncompiledNode));

                foreach (var child in EnumerateMotionNodeChildren(currentNode))
                {
                    queue.Enqueue(child);
                }
            }
        }

        var rootNodeId = rootNode.GetSubCollection("m_id").GetIntegerProperty("m_id");
        var rootNodeNewId = idMap[rootNodeId];
        var rootAnimNodeId = GenerateNewNodeId(assignedIds, ref idCursor);

        var rootAnimNode = MakeNode("CRootAnimNode");
        rootAnimNode.Add("m_sName", "Unnamed");
        rootAnimNode.Add("m_nNodeID", MakeNodeIdObjectValue(rootAnimNodeId));
        rootAnimNode.Add("m_networkMode", "ServerAuthoritative");

        var random = new Random((int)rootAnimNodeId);
        var posX = 400 + random.Next(0, 200);
        var posY = 50 + random.Next(0, 100);
        rootAnimNode.Add("m_vecPosition", MakeVector2(posX, posY));

        rootAnimNode.Add("m_inputConnection", MakeInputConnection(rootNodeNewId));
        nodes.Add(MakeNodeManagerEntry(rootAnimNodeId, rootAnimNode));

        return nodes;
    }

    private KVObject CreateSequenceMotionNode(KVObject compiledMotionNode, float posX, float posY)
    {
        var sequenceNode = MakeNode("CSequenceAnimNode");
        sequenceNode.Add("m_sName", compiledMotionNode.GetStringProperty("m_name") ?? "Unnamed");
        sequenceNode.Add("m_vecPosition", MakeVector2(posX, posY));
        sequenceNode.Add("m_sequenceName", compiledMotionNode.ContainsKey("m_hSequence")
            ? GetSequenceName(compiledMotionNode.GetIntegerProperty("m_hSequence"))
            : "");
        sequenceNode.Add("m_playbackSpeed", compiledMotionNode.GetFloatProperty("m_flPlaybackSpeed", 1.0f));
        sequenceNode.Add("m_bLoop", compiledMotionNode.GetIntegerProperty("m_bLoop") > 0);
        sequenceNode.Add("m_tagSpans", KVObject.Array());
        sequenceNode.Add("m_paramSpans", KVObject.Array());
        sequenceNode.Add("m_networkMode", "ServerAuthoritative");
        return sequenceNode;
    }

    private static KVObject CreateBlendMotionNode(KVObject compiledMotionNode, List<long> motionParamIds, Dictionary<long, long> idMap, float posX, float posY)
    {
        var blendNode = MakeNode("CBlendAnimNode");
        blendNode.Add("m_sName", compiledMotionNode.GetStringProperty("m_name") ?? "Unnamed");
        blendNode.Add("m_vecPosition", MakeVector2(posX, posY));

        if (compiledMotionNode.ContainsKey("m_blendItems"))
        {
            var blendItems = compiledMotionNode.GetArray("m_blendItems");
            var children = new List<KVObject>();

            foreach (var blendItem in blendItems)
            {
                var blendChild = MakeNode("CBlendNodeChild");
                blendChild.Add("m_name", "Unnamed");
                blendChild.Add("m_blendValue", blendItem.GetFloatProperty("m_flKeyValue"));

                if (blendItem.ContainsKey("m_pChild"))
                {
                    var childNode = blendItem.GetSubCollection("m_pChild");
                    var childCompiledId = childNode.GetSubCollection("m_id").GetIntegerProperty("m_id");
                    var childNewId = idMap[childCompiledId];
                    var inputConnection = MakeInputConnection(childNewId);
                    blendChild.Add("m_inputConnection", inputConnection);
                }

                children.Add(blendChild);
            }

            blendNode.Add("m_children", MakeArray(children));
        }

        blendNode.Add("m_blendValueSource", "Parameter");

        var paramIndex = compiledMotionNode.ContainsKey("m_nParamIndex")
            ? (int)compiledMotionNode.GetIntegerProperty("m_nParamIndex")
            : -1;
        var motionParamId = paramIndex >= 0 && paramIndex < motionParamIds.Count
            ? motionParamIds[paramIndex]
            : -1L;
        blendNode.Add("m_param", MakeNodeIdObjectValue(motionParamId));

        blendNode.Add("m_blendKeyType", "BlendKey_UserValue");
        blendNode.Add("m_bLockBlendOnReset", false);
        blendNode.Add("m_bSyncCycles", true);
        blendNode.Add("m_bLoop", true);
        blendNode.Add("m_bLockWhenWaning", true);
        blendNode.Add("m_damping", MakeDefaultDamping());
        blendNode.Add("m_networkMode", "ServerAuthoritative");

        return blendNode;
    }

    private KVObject ConvertMotionNode(KVObject compiledMotionNode, List<long> motionParamIds, Dictionary<long, long> idMap)
    {
        var className = compiledMotionNode.GetStringProperty("_class");

        var compiledNodeId = compiledMotionNode.GetSubCollection("m_id").GetIntegerProperty("m_id");
        var newNodeId = idMap[compiledNodeId];
        var random = new Random((int)newNodeId);
        var posX = -200 + random.Next(0, 400);
        var posY = -200 + random.Next(0, 400);

        // Check for sequence node
        if ((className == "CMotionNodeSequence") ||
            (className == "CMotionNode" && compiledMotionNode.ContainsKey("m_hSequence")))
        {
            return CreateSequenceMotionNode(compiledMotionNode, posX, posY);
        }

        // Check for blend node
        if ((className == "CMotionNodeBlend1D") ||
            (className == "CMotionNode" && compiledMotionNode.ContainsKey("m_blendItems")))
        {
            return CreateBlendMotionNode(compiledMotionNode, motionParamIds, idMap, posX, posY);
        }

        // Default fallback
        var defaultNode = MakeNode("CSequenceAnimNode");
        defaultNode.Add("m_sName", compiledMotionNode.GetStringProperty("m_name") ?? "Unnamed");
        defaultNode.Add("m_vecPosition", MakeVector2(posX, posY));
        defaultNode.Add("m_sequenceName", "");
        defaultNode.Add("m_playbackSpeed", 1.0f);
        defaultNode.Add("m_bLoop", false);
        defaultNode.Add("m_tagSpans", KVObject.Array());
        defaultNode.Add("m_paramSpans", KVObject.Array());
        defaultNode.Add("m_networkMode", "ServerAuthoritative");

        return defaultNode;
    }

    private KVObject ConvertToUncompiled(KVObject compiledNode, List<long> outConnections)
    {
        footPinningItems = [];
        var className = compiledNode.GetStringProperty("_class");
        className = className.Replace("UpdateNode", string.Empty, StringComparison.Ordinal);

        var newClass = className + "AnimNode";
        var node = MakeNode(newClass);

        var children = compiledNode.GetArray("m_children");
        var inputNodeIds = children?.Select(child =>
        {
            var nodeIndex = child.GetIntegerProperty("m_nodeIndex");
            return nodeIndexToIdMap?.TryGetValue(nodeIndex, out var nodeId) == true ? nodeId : -1L;
        }).Where(id => id != -1).ToArray();

        if (inputNodeIds != null)
        {
            outConnections.AddRange(inputNodeIds);
        }

        foreach (var (key, value) in compiledNode.Children)
        {
            if (key is "_class" or "m_nodePath")
            {
                continue;
            }

            var newKey = key;

            if (PropertyMappings.TryGetValue(className, out var classMap) && classMap.TryGetValue(key, out var mapEntry))
            {
                if (mapEntry.Action == PropAction.Skip)
                {
                    continue;
                }
                HandleMappedProperty(node, compiledNode, key, value, outConnections, mapEntry.Action, mapEntry.OutputKey);
                continue;
            }

            if (key == "m_name" && NameToSNameClasses.Contains(className))
            {
                newKey = "m_sName";
            }

            if (key is "m_pChildNode" or "m_pChild1" or "m_pChild2" or "m_pChild" && value.ValueType == KVValueType.Collection)
            {
                var nodeIndex = value.GetIntegerProperty("m_nodeIndex");
                if (nodeIndexToIdMap?.TryGetValue(nodeIndex, out var nodeId) == true)
                {
                    outConnections.Add(nodeId);

                    var connectionKey = key switch
                    {
                        "m_pChildNode" => "m_inputConnection",
                        "m_pChild1" when className == "CAdd" => "m_baseInput",
                        "m_pChild2" when className == "CAdd" => "m_additiveInput",
                        "m_pChild1" when className == "CSubtract" => "m_baseInputConnection",
                        "m_pChild2" when className == "CSubtract" => "m_subtractInputConnection",
                        "m_pChild1" when className == "CBoneMask" => "m_inputConnection1",
                        "m_pChild2" when className == "CBoneMask" => "m_inputConnection2",
                        _ => key
                    };

                    if (connectionKey != key)
                    {
                        var connection = MakeInputConnection(nodeId);
                        node.Add(connectionKey, connection);
                        continue;
                    }
                }
            }

            if (key == "m_hSequence")
            {
                var sequenceIndex = compiledNode.GetIntegerProperty("m_hSequence");
                var sequenceName = GetSequenceName(sequenceIndex);
                node.Add("m_sequenceName", sequenceName);
                continue;
            }

            if (className == "CSelector")
            {
                if (key == "m_hParameter")
                {
                    var paramRef = value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    var tagIndex = compiledNode.GetIntegerProperty("m_nTagIndex");

                    if (paramIndex == 255 || (tagIndex != -1 && paramType == "ANIMPARAM_UNKNOWN"))
                    {
                        continue;
                    }

                    if (!node.ContainsKey("m_selectionSource"))
                    {
                        node.Add("m_selectionSource",
                            paramType == "ANIMPARAM_ENUM" ? "SelectionSource_Enum" : "SelectionSource_Bool");
                    }
                    var source = paramType["ANIMPARAM_".Length..].ToLowerInvariant();
                    node.Add($"m_{source}ParamID", ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
            }
            else if (className == "CChoice")
            {
                if (key == "m_children")
                {
                    var weights = compiledNode.GetFloatArray("m_weights");
                    var blendTimes = compiledNode.GetFloatArray("m_blendTimes");

                    if (inputNodeIds is not null)
                    {
                        var newInputs = weights.Zip(blendTimes, inputNodeIds).Select((choice, index) =>
                        {
                            var (weight, blendTime, nodeId) = choice;
                            var choiceNode = new KVObject();
                            AddInputConnection(choiceNode, nodeId);
                            choiceNode.Add("m_name", (index + 1).ToString(CultureInfo.InvariantCulture));
                            choiceNode.Add("m_weight", weight);
                            choiceNode.Add("m_blendTime", blendTime);
                            return choiceNode;
                        });

                        node.Add("m_children", MakeArray(newInputs));
                    }

                    continue;
                }
            }
            else if (className is "CBoneMask" or "CRagdoll")
            {
                if (key == "m_nWeightListIndex")
                {
                    node.Add("m_weightListName", GetWeightListName(compiledNode.GetIntegerProperty("m_nWeightListIndex")));
                    continue;
                }
            }
            else if (className == "CBlend")
            {
                if (key == "m_children")
                {
                    if (inputNodeIds is not null)
                    {
                        var targetValues = compiledNode.GetFloatArray("m_targetValues");
                        var blendChildren = inputNodeIds.Select((nodeId, i) =>
                        {
                            var blendChild = MakeNode("CBlendNodeChild");
                            AddInputConnection(blendChild, nodeId);
                            blendChild.Add("m_name", "Unnamed");
                            blendChild.Add("m_blendValue", i < targetValues.Length ? targetValues[i] : 0.0f);
                            return blendChild;
                        }).ToArray();
                        node.Add("m_children", MakeArray(blendChildren));
                    }
                    continue;
                }
            }
            else if (className == "CBlend2D")
            {
                if (key == "m_items")
                {
                    var items = compiledNode.GetArray("m_items");
                    var convertedItems = new List<KVObject>();

                    foreach (var item in items)
                    {
                        var hasSequence = item.GetIntegerProperty("m_hSequence", -1) != -1;
                        var hasChild = item.GetSubCollection("m_pChild")?.GetIntegerProperty("m_nodeIndex", -1) is > -1;
                        var itemClass = !hasSequence && hasChild ? "CNodeBlend2DItem" : "CSequenceBlend2DItem";

                        var convertedItem = new KVObject();

                        foreach (var (itemKey, itemValue) in item.Children)
                        {
                            if (itemKey == "m_hSequence")
                            {
                                var sequenceIndex = item.GetIntegerProperty("m_hSequence");
                                if (sequenceIndex != -1)
                                {
                                    convertedItem.Add("m_sequenceName", GetSequenceName(sequenceIndex));
                                }
                            }
                            else if (itemKey == "m_pChild")
                            {
                                if (itemClass == "CNodeBlend2DItem"
                                    && nodeIndexToIdMap?.TryGetValue(item.GetSubCollection("m_pChild").GetIntegerProperty("m_nodeIndex"), out var nodeId) == true)
                                {
                                    convertedItem.Add("m_inputConnection", MakeInputConnection(nodeId));
                                }
                            }
                            else if (itemKey == "m_tags")
                            {
                                try
                                {
                                    convertedItem.Add("m_tagSpans", ConvertTagSpansArray(item.GetArray("m_tags")));
                                }
                                catch
                                {
                                    convertedItem.Add("m_tagSpans", KVObject.Array());
                                }
                            }
                            else if (itemKey == "m_vPos")
                            {
                                convertedItem.Add("m_blendValue", itemValue);
                            }
                            else if (itemKey == "m_flDuration")
                            {
                                if (item.GetIntegerProperty("m_bUseCustomDuration") > 0)
                                {
                                    convertedItem.Add("m_flCustomDuration", itemValue);
                                }
                            }
                            else
                            {
                                convertedItem.Add(itemKey, itemValue);
                            }
                        }
                        convertedItem.Add("_class", itemClass);
                        convertedItems.Add(convertedItem);
                    }

                    node.Add("m_items", MakeArray(convertedItems));
                    continue;
                }
                if (key == "m_tags")
                {
                    try
                    {
                        node.Add("m_tagSpans", ConvertTagSpansArray(compiledNode.GetArray("m_tags")));
                    }
                    catch
                    {
                        node.Add("m_tagSpans", KVObject.Array());
                    }
                    continue;
                }
            }
            else if (className == "CAimMatrix")
            {
                if (key == "m_opFixedSettings")
                {
                    var opFixedSettings = value;

                    CopyIfPresent(opFixedSettings, node, "m_eBlendMode", "m_blendMode");

                    if (opFixedSettings.ContainsKey("m_nBoneMaskIndex"))
                    {
                        var boneMaskIndex = opFixedSettings.GetIntegerProperty("m_nBoneMaskIndex");
                        node.Add("m_boneMaskName", boneMaskIndex == -1 ? "" : GetWeightListName(boneMaskIndex));
                    }

                    CopyIfPresent(opFixedSettings, node, "m_damping");
                    CopyIfPresent(opFixedSettings, node, "m_flMaxYawAngle", "m_fAngleIncrement");

                    if (opFixedSettings.ContainsKey("m_attachment"))
                    {
                        node.Add("m_attachmentName", FindMatchingAttachmentName(opFixedSettings.GetSubCollection("m_attachment")));
                    }
                    else
                    {
                        node.Add("m_attachmentName", "aim");
                    }
                    continue;
                }
            }
            else if (className == "CDirectionalBlend")
            {
                if (key == "m_hSequences")
                {
                    var sequenceIndices = compiledNode.GetIntegerArray("m_hSequences");
                    if (sequenceIndices.Length == 8)
                    {
                        var sequenceNames = new string[8];
                        for (var i = 0; i < 8; i++)
                        {
                            sequenceNames[i] = GetSequenceName(sequenceIndices[i]);
                        }
                        var prefix = ExtractCommonPrefix(sequenceNames);
                        if (!string.IsNullOrEmpty(prefix))
                        {
                            node.Add("m_animNamePrefix", prefix);
                        }
                        else
                        {
                            node.Add("m_animNamePrefix", "");
                        }
                    }
                    else
                    {
                        node.Add("m_animNamePrefix", "");
                    }
                    continue;
                }
            }
            else if (className == "CFollowAttachment")
            {
                if (key == "m_opFixedData")
                {
                    var opFixedData = value;

                    if (opFixedData.ContainsKey("m_boneIndex"))
                    {
                        node.Add("m_boneName", GetBoneName((int)opFixedData.GetIntegerProperty("m_boneIndex")));
                    }

                    if (opFixedData.ContainsKey("m_attachment"))
                    {
                        node.Add("m_attachmentName", FindMatchingAttachmentName(opFixedData.GetSubCollection("m_attachment")));
                    }

                    CopyBoolIfPresent(opFixedData, node, "m_bMatchTranslation");
                    CopyBoolIfPresent(opFixedData, node, "m_bMatchRotation");

                    continue;
                }
            }
            else if (className == "CFootAdjustment")
            {
                if (key == "m_clips")
                {
                    var clipNames = compiledNode.GetIntegerArray("m_clips").Select(idx => (KVObject)GetSequenceName(idx)).ToArray();
                    node.Add("m_clips", MakeArray(clipNames));
                    continue;
                }
                if (!node.ContainsKey("m_baseClipName"))
                {
                    node.Add("m_baseClipName", "");
                }
            }
            else if (className == "CFootPinning")
            {
                if (key == "m_poseOpFixedData")
                {
                    var poseOpFixedData = value;
                    if (poseOpFixedData.ContainsKey("m_footInfo"))
                    {
                        var footInfoArray = poseOpFixedData.GetArray("m_footInfo");
                        footPinningItems = [];

                        foreach (var footInfo in footInfoArray)
                        {
                            var convertedItem = new KVObject();
                            if (footInfo.ContainsKey("m_nFootIndex"))
                            {
                                AddIfNotEmpty(convertedItem, "m_footName", GetFootName((int)footInfo.GetIntegerProperty("m_nFootIndex")));
                            }
                            if (footInfo.ContainsKey("m_nTargetBoneIndex"))
                            {
                                AddIfNotEmpty(convertedItem, "m_targetBoneName", GetBoneName((int)footInfo.GetIntegerProperty("m_nTargetBoneIndex")));
                            }
                            if (footInfo.ContainsKey("m_ikChainIndex"))
                            {
                                AddIfNotEmpty(convertedItem, "m_ikChainName", GetIKChainName((int)footInfo.GetIntegerProperty("m_ikChainIndex")));
                            }
                            if (footInfo.ContainsKey("m_nTagIndex"))
                            {
                                convertedItem.Add("m_tag", MakeNodeIdObjectValue(GetTagIdFromIndex(footInfo.GetIntegerProperty("m_nTagIndex"))));
                            }
                            convertedItem.Add("m_param", MakeNodeIdObjectValue(-1));
                            CopyIfPresent(footInfo, convertedItem, "m_flMaxRotationLeft");
                            CopyIfPresent(footInfo, convertedItem, "m_flMaxRotationRight");
                            footPinningItems.Add(convertedItem);
                        }
                    }
                    CopyIfPresent(poseOpFixedData, node, "m_flBlendTime");
                    CopyIfPresent(poseOpFixedData, node, "m_flLockBreakDistance");
                    CopyIfPresent(poseOpFixedData, node, "m_flMaxLegTwist");
                    if (poseOpFixedData.ContainsKey("m_nHipBoneIndex"))
                    {
                        AddIfNotEmpty(node, "m_hipBoneName", GetBoneName((int)poseOpFixedData.GetIntegerProperty("m_nHipBoneIndex")));
                    }
                    CopyBoolIfPresent(poseOpFixedData, node, "m_bApplyLegTwistLimits");
                    CopyBoolIfPresent(poseOpFixedData, node, "m_bApplyFootRotationLimits");
                    continue;
                }
                else if (key == "m_params")
                {
                    var paramHandles = compiledNode.GetArray("m_params");
                    var existingItems = node.GetArray("m_items");
                    var itemsList = existingItems?.Count > 0
                        ? existingItems.ToList()
                        : footPinningItems ?? [];

                    for (var i = 0; i < itemsList.Count && i < paramHandles.Count; i++)
                    {
                        itemsList[i]["m_param"] = ExtractParameterID(paramHandles[i]);
                    }

                    node.Add("m_items", MakeArray(itemsList));
                    continue;
                }
            }
            else if (className == "CFootStepTrigger")
            {
                if (key == "m_triggers")
                {
                    var triggers = compiledNode.GetArray("m_triggers");
                    var convertedItems = new List<KVObject>();
                    foreach (var trigger in triggers)
                    {
                        var convertedItem = new KVObject();
                        if (trigger.ContainsKey("m_nFootIndex"))
                        {
                            var footIndex = (int)trigger.GetIntegerProperty("m_nFootIndex");
                            var footName = GetFootName(footIndex);
                            if (!string.IsNullOrEmpty(footName))
                            {
                                convertedItem.Add("m_footName", footName);
                            }
                        }
                        if (trigger.ContainsKey("m_triggerPhase"))
                        {
                            convertedItem.Add("m_triggerPhase", trigger.GetStringProperty("m_triggerPhase"));
                        }
                        if (trigger.ContainsKey("m_tags"))
                        {
                            try
                            {
                                var tagIndices = trigger.GetIntegerArray("m_tags");
                                convertedItem.Add("m_tags", ConvertTagIndicesArray(tagIndices));
                            }
                            catch (InvalidCastException)
                            {
                                convertedItem.Add("m_tags", KVObject.Array());
                            }
                        }
                        else
                        {
                            convertedItem.Add("m_tags", KVObject.Array());
                        }
                        convertedItems.Add(convertedItem);
                    }
                    node.Add("m_items", MakeArray(convertedItems));
                    continue;
                }
            }
            else if (className == "CJiggleBone")
            {
                if (key == "m_opFixedData")
                {
                    var opFixedData = value;
                    if (opFixedData.ContainsKey("m_boneSettings"))
                    {
                        var boneSettingsArray = opFixedData.GetArray("m_boneSettings");
                        var convertedItems = new List<KVObject>();
                        foreach (var boneSetting in boneSettingsArray)
                        {
                            var convertedItem = new KVObject();
                            if (boneSetting.ContainsKey("m_nBoneIndex"))
                            {
                                AddIfNotEmpty(convertedItem, "m_boneName", GetBoneName((int)boneSetting.GetIntegerProperty("m_nBoneIndex")));
                            }
                            CopyIfPresent(boneSetting, convertedItem, "m_flSpringStrength");
                            if (boneSetting.ContainsKey("m_flMaxTimeStep"))
                            {
                                var maxTimeStep = boneSetting.GetFloatProperty("m_flMaxTimeStep");
                                convertedItem.Add("m_flSimRateFPS", maxTimeStep > 0 ? 1.0f / maxTimeStep : 90.0f);
                            }
                            CopyIfPresent(boneSetting, convertedItem, "m_flDamping");
                            CopyIfPresent(boneSetting, convertedItem, "m_eSimSpace");
                            CopyIfPresent(boneSetting, convertedItem, "m_vBoundsMaxLS");
                            CopyIfPresent(boneSetting, convertedItem, "m_vBoundsMinLS");
                            convertedItems.Add(convertedItem);
                        }
                        node.Add("m_items", MakeArray(convertedItems));
                    }
                    continue;
                }
            }
            else if (className == "CJumpHelper")
            {
                if (key == "m_name")
                {
                    node.Add("m_sName", value.ToString() ?? "Unnamed");
                    continue;
                }
                else if (key == "m_flJumpEndCycle")
                {
                    var jumpDuration = compiledNode.GetFloatProperty("m_flJumpEndCycle")
                        - compiledNode.GetFloatProperty("m_flJumpStartCycle");
                    node.Add("m_flJumpDuration", jumpDuration);
                    continue;
                }
                else if (key == "m_tags")
                {
                    try
                    {
                        var tagIndices = compiledNode.GetIntegerArray("m_tags");
                        node.Add("m_tagSpans", ConvertTagIndicesArray(tagIndices));
                    }
                    catch (InvalidCastException)
                    {
                        node.Add("m_tagSpans", KVObject.Array());
                    }
                    continue;
                }
                else if (key == "m_paramSpans")
                {
                    try
                    {
                        node.Add("m_paramSpans", ConvertParamSpans(compiledNode.GetSubCollection("m_paramSpans")));
                    }
                    catch
                    {
                        node.Add("m_paramSpans", KVObject.Array());
                    }
                    continue;
                }
            }
            else if (className == "CLeanMatrix")
            {
                if (key == "m_name")
                {
                    node.Add("m_sName", value.ToString() ?? "Unnamed");
                    continue;
                }
            }
            else if (className == "CLookAt")
            {
                if (key == "m_opFixedSettings")
                {
                    var opFixedSettings = value;
                    node.Add("m_lookatChainName", opFixedSettings.ContainsKey("m_bones")
                        ? FindMatchingLookAtChainName(opFixedSettings)
                        : "");
                    node.Add("m_attachmentName", opFixedSettings.ContainsKey("m_attachment")
                        ? FindMatchingAttachmentName(opFixedSettings.GetSubCollection("m_attachment"))
                        : "aim");
                    CopyIfPresent(opFixedSettings, node, "m_flYawLimit");
                    CopyIfPresent(opFixedSettings, node, "m_flPitchLimit");
                    CopyIfPresent(opFixedSettings, node, "m_flHysteresisInnerAngle");
                    CopyIfPresent(opFixedSettings, node, "m_flHysteresisOuterAngle");
                    CopyBoolIfPresent(opFixedSettings, node, "m_bRotateYawForward");
                    CopyBoolIfPresent(opFixedSettings, node, "m_bMaintainUpDirection");
                    CopyBoolIfPresent(opFixedSettings, node, "m_bTargetIsPosition", "m_bIsPosition");
                    CopyBoolIfPresent(opFixedSettings, node, "m_bUseHysteresis");
                    CopyIfPresent(opFixedSettings, node, "m_damping");
                    continue;
                }
            }
            else if (className == "CHitReact")
            {
                if (key == "m_opFixedSettings")
                {
                    var opFixedSettings = value;
                    if (opFixedSettings.ContainsKey("m_nWeightListIndex"))
                    {
                        node.Add("m_weightListName", GetWeightListName(opFixedSettings.GetIntegerProperty("m_nWeightListIndex")));
                    }
                    if (opFixedSettings.ContainsKey("m_nHipBoneIndex"))
                    {
                        AddIfNotEmpty(node, "m_hipBoneName", GetBoneName((int)opFixedSettings.GetIntegerProperty("m_nHipBoneIndex")));
                    }
                    string[] settingsToCopy =
                    [
                        "m_nEffectedBoneCount",
                        "m_flMaxImpactForce",
                        "m_flMinImpactForce",
                        "m_flWhipImpactScale",
                        "m_flCounterRotationScale",
                        "m_flDistanceFadeScale",
                        "m_flPropagationScale",
                        "m_flWhipDelay",
                        "m_flSpringStrength",
                        "m_flWhipSpringStrength",
                        "m_flMaxAngleRadians",
                        "m_flHipBoneTranslationScale",
                        "m_flHipDipSpringStrength",
                        "m_flHipDipImpactScale",
                        "m_flHipDipDelay",
                    ];
                    foreach (var settingKey in settingsToCopy)
                    {
                        CopyIfPresent(opFixedSettings, node, settingKey);
                    }
                    continue;
                }
            }
            else if (className == "CSolveIKChain")
            {
                if (key == "m_opFixedData")
                {
                    var opFixedData = value;
                    var chainsToSolveData = opFixedData.GetArray("m_ChainsToSolveData");
                    var targetHandles = compiledNode.GetArray("m_targetHandles");

                    var ikChainsArray = new List<KVObject>();

                    for (var i = 0; i < chainsToSolveData.Count; i++)
                    {
                        var chainData = chainsToSolveData[i];
                        var targetHandle = i < targetHandles.Count ? targetHandles[i] : null;

                        var ikChain = new KVObject();
                        ikChain.Add("_class", "CSolveIKChainAnimNodeChainData");

                        if (chainData.ContainsKey("m_nChainIndex"))
                        {
                            ikChain.Add("m_IkChain", GetIKChainName((int)chainData.GetIntegerProperty("m_nChainIndex")));
                        }

                        ikChain.Add("m_SolverSettingSource", "SOLVEIKCHAINANIMNODESETTINGSOURCE_Default");

                        if (chainData.ContainsKey("m_SolverSettings"))
                        {
                            var solverSettings = chainData.GetSubCollection("m_SolverSettings");
                            var overrideSolverSettings = new KVObject();
                            CopyIfPresent(solverSettings, overrideSolverSettings, "m_SolverType");
                            ikChain.Add("m_OverrideSolverSettings", overrideSolverSettings);
                        }

                        ikChain.Add("m_TargetSettingSource", "SOLVEIKCHAINANIMNODESETTINGSOURCE_Default");

                        if (chainData.ContainsKey("m_TargetSettings"))
                        {
                            var targetSettings = chainData.GetSubCollection("m_TargetSettings");
                            var overrideTargetSettings = new KVObject();

                            CopyIfPresent(targetSettings, overrideTargetSettings, "m_TargetSource");

                            if (targetSettings.ContainsKey("m_Bone"))
                            {
                                var boneNameObj = new KVObject();
                                boneNameObj.Add("m_Name", targetSettings.GetSubCollection("m_Bone").GetStringProperty("m_Name"));
                                overrideTargetSettings.Add("m_Bone", boneNameObj);
                            }

                            if (targetHandle != null)
                            {
                                overrideTargetSettings.Add("m_AnimgraphParameterNamePosition",
                                    ExtractParameterID(targetHandle.GetSubCollection("m_positionHandle")));
                                overrideTargetSettings.Add("m_AnimgraphParameterNameOrientation",
                                    ExtractParameterID(targetHandle.GetSubCollection("m_orientationHandle")));
                            }
                            else
                            {
                                overrideTargetSettings.Add("m_AnimgraphParameterNamePosition", MakeNodeIdObjectValue(-1));
                                overrideTargetSettings.Add("m_AnimgraphParameterNameOrientation", MakeNodeIdObjectValue(-1));
                            }

                            CopyIfPresent(targetSettings, overrideTargetSettings, "m_TargetCoordSystem");

                            ikChain.Add("m_OverrideTargetSettings", overrideTargetSettings);
                        }

                        CopyIfPresent(chainData, ikChain, "m_DebugSetting");
                        CopyIfPresent(chainData, ikChain, "m_flDebugNormalizedValue", "m_flDebugNormalizedLength");
                        CopyIfPresent(chainData, ikChain, "m_vDebugOffset");

                        ikChainsArray.Add(ikChain);
                    }

                    node.Add("m_IkChains", MakeArray(ikChainsArray));

                    CopyBoolIfPresent(opFixedData, node, "m_bMatchTargetOrientation");

                    continue;
                }
            }
            else if (className == "CStanceOverride")
            {
                if (key == "m_pStanceSourceNode")
                {
                    var stanceSourceNodeId = value.GetIntegerProperty("m_nodeIndex");
                    if (stanceSourceNodeId != -1 && nodeIndexToIdMap?.TryGetValue(stanceSourceNodeId, out var mappedNodeId) == true)
                    {
                        node.Add("m_stanceSourceConnection", MakeInputConnection(mappedNodeId));
                    }
                    else if (stanceSourceNodeId == -1)
                    {
                        node.Add("m_stanceSourceConnection", MakeInputConnection(-1));
                    }
                    continue;
                }
                else if (key == "m_opFixedData")
                {
                    CopyIfPresent(value, node, "m_nFrameIndex");
                    continue;
                }
                if (!node.ContainsKey("m_sequenceName"))
                {
                    node.Add("m_sequenceName", "");
                }
                if (!node.ContainsKey("m_nFrameIndex"))
                {
                    node.Add("m_nFrameIndex", 0);
                }
            }
            else if (className == "CSkeletalInput")
            {
                if (!node.ContainsKey("m_transformSource"))
                {
                    node.Add("m_transformSource", "AnimVrBoneTransformSource_LiveStream");
                }
                if (!node.ContainsKey("m_motionRange"))
                {
                    node.Add("m_motionRange", "MotionRange_WithController");
                }
                if (!node.ContainsKey("m_bEnableIK"))
                {
                    node.Add("m_bEnableIK", true);
                }
                if (!node.ContainsKey("m_bEnableCollision"))
                {
                    node.Add("m_bEnableCollision", true);
                }
            }
            else if (className == "CFootLock")
            {
                if (key == "m_name")
                {
                    var nameValue = value.ToString() ?? "Unnamed";
                    node.Add("m_sName", nameValue);
                    continue;
                }
                else if (key == "m_opFixedSettings")
                {
                    var opFixedSettings = value;

                    if (opFixedSettings.ContainsKey("m_nHipBoneIndex"))
                    {
                        AddIfNotEmpty(node, "m_hipBoneName", GetBoneName((int)opFixedSettings.GetIntegerProperty("m_nHipBoneIndex")));
                    }

                    CopyIfPresent(opFixedSettings, node, "m_ikSolverType");
                    CopyBoolIfPresent(opFixedSettings, node, "m_bAlwaysUseFallbackHinge");
                    CopyBoolIfPresent(opFixedSettings, node, "m_bApplyLegTwistLimits");
                    CopyIfPresent(opFixedSettings, node, "m_flMaxLegTwist");
                    CopyBoolIfPresent(opFixedSettings, node, "m_bApplyTilt");
                    CopyBoolIfPresent(opFixedSettings, node, "m_bApplyHipDrop");

                    if (opFixedSettings.ContainsKey("m_bApplyFootRotationLimits") && !node.ContainsKey("m_bApplyFootRotationLimits"))
                    {
                        node.Add("m_bApplyFootRotationLimits", opFixedSettings.GetIntegerProperty("m_bApplyFootRotationLimits") > 0);
                    }

                    CopyIfPresent(opFixedSettings, node, "m_flMaxFootHeight");
                    CopyIfPresent(opFixedSettings, node, "m_flExtensionScale");
                    CopyBoolIfPresent(opFixedSettings, node, "m_bEnableLockBreaking");
                    CopyIfPresent(opFixedSettings, node, "m_flLockBreakTolerance");
                    CopyIfPresent(opFixedSettings, node, "m_flLockBlendTime", "m_flLockBreakBlendTime");
                    CopyBoolIfPresent(opFixedSettings, node, "m_bEnableStretching");
                    CopyIfPresent(opFixedSettings, node, "m_flMaxStretchAmount");
                    CopyIfPresent(opFixedSettings, node, "m_flStretchExtensionScale");
                    CopyIfPresent(opFixedSettings, node, "m_hipDampingSettings");
                    continue;
                }
                else if (key == "m_footSettings")
                {
                    var footSettings = compiledNode.GetArray("m_footSettings");
                    var opFixedSettings = compiledNode.GetSubCollection("m_opFixedSettings");
                    var footInfoArray = opFixedSettings?.GetArray("m_footInfo");

                    var items = new List<KVObject>();

                    var firstFootHasGroundTracing = false;
                    var firstFootTraceAngleBlend = 0.0f;

                    for (var i = 0; i < footSettings.Count; i++)
                    {
                        var footSetting = footSettings[i];
                        var footInfo = footInfoArray != null && i < footInfoArray.Count ? footInfoArray[i] : null;

                        var item = new KVObject();

                        if (footSetting.ContainsKey("m_nFootIndex"))
                        {
                            AddIfNotEmpty(item, "m_footName", GetFootName((int)footSetting.GetIntegerProperty("m_nFootIndex")));
                        }

                        if (footInfo?.ContainsKey("m_nTargetBoneIndex") == true)
                        {
                            AddIfNotEmpty(item, "m_targetBoneName", GetBoneName((int)footInfo.GetIntegerProperty("m_nTargetBoneIndex")));
                        }

                        if (footInfo?.ContainsKey("m_ikChainIndex") == true)
                        {
                            AddIfNotEmpty(item, "m_ikChainName", GetIKChainName((int)footInfo.GetIntegerProperty("m_ikChainIndex")));
                        }

                        if (footSetting.ContainsKey("m_nDisableTagIndex"))
                        {
                            item.Add("m_disableTagID",
                                MakeNodeIdObjectValue(GetTagIdFromIndex(footSetting.GetIntegerProperty("m_nDisableTagIndex"))));
                        }

                        if (footSetting.ContainsKey("m_footstepLandedTagIndex"))
                        {
                            item.Add("m_footstepLandedTag",
                                MakeNodeIdObjectValue(GetTagIdFromIndex(footSetting.GetIntegerProperty("m_footstepLandedTagIndex"))));
                        }

                        if (footSetting.ContainsKey("m_flMaxRotationLeft"))
                        {
                            item.Add("m_flMaxRotationLeft", footSetting.GetFloatProperty("m_flMaxRotationLeft"));
                        }
                        else if (footInfo?.ContainsKey("m_flMaxRotationLeft") == true)
                        {
                            item.Add("m_flMaxRotationLeft", footInfo.GetFloatProperty("m_flMaxRotationLeft"));
                        }

                        if (footSetting.ContainsKey("m_flMaxRotationRight"))
                        {
                            item.Add("m_flMaxRotationRight", footSetting.GetFloatProperty("m_flMaxRotationRight"));
                        }
                        else if (footInfo?.ContainsKey("m_flMaxRotationRight") == true)
                        {
                            item.Add("m_flMaxRotationRight", footInfo.GetFloatProperty("m_flMaxRotationRight"));
                        }

                        if (i == 0)
                        {
                            if (footSetting.ContainsKey("m_bEnableTracing"))
                            {
                                firstFootHasGroundTracing = footSetting.GetIntegerProperty("m_bEnableTracing") > 0;
                            }
                            if (footSetting.ContainsKey("m_flTraceAngleBlend"))
                            {
                                firstFootTraceAngleBlend = footSetting.GetFloatProperty("m_flTraceAngleBlend");
                            }
                        }

                        items.Add(item);
                    }

                    if (items.Count > 0)
                    {
                        node.Add("m_items", MakeArray(items));
                    }

                    if (!node.ContainsKey("m_bEnableGroundTracing"))
                    {
                        node.Add("m_bEnableGroundTracing", firstFootHasGroundTracing);
                    }
                    if (!node.ContainsKey("m_flTraceAngleBlend"))
                    {
                        node.Add("m_flTraceAngleBlend", firstFootTraceAngleBlend);
                    }

                    continue;
                }
            }
            else if (className == "CTwoBoneIK")
            {
                if (key == "m_name")
                {
                    var nameValue = value.ToString() ?? "Unnamed";
                    node.Add("m_sName", nameValue);
                    continue;
                }
                else if (key == "m_opFixedData")
                {
                    var opFixedData = value;
                    var foundChainName = "";

                    if (opFixedData.ContainsKey("m_nFixedBoneIndex") &&
                        opFixedData.ContainsKey("m_nMiddleBoneIndex") &&
                        opFixedData.ContainsKey("m_nEndBoneIndex"))
                    {
                        foundChainName = GetIKChainNameByBoneIndices(
                            (int)opFixedData.GetIntegerProperty("m_nFixedBoneIndex"),
                            (int)opFixedData.GetIntegerProperty("m_nMiddleBoneIndex"),
                            (int)opFixedData.GetIntegerProperty("m_nEndBoneIndex"));
                    }
                    node.Add("m_ikChainName", foundChainName);

                    node.Add("m_bAutoDetectHingeAxis", !opFixedData.ContainsKey("m_bAlwaysUseFallbackHinge")
                        || opFixedData.GetIntegerProperty("m_bAlwaysUseFallbackHinge") == 0);

                    CopyIfPresent(opFixedData, node, "m_endEffectorType");

                    if (opFixedData.ContainsKey("m_endEffectorAttachment"))
                    {
                        node.Add("m_endEffectorAttachmentName",
                            FindMatchingAttachmentName(opFixedData.GetSubCollection("m_endEffectorAttachment")));
                    }

                    CopyIfPresent(opFixedData, node, "m_targetType");

                    if (opFixedData.ContainsKey("m_targetAttachment"))
                    {
                        node.Add("m_attachmentName",
                            FindMatchingAttachmentName(opFixedData.GetSubCollection("m_targetAttachment")));
                    }

                    if (opFixedData.ContainsKey("m_targetBoneIndex"))
                    {
                        var targetBoneIndex = (int)opFixedData.GetIntegerProperty("m_targetBoneIndex");
                        node.Add("m_targetBoneName", targetBoneIndex != -1 ? GetBoneName(targetBoneIndex) : "");
                    }

                    if (opFixedData.ContainsKey("m_hPositionParam"))
                    {
                        node.Add("m_targetParam", ExtractParameterID(opFixedData.GetSubCollection("m_hPositionParam")));
                    }

                    CopyBoolIfPresent(opFixedData, node, "m_bMatchTargetOrientation");

                    if (opFixedData.ContainsKey("m_hRotationParam"))
                    {
                        node.Add("m_rotationParam", ExtractParameterID(opFixedData.GetSubCollection("m_hRotationParam")));
                    }

                    CopyBoolIfPresent(opFixedData, node, "m_bConstrainTwist");
                    CopyIfPresent(opFixedData, node, "m_flMaxTwist");

                    continue;
                }
            }
            else if (className == "CSingleFrame")
            {
                if (key == "m_name")
                {
                    var nameValue = value.ToString() ?? "Unnamed";
                    node.Add("m_sName", nameValue);

                    var colonIndex = nameValue.LastIndexOf(':');
                    if (colonIndex != -1)
                    {
                        var frameIndexStr = nameValue[(colonIndex + 1)..].Trim();
                        if (int.TryParse(frameIndexStr, out var frameIndex))
                        {
                            node.Add("m_nFrameIndex", frameIndex);
                        }
                    }
                    continue;
                }
                else if (key == "m_hPoseCacheHandle")
                {
                    node.Add("m_eFrameSelection", "SpecificFrame");
                    continue;
                }
                else if (key == "m_flCycle")
                {
                    continue;
                }
            }
            else if (className == "CMotionMatching")
            {
                if (key == "m_name")
                {
                    var nameValue = value.ToString() ?? "Unnamed";
                    node.Add("m_sName", nameValue);
                    continue;
                }
                else if (key == "m_dataSet")
                {
                    var dataSet = value;

                    if (dataSet.ContainsKey("m_groups"))
                    {
                        var compiledGroups = dataSet.GetArray("m_groups");
                        var groups = new List<KVObject>();

                        foreach (var compiledGroup in compiledGroups)
                        {
                            var group = MakeNode("CMotionItemGroup");
                            group.Add("m_name", compiledGroup.GetStringProperty("m_name") ?? string.Empty);

                            if (compiledGroup.ContainsKey("m_motionGraphs"))
                            {
                                var motionGraphs = compiledGroup.GetArray("m_motionGraphs");
                                var motions = new List<KVObject>();

                                foreach (var motionGraph in motionGraphs)
                                {
                                    var motion = MakeNode("CGraphMotionItem");
                                    motion.Add("m_name", "New Graph");
                                    motion.Add("m_bLoop", motionGraph.GetIntegerProperty("m_bLoop") > 0);

                                    var configCount = motionGraph.GetIntegerProperty("m_nConfigCount");
                                    var sampleCount = Math.Max(0, (int)configCount - 1);

                                    var paramManager = MakeNode("CMotionParameterManager");
                                    var motionParams = new List<KVObject>();
                                    var motionParamIds = new List<long>();
                                    var motionParamIdSet = new HashSet<long>();
                                    var motionParamCursor = GeneratedNodeIdMin;

                                    if (compiledGroup.ContainsKey("m_motionGraphConfigs"))
                                    {
                                        var configs = compiledGroup.GetArray("m_motionGraphConfigs");
                                        var parameterCount = motionGraph.GetIntegerProperty("m_nParameterCount");

                                        for (var paramIdx = 0; paramIdx < parameterCount; paramIdx++)
                                        {
                                            var motionParam = MakeNode("CMotionParameter");
                                            motionParam.Add("m_name", paramIdx.ToString(CultureInfo.InvariantCulture));

                                            var paramId = GenerateNewNodeId(motionParamIdSet, ref motionParamCursor);
                                            motionParamIds.Add(paramId);
                                            motionParamIdSet.Add(paramId);
                                            motionParam.Add("m_id", MakeNodeIdObjectValue(paramId));

                                            var minValue = float.MaxValue;
                                            var maxValue = float.MinValue;

                                            foreach (var config in configs)
                                            {
                                                var paramValues = config.GetFloatArray("m_paramValues");
                                                if (paramIdx < paramValues.Length)
                                                {
                                                    var paramValue = paramValues[paramIdx];
                                                    minValue = Math.Min(minValue, paramValue);
                                                    maxValue = Math.Max(maxValue, paramValue);
                                                }
                                            }

                                            if (minValue == float.MaxValue)
                                            {
                                                minValue = -180.0f;
                                                maxValue = 180.0f;
                                            }

                                            motionParam.Add("m_flMinValue", minValue);
                                            motionParam.Add("m_flMaxValue", maxValue);
                                            motionParam.Add("m_nSamples", sampleCount);

                                            motionParams.Add(motionParam);
                                        }
                                    }

                                    paramManager.Add("m_params", MakeArray(motionParams));
                                    motion.Add("m_paramManager", paramManager);

                                    if (motionGraph.ContainsKey("m_pRootNode"))
                                    {
                                        var rootNode = motionGraph.GetSubCollection("m_pRootNode");
                                        var nodeManager = MakeNode("CMotionNodeManager");

                                        var motionNodes = ConvertMotionNodeHierarchy(rootNode, motionParamIds);
                                        nodeManager.Add("m_nodes", MakeArray(motionNodes));

                                        motion.Add("m_nodeManager", nodeManager);
                                    }

                                    motion.Add("m_tagSpans", motionGraph.ContainsKey("m_tags")
                                        ? ConvertTagSpansArray(motionGraph.GetArray("m_tags"))
                                        : KVObject.Array());

                                    motion.Add("m_paramSpans", motionGraph.ContainsKey("m_paramSpans")
                                        ? ConvertParamSpans(motionGraph.GetSubCollection("m_paramSpans"))
                                        : KVObject.Array());

                                    motions.Add(motion);
                                }

                                group.Add("m_motions", MakeArray(motions));
                            }

                            if (compiledGroup.ContainsKey("m_hIsActiveScript"))
                            {
                                var scriptCode = TryGetFuseGeneralScriptCode(
                                    compiledGroup.GetSubCollection("m_hIsActiveScript").GetIntegerProperty("m_id"));
                                if (scriptCode != null)
                                {
                                    var conditions = ParseConditionExpression(scriptCode);
                                    if (conditions != null && conditions.Count > 0)
                                    {
                                        var conditionContainer = MakeNode("CConditionContainer");
                                        conditionContainer.Add("m_conditions", MakeArray(conditions));
                                        group.Add("m_conditions", conditionContainer);
                                    }
                                }
                            }

                            groups.Add(group);
                        }

                        node.Add("m_groups", MakeArray(groups));
                    }
                    continue;
                }
                else if (key == "m_metrics")
                {
                    var compiledMetrics = compiledNode.GetArray("m_metrics");
                    var metrics = new List<KVObject>();

                    foreach (var compiledMetric in compiledMetrics)
                    {
                        var metricClassName = compiledMetric.GetStringProperty("_class");
                        var uncompiledClassName = metricClassName.Replace("MetricEvaluator", "Metric", StringComparison.Ordinal);
                        var metric = MakeNode(uncompiledClassName);

                        metric.Add("m_flWeight", compiledMetric.GetFloatProperty("m_flWeight"));

                        if (metricClassName == "CBonePositionMetricEvaluator" || metricClassName == "CBoneVelocityMetricEvaluator")
                        {
                            var boneIndex = compiledMetric.GetIntegerProperty("m_nBoneIndex");
                            var boneName = GetBoneName((int)boneIndex);
                            metric.Add("m_boneName", boneName);
                        }
                        else if (metricClassName == "CFutureVelocityMetricEvaluator")
                        {
                            metric.Add("m_flDistance", compiledMetric.GetFloatProperty("m_flDistance"));
                            metric.Add("m_flStoppingDistance", compiledMetric.GetFloatProperty("m_flStoppingDistance"));
                            metric.Add("m_eMode", compiledMetric.GetStringProperty("m_eMode"));
                            metric.Add("m_bAutoTargetSpeed", compiledMetric.GetIntegerProperty("m_bAutoTargetSpeed") > 0);
                            metric.Add("m_flManualTargetSpeed", compiledMetric.GetFloatProperty("m_flManualTargetSpeed"));
                        }
                        else if (metricClassName == "CDistanceRemainingMetricEvaluator")
                        {
                            metric.Add("m_flMaxDistance", compiledMetric.GetFloatProperty("m_flMaxDistance"));
                            metric.Add("m_flMinDistance", compiledMetric.GetFloatProperty("m_flMinDistance"));
                            metric.Add("m_flStartGoalFilterDistance", compiledMetric.GetFloatProperty("m_flStartGoalFilterDistance"));
                            metric.Add("m_flMaxGoalOvershootScale", compiledMetric.GetFloatProperty("m_flMaxGoalOvershootScale"));
                            metric.Add("m_bFilterFixedMinDistance", compiledMetric.GetIntegerProperty("m_bFilterFixedMinDistance") > 0);
                            metric.Add("m_bFilterGoalDistance", compiledMetric.GetIntegerProperty("m_bFilterGoalDistance") > 0);
                            metric.Add("m_bFilterGoalOvershoot", compiledMetric.GetIntegerProperty("m_bFilterGoalOvershoot") > 0);
                        }
                        else if (metricClassName == "CTimeRemainingMetricEvaluator")
                        {
                            metric.Add("m_bMatchByTimeRemaining", compiledMetric.GetIntegerProperty("m_bMatchByTimeRemaining") > 0);
                            metric.Add("m_flMaxTimeRemaining", compiledMetric.GetFloatProperty("m_flMaxTimeRemaining"));
                            metric.Add("m_bFilterByTimeRemaining", compiledMetric.GetIntegerProperty("m_bFilterByTimeRemaining") > 0);
                            metric.Add("m_flMinTimeRemaining", compiledMetric.GetFloatProperty("m_flMinTimeRemaining"));
                        }
                        else if (metricClassName == "CPathMetricEvaluator")
                        {
                            metric.Add("m_flDistance", compiledMetric.GetFloatProperty("m_flDistance"));

                            if (compiledMetric.ContainsKey("m_pathTimeSamples"))
                            {
                                var timeSamples = compiledMetric.GetFloatArray("m_pathTimeSamples");
                                var pathSamplesArray = KVObject.Array();
                                foreach (var sample in timeSamples)
                                {
                                    pathSamplesArray.Add((float)sample);
                                }
                                metric.Add("m_pathSamples", pathSamplesArray);
                            }
                            metric.Add("m_bExtrapolateMovement", compiledMetric.GetIntegerProperty("m_bExtrapolateMovement") > 0);
                            metric.Add("m_flMinExtrapolationSpeed", compiledMetric.GetFloatProperty("m_flMinExtrapolationSpeed"));
                        }
                        else if (metricClassName == "CStepsRemainingMetricEvaluator")
                        {
                            metric.Add("m_flMinStepsRemaining", compiledMetric.GetFloatProperty("m_flMinStepsRemaining"));
                            AddFeetProperty(metric, compiledMetric);
                        }
                        else if (metricClassName == "CFootPositionMetricEvaluator")
                        {
                            metric.Add("m_bIgnoreSlope", compiledMetric.GetIntegerProperty("m_bIgnoreSlope") > 0);
                            AddFeetProperty(metric, compiledMetric);
                        }
                        else if (metricClassName == "CFutureFacingMetricEvaluator")
                        {
                            metric.Add("m_flDistance", compiledMetric.GetFloatProperty("m_flDistance"));
                            metric.Add("m_flTime", compiledMetric.GetFloatProperty("m_flTime"));
                        }
                        else if (metricClassName == "CFootCycleMetricEvaluator")
                        {
                            AddFeetProperty(metric, compiledMetric);
                        }
                        else if (metricClassName == "CCurrentRotationVelocityMetricEvaluator"
                        || metricClassName == "CCurrentVelocityMetricEvaluator"
                        || metricClassName == "CBlockSelectionMetricEvaluator")
                        {
                        }
                        metrics.Add(metric);
                    }

                    node.Add("m_metrics", MakeArray(metrics));
                    continue;
                }
            }
            else if (className == "CAimCamera")
            {
                if (key == "m_name")
                {
                    node.Add("m_sName", value.ToString() ?? "Unnamed");
                    continue;
                }
                else if (key == "m_opFixedSettings")
                {
                    var opFixedSettings = value;

                    if (opFixedSettings.ContainsKey("m_nChainIndex"))
                    {
                        node.Add("m_ikChain", GetIKChainName((int)opFixedSettings.GetIntegerProperty("m_nChainIndex")));
                    }

                    (string compiledKey, string sourceKey)[] boneProperties =
                    [
                        ("m_nCameraJointIndex", "m_cameraJointName"),
                        ("m_nPelvisJointIndex", "m_pelvisJointName"),
                        ("m_nClavicleLeftJointIndex", "m_clavicleLeftJointName"),
                        ("m_nClavicleRightJointIndex", "m_clavicleRightJointName"),
                        ("m_nDepenetrationJointIndex", "m_depenetrationJointName"),
                    ];

                    foreach (var (compiledKey, sourceKey) in boneProperties)
                    {
                        if (opFixedSettings.ContainsKey(compiledKey))
                        {
                            node.Add(sourceKey, GetBoneName((int)opFixedSettings.GetIntegerProperty(compiledKey)));
                        }
                    }

                    if (opFixedSettings.ContainsKey("m_propJoints"))
                    {
                        var propJoints = opFixedSettings.GetIntegerArray("m_propJoints").Select(jointIndex =>
                        {
                            var propJoint = new KVObject();
                            propJoint.Add("m_jointName", GetBoneName((int)jointIndex));
                            return propJoint;
                        }).ToArray();
                        node.Add("m_propJoints", MakeArray(propJoints));
                    }
                    continue;
                }
            }
            else if (className == "CTargetWarp")
            {
                if (key == "m_name")
                {
                    node.Add("m_sName", value.ToString() ?? "Unnamed");
                    continue;
                }
                if (!node.ContainsKey("m_eLinearRootMotionMode"))
                {
                    node.Add("m_eLinearRootMotionMode", "TargetWarpLinearRootMotionMode_Default");
                }
            }
            else if (className == "COrientationWarp")
            {
                if (key == "m_name")
                {
                    node.Add("m_sName", value.ToString() ?? "Unnamed");
                    continue;
                }
            }
            else if (className == "CPairedSequence")
            {
                if (key == "m_name")
                {
                    node.Add("m_sName", value.ToString() ?? "Unnamed");
                    continue;
                }
                if (!node.ContainsKey("m_previewSequenceName"))
                {
                    node.Add("m_previewSequenceName", "");
                }
            }
            else if (className == "CFollowTarget")
            {
                if (key == "m_name")
                {
                    node.Add("m_sName", value.ToString() ?? "Unnamed");
                    continue;
                }
                else if (key == "m_opFixedData")
                {
                    var opFixedData = value;

                    if (opFixedData.ContainsKey("m_boneIndex"))
                    {
                        node.Add("m_boneName", GetBoneName((int)opFixedData.GetIntegerProperty("m_boneIndex")));
                    }

                    var targetSettings = new KVObject();
                    targetSettings.Add("m_TargetSource",
                        opFixedData.GetIntegerProperty("m_bBoneTarget", 0) > 0 ? "Bone" : "AnimgraphParameter");

                    var boneNameAndIndex = new KVObject();
                    if (opFixedData.ContainsKey("m_boneTargetIndex"))
                    {
                        var boneTargetIndex = (int)opFixedData.GetIntegerProperty("m_boneTargetIndex");
                        if (boneTargetIndex != -1)
                        {
                            boneNameAndIndex.Add("m_Name", GetBoneName(boneTargetIndex));
                        }
                    }
                    targetSettings.Add("m_Bone", boneNameAndIndex);

                    targetSettings.Add("m_TargetCoordSystem",
                        opFixedData.GetIntegerProperty("m_bWorldCoodinateTarget", 0) > 0 ? "World" : "Model");

                    node.Add("m_TargetSettings", targetSettings);

                    CopyBoolIfPresent(opFixedData, node, "m_bMatchTargetOrientation");
                    continue;
                }
                else if (key is "m_hParameterPosition" or "m_hParameterOrientation")
                {
                    if (!node.ContainsKey("m_TargetSettings"))
                    {
                        var targetSettings = new KVObject();
                        targetSettings.Add("m_TargetSource", "AnimgraphParameter");
                        targetSettings.Add("m_Bone", KVObject.Null());
                        targetSettings.Add("m_TargetCoordSystem", "Model");
                        node.Add("m_TargetSettings", targetSettings);
                    }

                    var destKey = key == "m_hParameterPosition"
                        ? "m_AnimgraphParameterNamePosition"
                        : "m_AnimgraphParameterNameOrientation";
                    node.GetSubCollection("m_TargetSettings").Add(destKey, ExtractParameterID(value));
                    continue;
                }
            }
            if (key == "m_children")
            {
                if (inputNodeIds is not null)
                {
                    node.Add(key, MakeArray(inputNodeIds.Select(MakeInputConnection)));
                }
                continue;
            }
            if (key == "m_tags")
            {
                if (className is "CSequence" or "CCycleControlClip" or "CBlend2D")
                {
                    try
                    {
                        node.Add("m_tagSpans", ConvertTagSpansArray(compiledNode.GetArray("m_tags")));
                    }
                    catch
                    {
                        node.Add("m_tagSpans", KVObject.Array());
                    }
                    continue;
                }
                else if (className == "CSelector")
                {
                    try
                    {
                        var tagIndices = compiledNode.GetIntegerArray(key);
                        node.Add(key, ConvertTagIndicesArray(tagIndices));
                    }
                    catch (InvalidCastException)
                    {
                        node.Add(key, KVObject.Array());
                    }
                    continue;
                }
                else
                {
                    try
                    {
                        var tagIds = compiledNode.GetIntegerArray(key);
                        node.Add(key, MakeArray(tagIds.Select(MakeNodeIdObjectValue)));
                        continue;
                    }
                    catch (InvalidCastException)
                    {
                        continue;
                    }
                }
            }

            if (key == "m_paramSpans")
            {
                try
                {
                    var compiledParamSpans = compiledNode.GetSubCollection("m_paramSpans");
                    if (compiledParamSpans?.ContainsKey("m_spans") == true)
                    {
                        node.Add("m_paramSpans", ConvertParamSpans(compiledParamSpans));
                    }
                }
                catch
                {
                    node.Add("m_paramSpans", KVObject.Array());
                }
                continue;
            }

            if (key is "m_paramIndex" or "m_hParam")
            {
                node.Add("m_param", ExtractParameterID(value));
                continue;
            }

            node.Add(newKey, value);
        }
        if (className == "CStateMachine")
        {
            var stateMachine = compiledNode.GetSubCollection("m_stateMachine");
            var stateData = compiledNode.GetArray("m_stateData");
            var transitionData = compiledNode.GetArray("m_transitionData");

            var states = ConvertStateMachine(stateMachine, stateData, transitionData, isComponent: false);
            node.Add("m_states", MakeArray(states));
        }
        else if (className == "CFootPinning" && !node.ContainsKey("m_items") && footPinningItems is { Count: > 0 })
        {
            node.Add("m_items", MakeArray(footPinningItems));
        }
        return node;
    }

    private static string ClassNameToParamType(string paramClass) => paramClass switch
    {
        "CFloatAnimParameter" => "FLOAT",
        "CEnumAnimParameter" => "ENUM",
        "CBoolAnimParameter" => "BOOL",
        "CIntAnimParameter" => "INT",
        "CVectorAnimParameter" => "VECTOR",
        "CQuaternionAnimParameter" => "QUATERNION",
        "CSymbolAnimParameter" => "SYMBOL",
        "CVirtualAnimParameter" => "VIRTUAL",
        _ => paramClass.TrimStart('C').Replace("AnimParameter", "", StringComparison.Ordinal).ToUpperInvariant(),
    };

    private static long GetParameterId(KVObject parameter)
        => parameter.GetSubCollection("m_id").GetIntegerProperty("m_id");

    // Indexed by uncompiled type (e.g. "FLOAT"); each list is the ordered sequence of parameter ids of that type.
    private Dictionary<string, List<long>>? parameterIdsByType;
    private long? firstFloatParameterId;

    private void BuildParameterIndex()
    {
        parameterIdsByType = [];
        foreach (var parameter in Parameters)
        {
            var paramClass = parameter.GetStringProperty("_class");
            var type = ClassNameToParamType(paramClass);
            if (!parameterIdsByType.TryGetValue(type, out var list))
            {
                list = [];
                parameterIdsByType[type] = list;
            }
            list.Add(GetParameterId(parameter));

            if (firstFloatParameterId is null && paramClass == "CFloatAnimParameter")
            {
                firstFloatParameterId = GetParameterId(parameter);
            }
        }
    }

    private KVObject ParameterIDFromIndex(string paramType, long paramIndex, bool requireFloat = false)
    {
        if (paramIndex == 255)
        {
            return MakeNodeIdObjectValue(-1);
        }

        parameterIdsByType ??= BuildAndReturnParameterIndex();

        var uncompiledType = paramType.Replace("ANIMPARAM_", "", StringComparison.Ordinal);
        if (parameterIdsByType.TryGetValue(uncompiledType, out var idsOfType) && paramIndex < idsOfType.Count)
        {
            return MakeNodeIdObjectValue(idsOfType[(int)paramIndex]);
        }

        if (requireFloat && uncompiledType != "FLOAT" && firstFloatParameterId is { } floatId)
        {
            return MakeNodeIdObjectValue(floatId);
        }
        return MakeNodeIdObjectValue(-1);
    }

    private Dictionary<string, List<long>> BuildAndReturnParameterIndex()
    {
        BuildParameterIndex();
        return parameterIdsByType!;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources used by this instance.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            modelResourceCache?.Dispose();
            modelResourceCache = null;
            modelResourceLoaded = false;
        }
    }
}
