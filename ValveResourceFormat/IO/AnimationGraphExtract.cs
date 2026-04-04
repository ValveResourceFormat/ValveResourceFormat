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
    private Dictionary<string, string[]>? modelBoneNamesCache;
    private Dictionary<string, string[]>? modelIKChainNamesCache;
    private Dictionary<string, string[]>? modelFootNamesCache;
    private Dictionary<string, LookAtChainInfo[]>? modelLookAtChainInfoCache;
    private List<KVObject>? footPinningItems;
    private KVObject? scriptManager;

    // cached values derived from m_modelName property so we only load the resource once
    private string? modelNameCache;
    private Resource? modelResourceCache;
    private bool modelResourceLoaded;

    private Resource? ModelResource
    {
        get
        {
            if (!modelResourceLoaded)
            {
                modelResourceLoaded = true;
                modelNameCache ??= Graph.GetStringProperty("m_modelName");

                if (!string.IsNullOrEmpty(modelNameCache))
                {
                    modelResourceCache = fileLoader.LoadFileCompiled(modelNameCache);
                }
            }
            return modelResourceCache;
        }
    }

    private Model? ModelData => ModelResource?.DataBlock as Model;

    // helper enum & mappings to reduce duplication when converting nodes/properties
    private enum PropAction
    {
        Copy,
        Rename,
        ParamRef,
        InputConnection,
        BlendDuration,
        BlendCurve,
        SequenceName,
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
                ["m_nWeightListIndex"] = (PropAction.Skip, null),
            },
            ["CRagdoll"] = new(StringComparer.Ordinal)
            {
                ["m_nWeightListIndex"] = (PropAction.Skip, null),
            },
            ["CSequence"] = new(StringComparer.Ordinal)
            {
                ["m_duration"] = (PropAction.Skip, null),
                ["m_hSequence"] = (PropAction.Skip, null),
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
                ["m_hSequence"] = (PropAction.SequenceName, "m_sequenceName"),
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
            }
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
        Lazy<KVObject> subCollection,
        List<long> outConnections,
        PropAction action,
        string? outputKey)
    {
        var destKey = outputKey ?? key;
        switch (action)
        {
            case PropAction.Copy:
                node.Add(destKey, value);
                break;
            case PropAction.Rename:
                node.Add(destKey, value);
                break;
            case PropAction.ParamRef:
                {
                    node.Add(destKey, ExtractParameterID(subCollection.Value));
                    break;
                }
            case PropAction.InputConnection:
                {
                    var nodeIndex = subCollection.Value.GetIntegerProperty("m_nodeIndex");
                    if (nodeIndexToIdMap?.TryGetValue(nodeIndex, out var nodeId) == true)
                    {
                        outConnections.Add(nodeId);
                        var connection = MakeInputConnection(nodeId);
                        node.Add("m_inputConnection", connection);
                    }
                    break;
                }
            case PropAction.BlendDuration:
                {
                    var converted = ConvertBlendDuration(subCollection.Value);
                    node.Add(destKey, converted);
                    break;
                }
            case PropAction.BlendCurve:
                {
                    node.Add(destKey, MakeBlendCurve(subCollection.Value));
                    break;
                }
            case PropAction.SequenceName:
                {
                    var sequenceIndex = compiledNode.GetIntegerProperty("m_hSequence");
                    var sequenceName = GetSequenceName(sequenceIndex);
                    node.Add(destKey, sequenceName);
                    break;
                }
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
            case PropAction.Skip:
                // intentionally ignore this property
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
        for (var i = 0; i < compiledNodes.Count; i++)
        {
            var compiledNode = compiledNodes[i];
            var nodePath = compiledNode.GetSubCollection("m_nodePath");
            if (nodePath is null)
            {
                var newNodeId = GenerateNewNodeId(assignedNodeIds);
                assignedNodeIds.Add(newNodeId);
                compiledNodeIndexMap[newNodeId] = compiledNode;
                nodeIndexToIdMap[i] = newNodeId;
                continue;
            }

            var path = nodePath.GetArray("m_path");
            var count = nodePath.GetIntegerProperty("m_nCount");

            if (count <= 0 || path is null || path.Count == 0)
            {
                var newNodeId = GenerateNewNodeId(assignedNodeIds);
                assignedNodeIds.Add(newNodeId);
                compiledNodeIndexMap[newNodeId] = compiledNode;
                nodeIndexToIdMap[i] = newNodeId;
                continue;
            }

            long? foundId = null;
            for (var j = (int)count - 1; j >= 0; j--)
            {
                var id = path[j].GetIntegerProperty("m_id");
                if (id != uint.MaxValue)
                {
                    foundId = id;
                    break;
                }
            }

            if (foundId.HasValue)
            {
                var nodeId = foundId.Value;

                if (assignedNodeIds.Contains(nodeId))
                {
                    nodeId = GenerateNewNodeId(assignedNodeIds);
                }

                assignedNodeIds.Add(nodeId);
                compiledNodeIndexMap[nodeId] = compiledNode;
                nodeIndexToIdMap[i] = nodeId;
            }
            else
            {
                var newNodeId = GenerateNewNodeId(assignedNodeIds);
                assignedNodeIds.Add(newNodeId);
                compiledNodeIndexMap[newNodeId] = compiledNode;
                nodeIndexToIdMap[i] = newNodeId;
            }
        }
    }

    private static long GenerateNewNodeId(HashSet<long> assignedNodeIds)
    {
        const long MinId = 100_000_000L;
        const long MaxId = 999_999_999L;

        for (var candidate = MinId; candidate <= MaxId; candidate++)
        {
            if (!assignedNodeIds.Contains(candidate))
            {
                return candidate;
            }
        }

        // As a last resort (should never happen) just return MinId.
        return MinId;
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

    private void LoadModelData()
    {
        if (modelAttachments != null)
        {
            return;
        }

        var modelData = ModelData;
        if (modelData != null)
        {
            modelAttachments = modelData.Attachments;
        }
        else
        {
            modelAttachments = [];
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
        var modelName = Graph.GetStringProperty("m_modelName");
        if (string.IsNullOrEmpty(modelName))
        {
            return [];
        }
        if (modelBoneNamesCache?.TryGetValue(modelName, out var cached) == true)
        {
            return cached;
        }
        var boneNames = new List<string>();
        try
        {
            var modelData = ModelData;
            if (modelData != null)
            {
                var skeleton = modelData.Skeleton;
                foreach (var bone in skeleton.Bones)
                {
                    boneNames.Add(bone.Name);
                }
            }
        }
        catch (Exception)
        {
            return [];
        }
        modelBoneNamesCache ??= [];
        modelBoneNamesCache[modelName] = [.. boneNames];
        return [.. boneNames];
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
        var modelName = Graph.GetStringProperty("m_modelName");
        if (string.IsNullOrEmpty(modelName))
        {
            return [];
        }
        if (modelIKChainNamesCache?.TryGetValue(modelName, out var cached) == true)
        {
            return cached;
        }
        var ikChainNames = new List<string>();
        try
        {
            var ikChains = GetIKChainsFromModel(ModelData);
            if (ikChains is not null)
            {
                foreach (var chain in ikChains)
                {
                    var name = chain.GetStringProperty("m_Name");
                    if (!string.IsNullOrEmpty(name))
                    {
                        ikChainNames.Add(name);
                    }
                }
            }
        }
        catch (Exception)
        {
            return [];
        }
        modelIKChainNamesCache ??= [];
        modelIKChainNamesCache[modelName] = [.. ikChainNames];
        return [.. ikChainNames];
    }

    private Dictionary<string, List<string>> LoadIKChainBonesFromModel()
    {
        var chainBones = new Dictionary<string, List<string>>();
        var modelName = Graph.GetStringProperty("m_modelName");
        if (string.IsNullOrEmpty(modelName))
        {
            return chainBones;
        }

        try
        {
            var ikChains = GetIKChainsFromModel(ModelData);
            if (ikChains is not null)
            {
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
                        var joints = chain.GetArray("m_Joints");
                        foreach (var joint in joints)
                        {
                            if (joint.ContainsKey("m_Bone"))
                            {
                                var bone = joint.GetSubCollection("m_Bone");
                                var boneName = bone.GetStringProperty("m_Name");
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
        }
        catch (Exception)
        {
            return chainBones;
        }
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

        var chainBones = LoadIKChainBonesFromModel();
        foreach (var (chainName, bones) in chainBones)
        {
            if (bones.Count == 3)
            {
                if (bones[0] == fixedBoneName && bones[1] == middleBoneName && bones[2] == endBoneName)
                {
                    return chainName;
                }
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
        var modelName = Graph.GetStringProperty("m_modelName");
        if (string.IsNullOrEmpty(modelName))
        {
            return [];
        }
        if (modelFootNamesCache?.TryGetValue(modelName, out var cached) == true)
        {
            return cached;
        }
        var footNames = new List<string>();
        try
        {
            var modelData = ModelData;
            if (modelData is not null)
            {
                var keyvalues = modelData.KeyValues;
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
            }
        }
        catch (Exception)
        {
            return [];
        }

        modelFootNamesCache ??= [];
        modelFootNamesCache[modelName] = [.. footNames];

        return [.. footNames];
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
        var modelName = Graph.GetStringProperty("m_modelName");
        if (string.IsNullOrEmpty(modelName))
        {
            return [];
        }
        if (modelLookAtChainInfoCache?.TryGetValue(modelName, out var cached) == true)
        {
            return cached;
        }
        var lookAtChains = new List<LookAtChainInfo>();
        try
        {
            var modelData = ModelData;
            if (modelData is not null)
            {
                var keyvalues = modelData.KeyValues;
                if (keyvalues.ContainsKey("LookAtList"))
                {
                    var lookAtList = keyvalues.GetSubCollection("LookAtList");
                    foreach (var (_, chainEntryValue) in lookAtList.Children)
                    {
                        if (chainEntryValue.ValueType != KVValueType.Collection)
                        {
                            continue;
                        }
                        var chain = new LookAtChainInfo
                        {
                            Name = chainEntryValue.GetStringProperty("name")
                        };
                        if (chainEntryValue.ContainsKey("bones"))
                        {
                            var bones = chainEntryValue.GetArray("bones");
                            var boneNames = new List<string>();
                            var boneWeights = new List<float>();

                            foreach (var bone in bones)
                            {
                                boneNames.Add(bone.GetStringProperty("name"));
                                boneWeights.Add(bone.GetFloatProperty("weight"));
                            }

                            chain.BoneNames = [.. boneNames];
                            chain.BoneWeights = [.. boneWeights];
                        }
                        lookAtChains.Add(chain);
                    }
                }
            }
        }
        catch (Exception)
        {
            return [];
        }
        modelLookAtChainInfoCache ??= [];
        modelLookAtChainInfoCache[modelName] = [.. lookAtChains];
        return [.. lookAtChains];
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
            if (chain.BoneNames.Length == compiledBoneNames.Length)
            {
                var match = true;
                for (var i = 0; i < chain.BoneNames.Length; i++)
                {
                    if (chain.BoneNames[i] != compiledBoneNames[i])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    return chain.Name;
                }
            }
        }
        foreach (var chain in lookAtChains)
        {
            var chainSet = new HashSet<string>(chain.BoneNames);
            var compiledSet = new HashSet<string>(compiledBoneNames);

            if (chainSet.SetEquals(compiledSet))
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
        LoadModelData();

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
            var nodeManagerItem = new KVObject();
            nodeManagerItem.Add("key", MakeNodeIdObjectValue(nodeId));
            nodeManagerItem.Add("value", nodeData);
            nodeManager.Children.Add(nodeManagerItem);
        }

        var localParameters = MakeArray(Parameters);
        var localTags = MakeArray(Tags);
        var componentManager = MakeNode("CAnimComponentManager");
        componentManager.Add("m_components", MakeArray(componentList.ToArray()));

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
        var modelName = Graph.GetStringProperty("m_modelName");

        if (string.IsNullOrEmpty(modelName))
        {
            weightListNames[0] = "default";
            return weightListNames;
        }

        try
        {
            var modelResource = ModelResource;
            if (modelResource is not null)
            {
                var aseqData = GetAseqDataFromResource(modelResource);
                if (aseqData is not null)
                {
                    var localBoneMaskArray = aseqData.GetArray("m_localBoneMaskArray");
                    if (localBoneMaskArray is not null && localBoneMaskArray.Count > 0)
                    {
                        for (var i = 0; i < localBoneMaskArray.Count; i++)
                        {
                            var boneMask = localBoneMaskArray[i];
                            var weightListName = boneMask.GetStringProperty("m_sName");
                            weightListNames[i] = !string.IsNullOrEmpty(weightListName)
                                ? weightListName
                                : i == 0 ? "default" : $"weightlist_{i}";
                        }
                    }
                }
            }
        }
        catch
        {
            // If loading fails, ensure we have a default entry
        }

        if (!weightListNames.ContainsKey(0))
        {
            weightListNames[0] = "default";
        }

        return weightListNames;
    }
    private Dictionary<int, string> LoadSequenceNamesFromModel()
    {
        var sequenceNames = new Dictionary<int, string>();
        var modelName = Graph.GetStringProperty("m_modelName");
        if (string.IsNullOrEmpty(modelName))
        {
            return sequenceNames;
        }
        try
        {
            var modelResource = ModelResource;
            if (modelResource is null)
            {
                return sequenceNames;
            }
            var index = 0;
            var aseqData = GetAseqDataFromResource(modelResource);
            if (aseqData is not null)
            {
                var localSequenceNameArray = aseqData.GetArray<string>("m_localSequenceNameArray");
                if (localSequenceNameArray is not null)
                {
                    for (var i = 0; i < localSequenceNameArray.Length; i++)
                    {
                        var sequenceName = localSequenceNameArray[i];
                        if (!string.IsNullOrEmpty(sequenceName))
                        {
                            sequenceNames[index] = sequenceName;
                            index++;
                        }
                    }
                }
            }
            if (modelResource.DataBlock is Model modelData)
            {
                var referencedAnimations = modelData.GetReferencedAnimations(fileLoader);
                foreach (var animation in referencedAnimations)
                {
                    if (!string.IsNullOrEmpty(animation.Name))
                    {
                        sequenceNames[index] = animation.Name;
                        index++;
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

        var directionalSuffixes = new string[]
        {
        "_n", "_nw", "_w", "_sw", "_s", "_se", "_e", "_ne",
        "_N", "_NW", "_W", "_SW", "_S", "_SE", "_E", "_NE"
        };
        foreach (var seq in sequenceNames)
        {
            foreach (var suffix in directionalSuffixes)
            {
                if (seq.EndsWith(suffix, StringComparison.Ordinal))
                {
                    var candidatePrefix = seq[..^suffix.Length];

                    if (sequenceNames.All(s => s.StartsWith(candidatePrefix, StringComparison.Ordinal)))
                    {
                        var allMatch = true;
                        foreach (var otherSeq in sequenceNames)
                        {
                            var remaining = otherSeq[candidatePrefix.Length..];
                            if (!directionalSuffixes.Contains(remaining))
                            {
                                allMatch = false;
                                break;
                            }
                        }
                        if (allMatch)
                        {
                            return candidatePrefix;
                        }
                    }
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

    private static void AddInputConnection(KVObject node, long childNodeId)
    {
        var inputConnection = MakeInputConnection(childNodeId);
        node.Add("m_inputConnection", inputConnection);
    }

    private static KVObject MakeVector2(float x, float y)
    {
        var values = new object[] { x, y };
        return MakeArray(values.Select(v => (KVObject)(float)v).ToArray());
    }

    private static KVObject MakeBlendCurve(KVObject? compiledCurve)
    {
        var blendCurve = MakeNode("CBlendCurve");
        blendCurve.Add("m_flControlPoint1",
            compiledCurve?.ContainsKey("m_flControlPoint1") == true
                ? compiledCurve.GetFloatProperty("m_flControlPoint1")
                : 0.0f);
        blendCurve.Add("m_flControlPoint2",
            compiledCurve?.ContainsKey("m_flControlPoint2") == true
                ? compiledCurve.GetFloatProperty("m_flControlPoint2")
                : 1.0f);
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
        return MakeArray(tagSpans.ToArray());
    }

    private KVObject ExtractParameterID(KVObject paramHandle, bool requireFloat = false)
    {
        var paramType = paramHandle.GetStringProperty("m_type");
        var paramIndex = paramHandle.GetIntegerProperty("m_index");
        return ParameterIDFromIndex(paramType, paramIndex, requireFloat);
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

    private KVObject ConvertBlendDuration(KVObject compiledBlendDuration)
    {
        var constValue = compiledBlendDuration.GetFloatProperty("m_constValue");
        var paramRef = compiledBlendDuration.GetSubCollection("m_hParam");
        var paramType = paramRef.GetStringProperty("m_type");
        var paramIndex = paramRef.GetIntegerProperty("m_index");

        var blendDuration = MakeNode("CFloatAnimValue");
        blendDuration.Add("m_flConstValue", constValue);

        var paramIdValue = ParameterIDFromIndex(paramType, paramIndex, requireFloat: true);
        blendDuration.Add("m_paramID", paramIdValue);

        var paramId = paramIdValue.GetIntegerProperty("m_id");
        var source = paramId == uint.MaxValue ? "Constant" : "Parameter";

        blendDuration.Add("m_eSource", source);

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
                        var scriptHandle = compiledState.GetSubCollection("m_hScript");
                        var scriptIndex = scriptHandle.GetIntegerProperty("m_id");

                        if (scriptIndex >= 0 && scriptManager != null)
                        {
                            var scriptConditions = CreateConditionsFromScript(scriptIndex, transitionIndex);
                            if (scriptConditions != null)
                            {
                                conditions.AddRange(scriptConditions);
                            }
                        }
                    }

                    conditionList.Add("m_conditions", MakeArray(conditions.ToArray()));
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
                                var resetOption = transitionData.GetIntegerProperty("m_resetCycleOption");
                                var resetOptionStr = resetOption switch
                                {
                                    0 => "Beginning",
                                    1 => "SameCycleAsSource",
                                    2 => "InverseSourceCycle",
                                    3 => "FixedValue",
                                    4 => "SameTimeAsSource",
                                    _ => "Beginning",
                                };

                                transitionNode.Add("m_resetCycleOption", resetOptionStr);
                            }

                            if (transitionData.ContainsKey("m_blendDuration"))
                            {
                                var blendDuration = transitionData.GetSubCollection("m_blendDuration");

                                if (blendDuration is not null)
                                {
                                    var convertedBlendDuration = ConvertBlendDuration(blendDuration);
                                    transitionNode.Add("m_blendDuration", convertedBlendDuration);
                                }
                            }

                            if (transitionData.ContainsKey("m_resetCycleValue"))
                            {
                                var resetcycleValue = transitionData.GetSubCollection("m_resetCycleValue");

                                if (resetcycleValue is not null)
                                {
                                    var convertedfixedcycleValue = ConvertBlendDuration(resetcycleValue);
                                    transitionNode.Add("m_flFixedCycleValue", convertedfixedcycleValue);
                                }
                            }

                            if (transitionData.ContainsKey("m_curve"))
                            {
                                transitionNode.Add("m_blendCurve", MakeBlendCurve(transitionData.GetSubCollection("m_curve")));
                            }
                            else
                            {
                                transitionNode.Add("m_blendCurve", MakeBlendCurve(null));
                            }
                        }
                    }

                    transitions.Add(transitionNode);
                }

                stateNode.Add("m_transitions", MakeArray(transitions.ToArray()));
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
                            var scriptHandle = compiledActionData.GetSubCollection("m_hScript");
                            var scriptIndex = scriptHandle.GetIntegerProperty("m_id");

                            if (scriptIndex >= 0 && scriptManager != null)
                            {
                                var scriptInfoArray = scriptManager.GetArray("m_scriptInfo");
                                if (scriptIndex < scriptInfoArray.Count)
                                {
                                    var scriptInfo = scriptInfoArray[(int)scriptIndex];
                                    var scriptType = scriptInfo.GetStringProperty("m_eScriptType");
                                    var scriptCode = scriptInfo.GetStringProperty("m_code");

                                    if (scriptType == "ANIMSCRIPT_FUSE_GENERAL" && !string.IsNullOrEmpty(scriptCode))
                                    {
                                        actionData.Add("m_expression", scriptCode);
                                    }
                                }
                            }
                        }

                        if (compiledActionData.ContainsKey("m_nTagIndex"))
                        {
                            var tagIndex = compiledActionData.GetIntegerProperty("m_nTagIndex");
                            actionData.Add("m_tag", MakeNodeIdObjectValue(GetTagIdFromIndex(tagIndex)));
                        }

                        if (compiledActionData.ContainsKey("m_hParam"))
                        {
                            actionData.Add("m_param", ExtractParameterID(compiledActionData.GetSubCollection("m_hParam")));
                        }

                        if (compiledActionData.ContainsKey("m_value"))
                        {
                            actionData.Add("m_value", compiledActionData.GetSubCollection("m_value"));
                        }

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

                stateNode.Add("m_actions", MakeArray(actions.ToArray()));
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
        if (scriptManager == null || !scriptManager.ContainsKey("m_scriptInfo"))
        {
            return null;
        }

        var scriptInfoArray = scriptManager.GetArray("m_scriptInfo");
        if (scriptIndex < 0 || scriptIndex >= scriptInfoArray.Count)
        {
            return null;
        }

        var scriptInfo = scriptInfoArray[(int)scriptIndex];
        var scriptCode = scriptInfo.GetStringProperty("m_code");
        var scriptType = scriptInfo.GetStringProperty("m_eScriptType");

        if (scriptType != "ANIMSCRIPT_FUSE_STATEMACHINE" || string.IsNullOrEmpty(scriptCode))
        {
            return null;
        }

        var conditions = ParseConditionScript(scriptCode, transitionIndex);

        if (conditions == null || conditions.Count == 0)
        {
            return null;
        }

        return [.. conditions];
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

        orCondition.Add("m_conditions", MakeArray(subConditions.ToArray()));
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
        // Try exact match, then with underscores replaced by spaces
        var namesToTry = new[] { paramName, paramName.Replace('_', ' ') };

        // Try case-sensitive first, then case-insensitive
        foreach (var comparison in new[] { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase })
        {
            foreach (var nameToTry in namesToTry)
            {
                for (var i = 0; i < Parameters.Count; i++)
                {
                    var param = Parameters[i];
                    var currentParamName = param.GetStringProperty("m_name");

                    if (string.Equals(currentParamName, nameToTry, comparison))
                    {
                        var paramId = param.GetSubCollection("m_id").GetIntegerProperty("m_id");
                        var currentParamClass = param.GetStringProperty("_class");
                        return (param, paramId, currentParamClass);
                    }
                }
            }
        }

        // Try stripping component suffix (e.g., "param.x" -> "param")
        if (paramName.Contains('.', StringComparison.Ordinal))
        {
            return FindParameterByName(paramName[..paramName.IndexOf('.', StringComparison.Ordinal)]);
        }

        return (null, -1, null);
    }

    private long FindTagIdByName(string tagName)
    {
        // Strip "TAG_" prefix if present
        var strippedName = tagName.StartsWith("TAG_", StringComparison.Ordinal)
            ? tagName["TAG_".Length..]
            : tagName;

        // Generate name variations to try
        var namesToTry = new[]
        {
            tagName,                                    // Original
            strippedName,                               // Without TAG_ prefix
            tagName.Replace('_', ' '),                  // Underscores to spaces
            strippedName.Replace('_', ' '),             // Prefix stripped + spaces
            tagName.Replace(' ', '_'),                  // Spaces to underscores
        }.Distinct().ToList();

        // Try exact matches first (case-sensitive)
        foreach (var nameVariant in namesToTry)
        {
            for (var i = 0; i < Tags.Count; i++)
            {
                var currentTagName = Tags[i].GetStringProperty("m_name");
                if (currentTagName == nameVariant)
                {
                    return Tags[i].GetSubCollection("m_tagID").GetIntegerProperty("m_id");
                }
            }
        }

        // Try case-insensitive matches
        foreach (var nameVariant in namesToTry)
        {
            for (var i = 0; i < Tags.Count; i++)
            {
                var currentTagName = Tags[i].GetStringProperty("m_name");
                if (string.Equals(currentTagName, nameVariant, StringComparison.OrdinalIgnoreCase))
                {
                    return Tags[i].GetSubCollection("m_tagID").GetIntegerProperty("m_id");
                }
            }
        }

        // Try fuzzy alphanumeric-only match as last resort
        var cleanInput = new string([.. strippedName.Where(char.IsLetterOrDigit)]).ToLowerInvariant();
        for (var i = 0; i < Tags.Count; i++)
        {
            var currentTagName = Tags[i].GetStringProperty("m_name");
            var cleanCurrent = new string([.. currentTagName.Where(char.IsLetterOrDigit)]).ToLowerInvariant();

            if (cleanCurrent == cleanInput || cleanCurrent.Contains(cleanInput, StringComparison.Ordinal) || cleanInput.Contains(cleanCurrent, StringComparison.Ordinal))
            {
                return Tags[i].GetSubCollection("m_tagID").GetIntegerProperty("m_id");
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
                            var scriptIndex = actionChildValue.GetIntegerProperty("m_id");

                            if (scriptIndex >= 0 && scriptManager != null)
                            {
                                var scriptInfoArray = scriptManager.GetArray("m_scriptInfo");
                                if (scriptIndex < scriptInfoArray.Count)
                                {
                                    var scriptInfo = scriptInfoArray[(int)scriptIndex];
                                    var scriptType = scriptInfo.GetStringProperty("m_eScriptType");
                                    var scriptCode = scriptInfo.GetStringProperty("m_code");

                                    if (scriptType == "ANIMSCRIPT_FUSE_GENERAL" && !string.IsNullOrEmpty(scriptCode))
                                    {
                                        newAction.Add("m_expression", scriptCode);
                                    }
                                }
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

                component.Add("m_actions", MakeArray(convertedActions.ToArray()));
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

                component.Add("m_weightLists", MakeArray(convertedWeightLists.ToArray()));
            }

            component.Add("m_flSpringFrequencyMin", compiledComponent.GetFloatProperty("m_flSpringFrequencyMin"));
            component.Add("m_flSpringFrequencyMax", compiledComponent.GetFloatProperty("m_flSpringFrequencyMax"));

            if (compiledComponent.ContainsKey("m_flMaxStretch"))
            {
                component.Add("m_flMaxStretch", compiledComponent.GetFloatProperty("m_flMaxStretch"));
            }

            if (compiledComponent.ContainsKey("m_bSolidCollisionAtZeroWeight"))
            {
                component.Add("m_bSolidCollisionAtZeroWeight", compiledComponent.GetIntegerProperty("m_bSolidCollisionAtZeroWeight") > 0);
            }

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
                    var paramIn = item.GetSubCollection("m_hParamIn");
                    var paramOut = item.GetSubCollection("m_hParamOut");
                    var paramInType = paramIn.GetStringProperty("m_type");

                    var valueType = paramInType == "ANIMPARAM_VECTOR" ? "VectorParameter" : "FloatParameter";
                    newItem.Add("m_valueType", valueType);

                    if (valueType == "FloatParameter")
                    {
                        newItem.Add("m_floatParamIn", ExtractParameterID(paramIn));
                        newItem.Add("m_floatParamOut", ExtractParameterID(paramOut));
                        newItem.Add("m_vectorParamIn", MakeNodeIdObjectValue(-1));
                        newItem.Add("m_vectorParamOut", MakeNodeIdObjectValue(-1));
                    }
                    else
                    {
                        newItem.Add("m_floatParamIn", MakeNodeIdObjectValue(-1));
                        newItem.Add("m_floatParamOut", MakeNodeIdObjectValue(-1));
                        newItem.Add("m_vectorParamIn", ExtractParameterID(paramIn));
                        newItem.Add("m_vectorParamOut", ExtractParameterID(paramOut));
                    }

                    newItem.Add("m_damping", item.GetSubCollection("m_damping"));
                    return newItem;
                });

                component.Add("m_items", MakeArray(convertedItems.ToArray()));
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

                    var paramInHandle = item.GetSubCollection("m_hParamIn");
                    var paramOutHandle = item.GetSubCollection("m_hParamOut");

                    var paramInType = paramInHandle.GetStringProperty("m_type");
                    var paramOutType = paramOutHandle.GetStringProperty("m_type");

                    var valueType = (paramInType == "ANIMPARAM_VECTOR" || paramOutType == "ANIMPARAM_VECTOR")
                        ? "VectorParameter"
                        : "FloatParameter";

                    newItem.Add("m_valueType", valueType);

                    if (valueType == "FloatParameter")
                    {
                        newItem.Add("m_floatParamIn", ExtractParameterID(paramInHandle));
                        newItem.Add("m_floatParamOut", ExtractParameterID(paramOutHandle));
                        newItem.Add("m_vectorParamIn", MakeNodeIdObjectValue(-1));
                        newItem.Add("m_vectorParamOut", MakeNodeIdObjectValue(-1));
                    }
                    else
                    {
                        newItem.Add("m_floatParamIn", MakeNodeIdObjectValue(-1));
                        newItem.Add("m_floatParamOut", MakeNodeIdObjectValue(-1));
                        newItem.Add("m_vectorParamIn", ExtractParameterID(paramInHandle));
                        newItem.Add("m_vectorParamOut", ExtractParameterID(paramOutHandle));
                    }

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
                component.Add("m_items", MakeArray(convertedItems.ToArray()));
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
            if (compiledComponent.ContainsKey("m_scriptsToRun"))
            {
                var scriptsArray = compiledComponent.GetArray("m_scriptsToRun");
                var convertedScripts = new List<KVObject>();
                foreach (var script in scriptsArray)
                {
                    var scriptName = script.GetStringProperty("") ?? string.Empty;
                    convertedScripts.Add(scriptName);
                }
                component.Add("m_scriptsToRun", MakeArray(convertedScripts.ToArray()));
            }
            else
            {
                component.Add("m_scriptsToRun", MakeArray(Array.Empty<KVObject>()));
            }
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

                component.Add(childKey, MakeArray(convertedMotors.ToArray()));
                continue;
            }

            component.Add(childKey, childValue);
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

            component.Add("m_paramIDs", MakeArray(paramIDs));
        }

        return component;
    }

    private List<KVObject> ConvertMotionNodeHierarchy(KVObject rootNode, List<long> motionParamIds)
    {
        var nodes = new List<KVObject>();
        var idMap = new Dictionary<long, long>();
        var processedNodeIds = new HashSet<long>();
        var nodesToProcess = new Queue<KVObject>();
        nodesToProcess.Enqueue(rootNode);

        while (nodesToProcess.Count > 0)
        {
            var currentNode = nodesToProcess.Dequeue();
            var compiledNodeId = currentNode.GetSubCollection("m_id").GetIntegerProperty("m_id");

            if (processedNodeIds.Contains(compiledNodeId))
            {
                continue;
            }

            processedNodeIds.Add(compiledNodeId);

            var newId = GenerateNewNodeId([.. idMap.Values]);
            idMap[compiledNodeId] = newId;

            var className = currentNode.GetStringProperty("_class");

            if (className == "CMotionNodeBlend1D")
            {
                if (currentNode.ContainsKey("m_blendItems"))
                {
                    var blendItems = currentNode.GetArray("m_blendItems");
                    foreach (var blendItem in blendItems)
                    {
                        if (blendItem.ContainsKey("m_pChild"))
                        {
                            var childNode = blendItem.GetSubCollection("m_pChild");
                            nodesToProcess.Enqueue(childNode);
                        }
                    }
                }
            }
            else if (className == "CMotionNodeSequence")
            {
            }
            else if (className == "CMotionNode")
            {
                if (currentNode.ContainsKey("m_pChild"))
                {
                    var childNode = currentNode.GetSubCollection("m_pChild");
                    nodesToProcess.Enqueue(childNode);
                }
            }
        }

        processedNodeIds.Clear();
        nodesToProcess.Enqueue(rootNode);

        while (nodesToProcess.Count > 0)
        {
            var currentNode = nodesToProcess.Dequeue();
            var compiledNodeId = currentNode.GetSubCollection("m_id").GetIntegerProperty("m_id");

            if (processedNodeIds.Contains(compiledNodeId))
            {
                continue;
            }

            processedNodeIds.Add(compiledNodeId);

            var newNodeId = idMap[compiledNodeId];

            var uncompiledNode = ConvertMotionNode(currentNode, motionParamIds, idMap);
            uncompiledNode.Add("m_nNodeID", MakeNodeIdObjectValue(newNodeId));

            var nodeItem = new KVObject();
            nodeItem.Add("key", MakeNodeIdObjectValue(newNodeId));
            nodeItem.Add("value", uncompiledNode);
            nodes.Add(nodeItem);

            var className = currentNode.GetStringProperty("_class");

            if (className == "CMotionNodeBlend1D")
            {
                if (currentNode.ContainsKey("m_blendItems"))
                {
                    var blendItems = currentNode.GetArray("m_blendItems");
                    foreach (var blendItem in blendItems)
                    {
                        if (blendItem.ContainsKey("m_pChild"))
                        {
                            var childNode = blendItem.GetSubCollection("m_pChild");
                            nodesToProcess.Enqueue(childNode);
                        }
                    }
                }
            }
            else if (className == "CMotionNodeSequence")
            {
            }
            else if (className == "CMotionNode")
            {
                if (currentNode.ContainsKey("m_pChild"))
                {
                    var childNode = currentNode.GetSubCollection("m_pChild");
                    nodesToProcess.Enqueue(childNode);
                }
            }
        }

        var rootNodeId = rootNode.GetSubCollection("m_id").GetIntegerProperty("m_id");
        var rootNodeNewId = idMap[rootNodeId];
        var rootAnimNodeId = GenerateNewNodeId([.. idMap.Values]);

        var rootAnimNode = MakeNode("CRootAnimNode");
        rootAnimNode.Add("m_sName", "Unnamed");
        rootAnimNode.Add("m_nNodeID", MakeNodeIdObjectValue(rootAnimNodeId));
        rootAnimNode.Add("m_networkMode", "ServerAuthoritative");

        var random = new Random((int)rootAnimNodeId);
        var posX = 400 + random.Next(0, 200);
        var posY = 50 + random.Next(0, 100);
        rootAnimNode.Add("m_vecPosition", MakeVector2(posX, posY));

        var inputConnection = MakeInputConnection(rootNodeNewId);
        rootAnimNode.Add("m_inputConnection", inputConnection);

        var rootAnimNodeItem = new KVObject();
        rootAnimNodeItem.Add("key", MakeNodeIdObjectValue(rootAnimNodeId));
        rootAnimNodeItem.Add("value", rootAnimNode);
        nodes.Add(rootAnimNodeItem);

        return nodes;
    }

    private KVObject CreateSequenceMotionNode(KVObject compiledMotionNode, float posX, float posY)
    {
        var sequenceNode = MakeNode("CSequenceAnimNode");
        sequenceNode.Add("m_sName", compiledMotionNode.GetStringProperty("m_name") ?? "Unnamed");
        sequenceNode.Add("m_vecPosition", MakeVector2(posX, posY));

        if (compiledMotionNode.ContainsKey("m_hSequence"))
        {
            var sequenceIndex = compiledMotionNode.GetIntegerProperty("m_hSequence");
            var sequenceName = GetSequenceName(sequenceIndex);
            sequenceNode.Add("m_sequenceName", sequenceName);
        }
        else
        {
            sequenceNode.Add("m_sequenceName", "");
        }

        var playbackSpeed = compiledMotionNode.ContainsKey("m_flPlaybackSpeed")
            ? compiledMotionNode.GetFloatProperty("m_flPlaybackSpeed")
            : 1.0f;
        sequenceNode.Add("m_playbackSpeed", playbackSpeed);

        sequenceNode.Add("m_bLoop", compiledMotionNode.GetIntegerProperty("m_bLoop") > 0);
        sequenceNode.Add("m_tagSpans", MakeArray(Array.Empty<KVObject>()));
        sequenceNode.Add("m_paramSpans", MakeArray(Array.Empty<KVObject>()));
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

            blendNode.Add("m_children", MakeArray(children.ToArray()));
        }

        blendNode.Add("m_blendValueSource", "Parameter");

        if (compiledMotionNode.ContainsKey("m_nParamIndex"))
        {
            var paramIndex = (int)compiledMotionNode.GetIntegerProperty("m_nParamIndex");

            if (paramIndex >= 0 && paramIndex < motionParamIds.Count)
            {
                var motionParamId = motionParamIds[paramIndex];
                var paramIdValue = MakeNodeIdObjectValue(motionParamId);
                blendNode.Add("m_param", paramIdValue);
            }
            else
            {
                blendNode.Add("m_param", MakeNodeIdObjectValue(-1));
            }
        }
        else
        {
            blendNode.Add("m_param", MakeNodeIdObjectValue(-1));
        }

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
        defaultNode.Add("m_tagSpans", MakeArray(Array.Empty<KVObject>()));
        defaultNode.Add("m_paramSpans", MakeArray(Array.Empty<KVObject>()));
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
            var subCollection = new Lazy<KVObject>(() => value);

            // preserve earlier semantics: always convert blend-time fields
            if (key == "m_flBlendTime")
            {
                var converted = ConvertBlendDuration(subCollection.Value);
                node.Add("m_blendDuration", converted);
                continue;
            }

            // first see whether a mapping exists for this class/key combination
            if (PropertyMappings.TryGetValue(className, out var classMap) && classMap.TryGetValue(key, out var mapEntry))
            {
                if (mapEntry.Action != PropAction.Skip)
                {
                    HandleMappedProperty(node, compiledNode, key, value, subCollection, outConnections, mapEntry.Action, mapEntry.OutputKey);
                    continue;
                }
                // Skip action: fall through to handle in manual code below
            }

            // fallback name->sName conversion for the long list of classes
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

            if (className == "CRoot")
            {
                if (key == "m_pChildNode")
                {
                    TryAddInputConnectionFromRef(node, subCollection.Value);
                    continue;
                }
            }
            else if (className == "CSelector")
            {
                if (key == "m_hParameter")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    var tagIndex = compiledNode.GetIntegerProperty("m_nTagIndex");
                    if (tagIndex != -1 && (paramIndex == 255 || paramType == "ANIMPARAM_UNKNOWN"))
                    {
                        continue;
                    }
                    else if (paramIndex != 255)
                    {
                        var source = paramType["ANIMPARAM_".Length..];
                        source = char.ToUpperInvariant(source[0]) + source[1..].ToLowerInvariant();
                        var selectionSource = paramType switch
                        {
                            "ANIMPARAM_BOOL" => "SelectionSource_Bool",
                            "ANIMPARAM_ENUM" => "SelectionSource_Enum",
                            _ => "SelectionSource_Bool",
                        };
                        if (!node.ContainsKey("m_selectionSource"))
                        {
                            node.Add("m_selectionSource", selectionSource);
                        }
                        node.Add($"m_{source.ToLowerInvariant()}ParamID", ParameterIDFromIndex(paramType, paramIndex));
                    }
                    continue;
                }


            }
            else if (className == "CStateMachine")
            {
                if (key is "m_stateMachine" or "m_stateData" or "m_transitionData")
                {
                    continue;
                }
            }
            else if (className == "CSequence")
            {
                if (key is "m_duration" or "m_hSequence")
                {
                    continue;
                }
                else if (key == "m_name")
                {
                    node.Add("m_sName", value);
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

                        node.Add("m_children", MakeArray(newInputs.ToArray()));
                    }

                    continue;
                }

                if (key is "m_weights" or "m_blendTimes")
                {
                    continue;
                }
            }
            else if (className == "CBoneMask")
            {
                if (key == "m_nWeightListIndex")
                {
                    var weightListIndex = compiledNode.GetIntegerProperty("m_nWeightListIndex");
                    var weightListName = GetWeightListName(weightListIndex);
                    node.Add("m_weightListName", weightListName);
                    continue;
                }
            }
            else if (className == "CRagdoll")
            {
                if (key == "m_nWeightListIndex")
                {
                    var weightListIndex = compiledNode.GetIntegerProperty("m_nWeightListIndex");
                    var weightListName = GetWeightListName(weightListIndex);
                    node.Add("m_weightListName", weightListName);
                    continue;
                }
            }
            else if (className == "CBlend")
            {
                if (key == "m_children")
                {
                    var targetValues = compiledNode.GetFloatArray("m_targetValues");

                    if (inputNodeIds is not null)
                    {
                        var blendChildren = new List<KVObject>();

                        for (var childIndex = 0; childIndex < inputNodeIds.Length; childIndex++)
                        {
                            var nodeId = inputNodeIds[childIndex];
                            var blendValue = childIndex < targetValues.Length ? targetValues[childIndex] : 0.0f;
                            var blendChild = MakeNode("CBlendNodeChild");
                            AddInputConnection(blendChild, nodeId);
                            blendChild.Add("m_name", "Unnamed");
                            blendChild.Add("m_blendValue", blendValue);
                            blendChildren.Add(blendChild);
                        }

                        node.Add("m_children", MakeArray(blendChildren.ToArray()));
                    }

                    continue;
                }

                if (key is "m_targetValues" or "m_sortedOrder")
                {
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

                        var itemClass = (hasSequence, hasChild) switch
                        {
                            (true, _) => "CSequenceBlend2DItem",
                            (false, true) => "CNodeBlend2DItem",
                            _ => "CSequenceBlend2DItem"
                        };

                        var convertedItem = new KVObject();

                        foreach (var (itemKey, itemValue) in item.Children)
                        {
                            if (itemKey == "m_hSequence")
                            {
                                var sequenceIndex = item.GetIntegerProperty("m_hSequence");
                                if (sequenceIndex != -1)
                                {
                                    var sequenceName = GetSequenceName(sequenceIndex);
                                    convertedItem.Add("m_sequenceName", sequenceName);
                                }
                            }
                            else if (itemKey == "m_pChild")
                            {
                                if (itemClass == "CNodeBlend2DItem")
                                {
                                    var itemChildRef = item.GetSubCollection("m_pChild");
                                    var nodeIndex = itemChildRef.GetIntegerProperty("m_nodeIndex");
                                    if (nodeIndexToIdMap?.TryGetValue(nodeIndex, out var nodeId) == true)
                                    {
                                        var connection = MakeInputConnection(nodeId);
                                        convertedItem.Add("m_inputConnection", connection);
                                    }
                                }
                                continue;
                            }
                            else if (itemKey == "m_tags")
                            {
                                try
                                {
                                    convertedItem.Add("m_tagSpans", ConvertTagSpansArray(item.GetArray("m_tags")));
                                }
                                catch
                                {
                                    convertedItem.Add("m_tagSpans", MakeArray(Array.Empty<KVObject>()));
                                }
                                continue;
                            }
                            else if (itemKey == "m_vPos")
                            {
                                convertedItem.Add("m_blendValue", itemValue);
                            }
                            else if (itemKey == "m_flDuration")
                            {
                                var useCustomDuration = item.GetIntegerProperty("m_bUseCustomDuration") > 0;
                                if (useCustomDuration)
                                {
                                    convertedItem.Add("m_flCustomDuration", itemValue);
                                }
                            }
                            else if (itemKey == "m_bUseCustomDuration")
                            {
                                convertedItem.Add(itemKey, itemValue);
                            }
                            else
                            {
                                convertedItem.Add(itemKey, itemValue);
                            }
                        }
                        convertedItem.Add("_class", itemClass);
                        convertedItems.Add(convertedItem);
                    }

                    node.Add("m_items", MakeArray(convertedItems.ToArray()));
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
                        node.Add("m_tagSpans", MakeArray(Array.Empty<KVObject>()));
                    }
                    continue;
                }
            }
            else if (className == "CAimMatrix")
            {
                if (key == "m_opFixedSettings")
                {
                    var opFixedSettings = subCollection.Value;

                    if (opFixedSettings.ContainsKey("m_eBlendMode"))
                    {
                        node.Add("m_blendMode", opFixedSettings.GetStringProperty("m_eBlendMode"));
                    }

                    if (opFixedSettings.ContainsKey("m_nBoneMaskIndex"))
                    {
                        var boneMaskIndex = opFixedSettings.GetIntegerProperty("m_nBoneMaskIndex");
                        var boneMaskName = boneMaskIndex == -1 ? "" : GetWeightListName(boneMaskIndex);
                        node.Add("m_boneMaskName", boneMaskName);
                    }

                    if (opFixedSettings.ContainsKey("m_damping"))
                    {
                        node.Add("m_damping", opFixedSettings.GetSubCollection("m_damping"));
                    }

                    if (opFixedSettings.ContainsKey("m_flMaxYawAngle"))
                    {
                        node.Add("m_fAngleIncrement", opFixedSettings.GetFloatProperty("m_flMaxYawAngle"));
                    }

                    if (opFixedSettings.ContainsKey("m_attachment"))
                    {
                        var attachment = opFixedSettings.GetSubCollection("m_attachment");
                        var attachmentName = FindMatchingAttachmentName(attachment);
                        node.Add("m_attachmentName", attachmentName);
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
                    var opFixedData = subCollection.Value;

                    if (opFixedData.ContainsKey("m_boneIndex"))
                    {
                        var boneIndex = (int)opFixedData.GetIntegerProperty("m_boneIndex");
                        var boneName = GetBoneName(boneIndex);
                        node.Add("m_boneName", boneName);
                    }

                    if (opFixedData.ContainsKey("m_attachment"))
                    {
                        var attachment = opFixedData.GetSubCollection("m_attachment");
                        var attachmentName = FindMatchingAttachmentName(attachment);
                        node.Add("m_attachmentName", attachmentName);
                    }

                    if (opFixedData.ContainsKey("m_bMatchTranslation"))
                    {
                        node.Add("m_bMatchTranslation", opFixedData.GetIntegerProperty("m_bMatchTranslation") > 0);
                    }

                    if (opFixedData.ContainsKey("m_bMatchRotation"))
                    {
                        node.Add("m_bMatchRotation", opFixedData.GetIntegerProperty("m_bMatchRotation") > 0);
                    }

                    continue;
                }
            }
            else if (className == "CFootAdjustment")
            {
                if (key == "m_clips")
                {
                    var clipIndices = compiledNode.GetIntegerArray("m_clips");
                    var clipNames = clipIndices.Select(idx => GetSequenceName(idx)).ToArray();
                    node.Add("m_clips", MakeArray(clipNames.Select(name => (KVObject)name).ToArray()));
                    continue;
                }
                else if (key == "m_hBasePoseCacheHandle")
                {
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
                    var poseOpFixedData = subCollection.Value;
                    if (poseOpFixedData.ContainsKey("m_footInfo"))
                    {
                        var footInfoArray = poseOpFixedData.GetArray("m_footInfo");
                        footPinningItems = [];

                        foreach (var footInfo in footInfoArray)
                        {
                            var convertedItem = new KVObject();
                            if (footInfo.ContainsKey("m_nFootIndex"))
                            {
                                var footIndex = (int)footInfo.GetIntegerProperty("m_nFootIndex");
                                var footName = GetFootName(footIndex);
                                if (!string.IsNullOrEmpty(footName))
                                {
                                    convertedItem.Add("m_footName", footName);
                                }
                            }
                            if (footInfo.ContainsKey("m_nTargetBoneIndex"))
                            {
                                var boneIndex = (int)footInfo.GetIntegerProperty("m_nTargetBoneIndex");
                                var boneName = GetBoneName(boneIndex);
                                if (!string.IsNullOrEmpty(boneName))
                                {
                                    convertedItem.Add("m_targetBoneName", boneName);
                                }
                            }
                            if (footInfo.ContainsKey("m_ikChainIndex"))
                            {
                                var ikChainIndex = (int)footInfo.GetIntegerProperty("m_ikChainIndex");
                                var ikChainName = GetIKChainName(ikChainIndex);
                                if (!string.IsNullOrEmpty(ikChainName))
                                {
                                    convertedItem.Add("m_ikChainName", ikChainName);
                                }
                            }
                            if (footInfo.ContainsKey("m_nTagIndex"))
                            {
                                var tagIndex = footInfo.GetIntegerProperty("m_nTagIndex");
                                convertedItem.Add("m_tag", MakeNodeIdObjectValue(GetTagIdFromIndex(tagIndex)));
                            }
                            convertedItem.Add("m_param", MakeNodeIdObjectValue(-1));
                            if (footInfo.ContainsKey("m_flMaxRotationLeft"))
                            {
                                convertedItem.Add("m_flMaxRotationLeft", footInfo.GetFloatProperty("m_flMaxRotationLeft"));
                            }
                            if (footInfo.ContainsKey("m_flMaxRotationRight"))
                            {
                                convertedItem.Add("m_flMaxRotationRight", footInfo.GetFloatProperty("m_flMaxRotationRight"));
                            }
                            footPinningItems.Add(convertedItem);
                        }
                    }
                    if (poseOpFixedData.ContainsKey("m_flBlendTime"))
                    {
                        node.Add("m_flBlendTime", poseOpFixedData.GetFloatProperty("m_flBlendTime"));
                    }
                    if (poseOpFixedData.ContainsKey("m_flLockBreakDistance"))
                    {
                        node.Add("m_flLockBreakDistance", poseOpFixedData.GetFloatProperty("m_flLockBreakDistance"));
                    }
                    if (poseOpFixedData.ContainsKey("m_flMaxLegTwist"))
                    {
                        node.Add("m_flMaxLegTwist", poseOpFixedData.GetFloatProperty("m_flMaxLegTwist"));
                    }
                    if (poseOpFixedData.ContainsKey("m_nHipBoneIndex"))
                    {
                        var hipBoneIndex = (int)poseOpFixedData.GetIntegerProperty("m_nHipBoneIndex");
                        var hipBoneName = GetBoneName(hipBoneIndex);
                        if (!string.IsNullOrEmpty(hipBoneName))
                        {
                            node.Add("m_hipBoneName", hipBoneName);
                        }
                    }
                    if (poseOpFixedData.ContainsKey("m_bApplyLegTwistLimits"))
                    {
                        node.Add("m_bApplyLegTwistLimits", poseOpFixedData.GetIntegerProperty("m_bApplyLegTwistLimits") > 0);
                    }
                    if (poseOpFixedData.ContainsKey("m_bApplyFootRotationLimits"))
                    {
                        node.Add("m_bApplyFootRotationLimits", poseOpFixedData.GetIntegerProperty("m_bApplyFootRotationLimits") > 0);
                    }
                    continue;
                }
                else if (key == "m_eTimingSource")
                {
                    node.Add(key, value);
                    continue;
                }
                else if (key == "m_params")
                {
                    var paramHandles = compiledNode.GetArray("m_params");
                    var itemsArray = node.GetArray("m_items");
                    var itemsList = itemsArray?.ToList() ?? [];

                    if (itemsList.Count == 0 && footPinningItems != null && footPinningItems.Count > 0)
                    {
                        itemsList = footPinningItems;
                    }

                    for (var i = 0; i < itemsList.Count; i++)
                    {
                        if (i < paramHandles.Count)
                        {
                            var paramHandle = paramHandles[i];
                            var paramType = paramHandle.GetStringProperty("m_type");
                            var paramIndex = paramHandle.GetIntegerProperty("m_index");
                            var paramIdValue = ParameterIDFromIndex(paramType, paramIndex);
                            itemsList[i]["m_param"] = paramIdValue;
                        }
                    }

                    node.Add("m_items", MakeArray(itemsList.ToArray()));
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
                                convertedItem.Add("m_tags", MakeArray(Array.Empty<KVObject>()));
                            }
                        }
                        else
                        {
                            convertedItem.Add("m_tags", MakeArray(Array.Empty<KVObject>()));
                        }
                        convertedItems.Add(convertedItem);
                    }
                    node.Add("m_items", MakeArray(convertedItems.ToArray()));
                    continue;
                }
            }
            else if (className == "CJiggleBone")
            {
                if (key == "m_opFixedData")
                {
                    var opFixedData = subCollection.Value;
                    if (opFixedData.ContainsKey("m_boneSettings"))
                    {
                        var boneSettingsArray = opFixedData.GetArray("m_boneSettings");
                        var convertedItems = new List<KVObject>();
                        foreach (var boneSetting in boneSettingsArray)
                        {
                            var convertedItem = new KVObject();
                            if (boneSetting.ContainsKey("m_nBoneIndex"))
                            {
                                var boneIndex = (int)boneSetting.GetIntegerProperty("m_nBoneIndex");
                                var boneName = GetBoneName(boneIndex);
                                if (!string.IsNullOrEmpty(boneName))
                                {
                                    convertedItem.Add("m_boneName", boneName);
                                }
                            }
                            if (boneSetting.ContainsKey("m_flSpringStrength"))
                            {
                                convertedItem.Add("m_flSpringStrength", boneSetting.GetFloatProperty("m_flSpringStrength"));
                            }
                            if (boneSetting.ContainsKey("m_flMaxTimeStep"))
                            {
                                var maxTimeStep = boneSetting.GetFloatProperty("m_flMaxTimeStep");
                                if (maxTimeStep > 0)
                                {
                                    var simRateFPS = 1.0f / maxTimeStep;
                                    convertedItem.Add("m_flSimRateFPS", simRateFPS);
                                }
                                else
                                {
                                    convertedItem.Add("m_flSimRateFPS", 90.0f);
                                }
                            }
                            if (boneSetting.ContainsKey("m_flDamping"))
                            {
                                convertedItem.Add("m_flDamping", boneSetting.GetFloatProperty("m_flDamping"));
                            }
                            if (boneSetting.ContainsKey("m_eSimSpace"))
                            {
                                convertedItem.Add("m_eSimSpace", boneSetting.GetStringProperty("m_eSimSpace"));
                            }
                            if (boneSetting.ContainsKey("m_vBoundsMaxLS"))
                            {
                                convertedItem.Add("m_vBoundsMaxLS", boneSetting.GetSubCollection("m_vBoundsMaxLS"));
                            }
                            if (boneSetting.ContainsKey("m_vBoundsMinLS"))
                            {
                                convertedItem.Add("m_vBoundsMinLS", boneSetting.GetSubCollection("m_vBoundsMinLS"));
                            }
                            convertedItems.Add(convertedItem);
                        }
                        node.Add("m_items", MakeArray(convertedItems.ToArray()));
                    }
                    continue;
                }
            }
            else if (className == "CJumpHelper")
            {
                if (key == "m_name")
                {
                    var nameValue = value.ToString() ?? "Unnamed";
                    node.Add("m_sName", nameValue);
                    continue;
                }
                else if (key == "m_hSequence")
                {
                    var sequenceIndex = compiledNode.GetIntegerProperty("m_hSequence");
                    var sequenceName = GetSequenceName(sequenceIndex);
                    node.Add("m_sequenceName", sequenceName);
                    continue;
                }
                else if (key == "m_hTargetParam")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    var paramIdValue = ParameterIDFromIndex(paramType, paramIndex);
                    node.Add("m_targetParamID", paramIdValue);
                    continue;
                }
                else if (key == "m_flJumpStartCycle")
                {
                    node.Add(key, value);
                    continue;
                }
                else if (key == "m_flJumpEndCycle")
                {
                    var jumpStart = compiledNode.GetFloatProperty("m_flJumpStartCycle");
                    var jumpEnd = compiledNode.GetFloatProperty("m_flJumpEndCycle");
                    var jumpDuration = jumpEnd - jumpStart;
                    node.Add("m_flJumpDuration", jumpDuration);
                    continue;
                }
                else if (key == "m_flOriginalJumpDuration")
                {
                    continue;
                }
                else if (key == "m_flOriginalJumpMovement")
                {
                    continue;
                }
                else if (key == "m_bScaleSpeed" || key == "m_playbackSpeed" || key == "m_bLoop" || key == "m_eCorrectionMethod")
                {
                    node.Add(key, value);
                    continue;
                }
                else if (key == "m_duration")
                {
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
                        node.Add("m_tagSpans", MakeArray(Array.Empty<KVObject>()));
                    }
                    continue;
                }
                else if (key == "m_paramSpans")
                {
                    try
                    {
                        var compiledParamSpans = compiledNode.GetSubCollection("m_paramSpans");

                        if (compiledParamSpans is not null && compiledParamSpans.ContainsKey("m_spans"))
                        {
                            var compiledSpans = compiledParamSpans.GetArray("m_spans");
                            var paramSpans = new List<KVObject>();

                            foreach (var compiledSpan in compiledSpans)
                            {
                                var paramSpan = MakeNode("CAnimParamSpan");

                                if (compiledSpan.ContainsKey("m_samples"))
                                {
                                    paramSpan.Add("m_samples", compiledSpan.GetSubCollection("m_samples"));
                                }

                                if (compiledSpan.ContainsKey("m_hParam"))
                                {
                                    var paramHandle = compiledSpan.GetSubCollection("m_hParam");
                                    var paramType = paramHandle.GetStringProperty("m_type");
                                    var paramIndex = paramHandle.GetIntegerProperty("m_index");
                                    var paramIdValue = ParameterIDFromIndex(paramType, paramIndex);
                                    paramSpan.Add("m_id", paramIdValue);
                                }

                                if (compiledSpan.ContainsKey("m_flStartCycle"))
                                {
                                    paramSpan.Add("m_flStartCycle", compiledSpan.GetFloatProperty("m_flStartCycle"));
                                }

                                if (compiledSpan.ContainsKey("m_flEndCycle"))
                                {
                                    paramSpan.Add("m_flEndCycle", compiledSpan.GetFloatProperty("m_flEndCycle"));
                                }

                                paramSpans.Add(paramSpan);
                            }

                            node.Add("m_paramSpans", MakeArray(paramSpans.ToArray()));
                        }
                        else
                        {
                            node.Add("m_paramSpans", MakeArray(Array.Empty<KVObject>()));
                        }
                    }
                    catch
                    {
                        node.Add("m_paramSpans", MakeArray(Array.Empty<KVObject>()));
                    }
                    continue;
                }
            }
            else if (className == "CLeanMatrix")
            {
                if (key == "m_name")
                {
                    var nameValue = value.ToString() ?? "Unnamed";
                    node.Add("m_sName", nameValue);
                    continue;
                }
                else if (key == "m_hSequence")
                {
                    var sequenceIndex = compiledNode.GetIntegerProperty("m_hSequence");
                    var sequenceName = GetSequenceName(sequenceIndex);
                    node.Add("m_sequenceName", sequenceName);
                    continue;
                }
                else if (key == "m_paramIndex")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    var paramIdValue = ParameterIDFromIndex(paramType, paramIndex);
                    node.Add("m_param", paramIdValue);
                    continue;
                }
                else if (key == "m_verticalAxis")
                {
                    node.Add("m_verticalAxisDirection", value);
                    continue;
                }
                else if (key == "m_horizontalAxis")
                {
                    node.Add("m_horizontalAxisDirection", value);
                    continue;
                }
                else if (key == "m_damping")
                {
                    node.Add(key, value);
                    continue;
                }
                else if (key == "m_blendSource")
                {
                    node.Add(key, value);
                    continue;
                }
                else if (key == "m_flMaxValue")
                {
                    node.Add(key, value);
                    continue;
                }
                else if (key == "m_frameCorners" || key == "m_poses" || key == "m_nSequenceMaxFrame")
                {
                    continue;
                }
            }
            else if (className == "CLookAt")
            {
                if (key == "m_name")
                {
                    node.Add("m_sName", value);
                    continue;
                }
                else if (key == "m_pChildNode")
                {
                    TryAddInputConnectionFromRef(node, subCollection.Value);
                    continue;
                }
                else if (key == "m_target")
                {
                    node.Add(key, value);
                    continue;
                }
                else if (key == "m_paramIndex")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    var paramIdValue = ParameterIDFromIndex(paramType, paramIndex);
                    node.Add("m_param", paramIdValue);
                    continue;
                }
                else if (key == "m_weightParamIndex")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    var paramIdValue = ParameterIDFromIndex(paramType, paramIndex);
                    node.Add("m_weightParam", paramIdValue);
                    continue;
                }
                else if (key == "m_opFixedSettings")
                {
                    var opFixedSettings = subCollection.Value;
                    if (opFixedSettings.ContainsKey("m_bones"))
                    {
                        var lookAtChainName = FindMatchingLookAtChainName(opFixedSettings);
                        node.Add("m_lookatChainName", lookAtChainName);
                    }
                    else
                    {
                        node.Add("m_lookatChainName", "");
                    }
                    if (opFixedSettings.ContainsKey("m_attachment"))
                    {
                        var attachment = opFixedSettings.GetSubCollection("m_attachment");
                        var attachmentName = FindMatchingAttachmentName(attachment);
                        node.Add("m_attachmentName", attachmentName);
                    }
                    else
                    {
                        node.Add("m_attachmentName", "aim");
                    }
                    if (opFixedSettings.ContainsKey("m_flYawLimit"))
                    {
                        node.Add("m_flYawLimit", opFixedSettings.GetFloatProperty("m_flYawLimit"));
                    }
                    if (opFixedSettings.ContainsKey("m_flPitchLimit"))
                    {
                        node.Add("m_flPitchLimit", opFixedSettings.GetFloatProperty("m_flPitchLimit"));
                    }
                    if (opFixedSettings.ContainsKey("m_flHysteresisInnerAngle"))
                    {
                        node.Add("m_flHysteresisInnerAngle", opFixedSettings.GetFloatProperty("m_flHysteresisInnerAngle"));
                    }
                    if (opFixedSettings.ContainsKey("m_flHysteresisOuterAngle"))
                    {
                        node.Add("m_flHysteresisOuterAngle", opFixedSettings.GetFloatProperty("m_flHysteresisOuterAngle"));
                    }
                    if (opFixedSettings.ContainsKey("m_bRotateYawForward"))
                    {
                        node.Add("m_bRotateYawForward", opFixedSettings.GetIntegerProperty("m_bRotateYawForward") > 0);
                    }
                    if (opFixedSettings.ContainsKey("m_bMaintainUpDirection"))
                    {
                        node.Add("m_bMaintainUpDirection", opFixedSettings.GetIntegerProperty("m_bMaintainUpDirection") > 0);
                    }
                    if (opFixedSettings.ContainsKey("m_bTargetIsPosition"))
                    {
                        node.Add("m_bIsPosition", opFixedSettings.GetIntegerProperty("m_bTargetIsPosition") > 0);
                    }
                    if (opFixedSettings.ContainsKey("m_bUseHysteresis"))
                    {
                        node.Add("m_bUseHysteresis", opFixedSettings.GetIntegerProperty("m_bUseHysteresis") > 0);
                    }
                    if (opFixedSettings.ContainsKey("m_damping"))
                    {
                        node.Add("m_damping", opFixedSettings.GetSubCollection("m_damping"));
                    }
                    continue;
                }
                else if (key == "m_bResetChild")
                {
                    node.Add("m_bResetBase", value);
                    continue;
                }
                else if (key == "m_bLockWhenWaning")
                {
                    node.Add(key, value);
                    continue;
                }
            }
            else if (className == "CHitReact")
            {
                if (key == "m_pChildNode")
                {
                    TryAddInputConnectionFromRef(node, subCollection.Value);
                    continue;
                }
                else if (key == "m_networkMode")
                {
                    node.Add(key, value);
                    continue;
                }
                else if (key == "m_opFixedSettings")
                {
                    var opFixedSettings = subCollection.Value;
                    if (opFixedSettings.ContainsKey("m_nWeightListIndex"))
                    {
                        var weightListIndex = opFixedSettings.GetIntegerProperty("m_nWeightListIndex");
                        var weightListName = GetWeightListName(weightListIndex);
                        node.Add("m_weightListName", weightListName);
                    }
                    if (opFixedSettings.ContainsKey("m_nHipBoneIndex"))
                    {
                        var hipBoneIndex = (int)opFixedSettings.GetIntegerProperty("m_nHipBoneIndex");
                        var hipBoneName = GetBoneName(hipBoneIndex);
                        if (!string.IsNullOrEmpty(hipBoneName))
                        {
                            node.Add("m_hipBoneName", hipBoneName);
                        }
                    }
                    var settingsToCopy = new[]
                    {
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
                    "m_flHipDipDelay"
                };

                    foreach (var settingKey in settingsToCopy)
                    {
                        if (opFixedSettings.ContainsKey(settingKey))
                        {
                            node.Add(settingKey, opFixedSettings[settingKey]);
                        }
                    }
                    continue;
                }
                else if (key == "m_triggerParam")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add(key, ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_hitBoneParam")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add(key, ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_hitOffsetParam")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add(key, ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_hitDirectionParam")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add(key, ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_hitStrengthParam")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add(key, ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_flMinDelayBetweenHits")
                {
                    node.Add(key, value);
                    continue;
                }
                else if (key == "m_bResetChild")
                {
                    node.Add("m_bResetBase", value);
                    continue;
                }
            }
            else if (className == "CSolveIKChain")
            {
                if (key == "m_pChildNode")
                {
                    TryAddInputConnectionFromRef(node, subCollection.Value);
                    continue;
                }
                else if (key == "m_networkMode")
                {
                    node.Add(key, value);
                    continue;
                }
                else if (key == "m_targetHandles")
                {
                    continue;
                }
                else if (key == "m_opFixedData")
                {
                    var opFixedData = subCollection.Value;
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
                            var chainIndex = (int)chainData.GetIntegerProperty("m_nChainIndex");
                            var chainName = GetIKChainName(chainIndex);
                            ikChain.Add("m_IkChain", chainName);
                        }

                        ikChain.Add("m_SolverSettingSource", "SOLVEIKCHAINANIMNODESETTINGSOURCE_Default");

                        if (chainData.ContainsKey("m_SolverSettings"))
                        {
                            var solverSettings = chainData.GetSubCollection("m_SolverSettings");
                            var overrideSolverSettings = new KVObject();

                            if (solverSettings.ContainsKey("m_SolverType"))
                            {
                                overrideSolverSettings.Add("m_SolverType", solverSettings.GetStringProperty("m_SolverType"));
                            }

                            ikChain.Add("m_OverrideSolverSettings", overrideSolverSettings);
                        }

                        ikChain.Add("m_TargetSettingSource", "SOLVEIKCHAINANIMNODESETTINGSOURCE_Default");

                        if (chainData.ContainsKey("m_TargetSettings"))
                        {
                            var targetSettings = chainData.GetSubCollection("m_TargetSettings");
                            var overrideTargetSettings = new KVObject();

                            if (targetSettings.ContainsKey("m_TargetSource"))
                            {
                                overrideTargetSettings.Add("m_TargetSource", targetSettings.GetStringProperty("m_TargetSource"));
                            }

                            if (targetSettings.ContainsKey("m_Bone"))
                            {
                                var boneSettings = targetSettings.GetSubCollection("m_Bone");
                                var boneNameObj = new KVObject();
                                boneNameObj.Add("m_Name", boneSettings.GetStringProperty("m_Name"));
                                overrideTargetSettings.Add("m_Bone", boneNameObj);
                            }

                            if (targetHandle != null)
                            {
                                var positionHandle = targetHandle.GetSubCollection("m_positionHandle");
                                var positionParamType = positionHandle.GetStringProperty("m_type");
                                var positionParamIndex = positionHandle.GetIntegerProperty("m_index");
                                overrideTargetSettings.Add("m_AnimgraphParameterNamePosition",
                                    ParameterIDFromIndex(positionParamType, positionParamIndex));

                                var orientationHandle = targetHandle.GetSubCollection("m_orientationHandle");
                                var orientationParamType = orientationHandle.GetStringProperty("m_type");
                                var orientationParamIndex = orientationHandle.GetIntegerProperty("m_index");
                                overrideTargetSettings.Add("m_AnimgraphParameterNameOrientation",
                                    ParameterIDFromIndex(orientationParamType, orientationParamIndex));
                            }
                            else
                            {
                                overrideTargetSettings.Add("m_AnimgraphParameterNamePosition", MakeNodeIdObjectValue(-1));
                                overrideTargetSettings.Add("m_AnimgraphParameterNameOrientation", MakeNodeIdObjectValue(-1));
                            }

                            if (targetSettings.ContainsKey("m_TargetCoordSystem"))
                            {
                                overrideTargetSettings.Add("m_TargetCoordSystem", targetSettings.GetStringProperty("m_TargetCoordSystem"));
                            }

                            ikChain.Add("m_OverrideTargetSettings", overrideTargetSettings);
                        }

                        if (chainData.ContainsKey("m_DebugSetting"))
                        {
                            ikChain.Add("m_DebugSetting", chainData.GetStringProperty("m_DebugSetting"));
                        }

                        if (chainData.ContainsKey("m_flDebugNormalizedValue"))
                        {
                            ikChain.Add("m_flDebugNormalizedLength", chainData.GetFloatProperty("m_flDebugNormalizedValue"));
                        }

                        if (chainData.ContainsKey("m_vDebugOffset"))
                        {
                            ikChain.Add("m_vDebugOffset", chainData.GetSubCollection("m_vDebugOffset"));
                        }

                        ikChainsArray.Add(ikChain);
                    }

                    node.Add("m_IkChains", MakeArray(ikChainsArray.ToArray()));

                    if (opFixedData.ContainsKey("m_bMatchTargetOrientation"))
                    {
                        node.Add("m_bMatchTargetOrientation", opFixedData.GetIntegerProperty("m_bMatchTargetOrientation") > 0);
                    }

                    continue;
                }
            }
            else if (className == "CStanceOverride")
            {
                if (key == "m_pChildNode")
                {
                    TryAddInputConnectionFromRef(node, subCollection.Value);
                    continue;
                }
                else if (key == "m_networkMode")
                {
                    node.Add(key, value);
                    continue;
                }
                else if (key == "m_pStanceSourceNode")
                {
                    var stanceSourceNodeId = subCollection.Value.GetIntegerProperty("m_nodeIndex");
                    if (stanceSourceNodeId != -1)
                    {
                        if (nodeIndexToIdMap?.TryGetValue(stanceSourceNodeId, out var mappedNodeId) == true)
                        {
                            var stanceSourceConnection = MakeInputConnection(mappedNodeId);
                            node.Add("m_stanceSourceConnection", stanceSourceConnection);
                        }
                    }
                    else
                    {
                        var emptyConnection = MakeInputConnection(-1);
                        node.Add("m_stanceSourceConnection", emptyConnection);
                    }
                    continue;
                }
                else if (key == "m_hParameter")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add("m_blendParamID", ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_eMode")
                {
                    node.Add(key, value);
                    continue;
                }
                else if (key == "m_footStanceInfo")
                {
                    continue;
                }
                else if (key == "m_opFixedData")
                {
                    var opFixedData = subCollection.Value;
                    if (opFixedData.ContainsKey("m_nFrameIndex"))
                    {
                        var frameIndex = opFixedData.GetIntegerProperty("m_nFrameIndex");
                        node.Add("m_nFrameIndex", frameIndex);
                    }
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
            else if (className == "CStopAtGoal")
            {
                if (key == "m_pChildNode")
                {
                    TryAddInputConnectionFromRef(node, subCollection.Value);
                    continue;
                }
                else if (key == "m_networkMode")
                {
                    node.Add(key, value);
                    continue;
                }
                else if (key == "m_flOuterRadius" || key == "m_flInnerRadius" ||
                         key == "m_flMaxScale" || key == "m_flMinScale")
                {
                    node.Add(key, value);
                    continue;
                }
                else if (key == "m_damping")
                {
                    node.Add(key, value);
                    continue;
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
                else if (key == "m_pChildNode")
                {
                    var childNodeIndex = subCollection.Value.GetIntegerProperty("m_nodeIndex");
                    if (nodeIndexToIdMap?.TryGetValue(childNodeIndex, out var childNodeId) == true)
                    {
                        var connection = MakeInputConnection(childNodeId);
                        node.Add("m_inputConnection", connection);
                    }
                    continue;
                }
                else if (key == "m_opFixedSettings")
                {
                    var opFixedSettings = subCollection.Value;

                    if (opFixedSettings.ContainsKey("m_nHipBoneIndex"))
                    {
                        var hipBoneIndex = (int)opFixedSettings.GetIntegerProperty("m_nHipBoneIndex");
                        var hipBoneName = GetBoneName(hipBoneIndex);
                        if (!string.IsNullOrEmpty(hipBoneName))
                        {
                            node.Add("m_hipBoneName", hipBoneName);
                        }
                    }

                    if (opFixedSettings.ContainsKey("m_ikSolverType"))
                    {
                        node.Add("m_ikSolverType", opFixedSettings.GetStringProperty("m_ikSolverType"));
                    }

                    if (opFixedSettings.ContainsKey("m_bAlwaysUseFallbackHinge"))
                    {
                        node.Add("m_bAlwaysUseFallbackHinge", opFixedSettings.GetIntegerProperty("m_bAlwaysUseFallbackHinge") > 0);
                    }

                    if (opFixedSettings.ContainsKey("m_bApplyLegTwistLimits"))
                    {
                        node.Add("m_bApplyLegTwistLimits", opFixedSettings.GetIntegerProperty("m_bApplyLegTwistLimits") > 0);
                    }

                    if (opFixedSettings.ContainsKey("m_flMaxLegTwist"))
                    {
                        node.Add("m_flMaxLegTwist", opFixedSettings.GetFloatProperty("m_flMaxLegTwist"));
                    }

                    if (opFixedSettings.ContainsKey("m_bApplyTilt"))
                    {
                        node.Add("m_bApplyTilt", opFixedSettings.GetIntegerProperty("m_bApplyTilt") > 0);
                    }

                    if (opFixedSettings.ContainsKey("m_bApplyHipDrop"))
                    {
                        node.Add("m_bApplyHipDrop", opFixedSettings.GetIntegerProperty("m_bApplyHipDrop") > 0);
                    }

                    if (opFixedSettings.ContainsKey("m_bApplyFootRotationLimits") && !node.ContainsKey("m_bApplyFootRotationLimits"))
                    {
                        node.Add("m_bApplyFootRotationLimits", opFixedSettings.GetIntegerProperty("m_bApplyFootRotationLimits") > 0);
                    }

                    if (opFixedSettings.ContainsKey("m_flMaxFootHeight"))
                    {
                        node.Add("m_flMaxFootHeight", opFixedSettings.GetFloatProperty("m_flMaxFootHeight"));
                    }

                    if (opFixedSettings.ContainsKey("m_flExtensionScale"))
                    {
                        node.Add("m_flExtensionScale", opFixedSettings.GetFloatProperty("m_flExtensionScale"));
                    }

                    if (opFixedSettings.ContainsKey("m_bEnableLockBreaking"))
                    {
                        node.Add("m_bEnableLockBreaking", opFixedSettings.GetIntegerProperty("m_bEnableLockBreaking") > 0);
                    }

                    if (opFixedSettings.ContainsKey("m_flLockBreakTolerance"))
                    {
                        node.Add("m_flLockBreakTolerance", opFixedSettings.GetFloatProperty("m_flLockBreakTolerance"));
                    }

                    if (opFixedSettings.ContainsKey("m_flLockBlendTime"))
                    {
                        node.Add("m_flLockBreakBlendTime", opFixedSettings.GetFloatProperty("m_flLockBlendTime"));
                    }

                    if (opFixedSettings.ContainsKey("m_bEnableStretching"))
                    {
                        node.Add("m_bEnableStretching", opFixedSettings.GetIntegerProperty("m_bEnableStretching") > 0);
                    }

                    if (opFixedSettings.ContainsKey("m_flMaxStretchAmount"))
                    {
                        node.Add("m_flMaxStretchAmount", opFixedSettings.GetFloatProperty("m_flMaxStretchAmount"));
                    }

                    if (opFixedSettings.ContainsKey("m_flStretchExtensionScale"))
                    {
                        node.Add("m_flStretchExtensionScale", opFixedSettings.GetFloatProperty("m_flStretchExtensionScale"));
                    }

                    if (opFixedSettings.ContainsKey("m_hipDampingSettings"))
                    {
                        node.Add("m_hipDampingSettings", opFixedSettings.GetSubCollection("m_hipDampingSettings"));
                    }
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
                            var footIndex = (int)footSetting.GetIntegerProperty("m_nFootIndex");
                            var footName = GetFootName(footIndex);
                            if (!string.IsNullOrEmpty(footName))
                            {
                                item.Add("m_footName", footName);
                            }
                        }

                        if (footInfo != null && footInfo.ContainsKey("m_nTargetBoneIndex"))
                        {
                            var targetBoneIndex = (int)footInfo.GetIntegerProperty("m_nTargetBoneIndex");
                            var targetBoneName = GetBoneName(targetBoneIndex);
                            if (!string.IsNullOrEmpty(targetBoneName))
                            {
                                item.Add("m_targetBoneName", targetBoneName);
                            }
                        }

                        if (footInfo != null && footInfo.ContainsKey("m_ikChainIndex"))
                        {
                            var ikChainIndex = (int)footInfo.GetIntegerProperty("m_ikChainIndex");
                            var ikChainName = GetIKChainName(ikChainIndex);
                            if (!string.IsNullOrEmpty(ikChainName))
                            {
                                item.Add("m_ikChainName", ikChainName);
                            }
                        }

                        if (footSetting.ContainsKey("m_nDisableTagIndex"))
                        {
                            var tagIndex = footSetting.GetIntegerProperty("m_nDisableTagIndex");
                            var tagId = -1L;
                            if (tagIndex >= 0 && tagIndex < Tags.Count)
                            {
                                tagId = Tags[(int)tagIndex].GetSubCollection("m_tagID").GetIntegerProperty("m_id");
                            }
                            item.Add("m_disableTagID", MakeNodeIdObjectValue(tagId));
                        }

                        if (footSetting.ContainsKey("m_footstepLandedTagIndex"))
                        {
                            var tagIndex = footSetting.GetIntegerProperty("m_footstepLandedTagIndex");
                            var tagId = -1L;
                            if (tagIndex >= 0 && tagIndex < Tags.Count)
                            {
                                tagId = Tags[(int)tagIndex].GetSubCollection("m_tagID").GetIntegerProperty("m_id");
                            }
                            item.Add("m_footstepLandedTag", MakeNodeIdObjectValue(tagId));
                        }

                        if (footSetting.ContainsKey("m_flMaxRotationLeft"))
                        {
                            item.Add("m_flMaxRotationLeft", footSetting.GetFloatProperty("m_flMaxRotationLeft"));
                        }
                        else if (footInfo != null && footInfo.ContainsKey("m_flMaxRotationLeft"))
                        {
                            item.Add("m_flMaxRotationLeft", footInfo.GetFloatProperty("m_flMaxRotationLeft"));
                        }

                        if (footSetting.ContainsKey("m_flMaxRotationRight"))
                        {
                            item.Add("m_flMaxRotationRight", footSetting.GetFloatProperty("m_flMaxRotationRight"));
                        }
                        else if (footInfo != null && footInfo.ContainsKey("m_flMaxRotationRight"))
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
                        node.Add("m_items", MakeArray(items.ToArray()));
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
                else if (key == "m_hipShiftDamping")
                {
                    node.Add("m_hipShiftDamping", subCollection.Value);
                    continue;
                }
                else if (key == "m_rootHeightDamping")
                {
                    node.Add("m_rootHeightDamping", subCollection.Value);
                    continue;
                }
                else if (key == "m_flStrideCurveScale")
                {
                    node.Add("m_flStrideCurveScale", value);
                    continue;
                }
                else if (key == "m_flStrideCurveLimitScale")
                {
                    node.Add("m_flStrideCurveLimitScale", value);
                    continue;
                }
                else if (key == "m_flStepHeightIncreaseScale")
                {
                    node.Add("m_flStepHeightIncreaseScale", value);
                    continue;
                }
                else if (key == "m_flStepHeightDecreaseScale")
                {
                    node.Add("m_flStepHeightDecreaseScale", value);
                    continue;
                }
                else if (key == "m_flHipShiftScale")
                {
                    node.Add("m_flHipShiftScale", value);
                    continue;
                }
                else if (key == "m_flBlendTime")
                {
                    node.Add("m_flBlendTime", value);
                    continue;
                }
                else if (key == "m_flMaxRootHeightOffset")
                {
                    node.Add("m_flMaxRootHeightOffset", value);
                    continue;
                }
                else if (key == "m_flMinRootHeightOffset")
                {
                    node.Add("m_flMinRootHeightOffset", value);
                    continue;
                }
                else if (key == "m_flTiltPlanePitchSpringStrength")
                {
                    node.Add("m_flTiltPlanePitchSpringStrength", value);
                    continue;
                }
                else if (key == "m_flTiltPlaneRollSpringStrength")
                {
                    node.Add("m_flTiltPlaneRollSpringStrength", value);
                    continue;
                }
                else if (key == "m_bApplyFootRotationLimits")
                {
                    node.Add("m_bApplyFootRotationLimits", value);
                    continue;
                }
                else if (key == "m_bApplyHipShift")
                {
                    node.Add("m_bEnableHipShift", value);
                    continue;
                }
                else if (key == "m_bModulateStepHeight")
                {
                    node.Add("m_bModulateStepHeight", value);
                    continue;
                }
                else if (key == "m_bResetChild")
                {
                    node.Add("m_bResetChild", value);
                    continue;
                }
                else if (key == "m_bEnableVerticalCurvedPaths")
                {
                    node.Add("m_bEnableVerticalCurvedPaths", value);
                    continue;
                }
                else if (key == "m_bEnableRootHeightDamping")
                {
                    node.Add("m_bEnableRootHeightDamping", value);
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
                else if (key == "m_pChildNode")
                {
                    TryAddInputConnectionFromRef(node, subCollection.Value);
                    continue;
                }
                else if (key == "m_opFixedData")
                {
                    var opFixedData = subCollection.Value;
                    var foundChainName = "";

                    if (opFixedData.ContainsKey("m_nFixedBoneIndex") &&
                        opFixedData.ContainsKey("m_nMiddleBoneIndex") &&
                        opFixedData.ContainsKey("m_nEndBoneIndex"))
                    {
                        var fixedBoneIndex = (int)opFixedData.GetIntegerProperty("m_nFixedBoneIndex");
                        var middleBoneIndex = (int)opFixedData.GetIntegerProperty("m_nMiddleBoneIndex");
                        var endBoneIndex = (int)opFixedData.GetIntegerProperty("m_nEndBoneIndex");

                        foundChainName = GetIKChainNameByBoneIndices(fixedBoneIndex, middleBoneIndex, endBoneIndex);
                    }
                    node.Add("m_ikChainName", foundChainName);

                    if (opFixedData.ContainsKey("m_bAlwaysUseFallbackHinge"))
                    {
                        var alwaysUseFallback = opFixedData.GetIntegerProperty("m_bAlwaysUseFallbackHinge") > 0;
                        node.Add("m_bAutoDetectHingeAxis", !alwaysUseFallback);
                    }
                    else
                    {
                        node.Add("m_bAutoDetectHingeAxis", true);
                    }

                    if (opFixedData.ContainsKey("m_endEffectorType"))
                    {
                        node.Add("m_endEffectorType", opFixedData.GetStringProperty("m_endEffectorType"));
                    }

                    if (opFixedData.ContainsKey("m_endEffectorAttachment"))
                    {
                        var attachment = opFixedData.GetSubCollection("m_endEffectorAttachment");
                        var attachmentName = FindMatchingAttachmentName(attachment);
                        node.Add("m_endEffectorAttachmentName", attachmentName);
                    }

                    if (opFixedData.ContainsKey("m_targetType"))
                    {
                        node.Add("m_targetType", opFixedData.GetStringProperty("m_targetType"));
                    }

                    if (opFixedData.ContainsKey("m_targetAttachment"))
                    {
                        var attachment = opFixedData.GetSubCollection("m_targetAttachment");
                        var attachmentName = FindMatchingAttachmentName(attachment);
                        node.Add("m_attachmentName", attachmentName);
                    }

                    if (opFixedData.ContainsKey("m_targetBoneIndex"))
                    {
                        var targetBoneIndex = (int)opFixedData.GetIntegerProperty("m_targetBoneIndex");
                        if (targetBoneIndex != -1)
                        {
                            var targetBoneName = GetBoneName(targetBoneIndex);
                            node.Add("m_targetBoneName", targetBoneName);
                        }
                        else
                        {
                            node.Add("m_targetBoneName", "");
                        }
                    }

                    if (opFixedData.ContainsKey("m_hPositionParam"))
                    {
                        var paramRef = opFixedData.GetSubCollection("m_hPositionParam");
                        var paramType = paramRef.GetStringProperty("m_type");
                        var paramIndex = paramRef.GetIntegerProperty("m_index");
                        var paramIdValue = ParameterIDFromIndex(paramType, paramIndex);
                        node.Add("m_targetParam", paramIdValue);
                    }

                    if (opFixedData.ContainsKey("m_bMatchTargetOrientation"))
                    {
                        node.Add("m_bMatchTargetOrientation", opFixedData.GetIntegerProperty("m_bMatchTargetOrientation") > 0);
                    }

                    if (opFixedData.ContainsKey("m_hRotationParam"))
                    {
                        var paramRef = opFixedData.GetSubCollection("m_hRotationParam");
                        var paramType = paramRef.GetStringProperty("m_type");
                        var paramIndex = paramRef.GetIntegerProperty("m_index");
                        var paramIdValue = ParameterIDFromIndex(paramType, paramIndex);
                        node.Add("m_rotationParam", paramIdValue);
                    }

                    if (opFixedData.ContainsKey("m_bConstrainTwist"))
                    {
                        node.Add("m_bConstrainTwist", opFixedData.GetIntegerProperty("m_bConstrainTwist") > 0);
                    }

                    if (opFixedData.ContainsKey("m_flMaxTwist"))
                    {
                        node.Add("m_flMaxTwist", opFixedData.GetFloatProperty("m_flMaxTwist"));
                    }

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
                else if (key == "m_hSequence")
                {
                    var sequenceIndex = compiledNode.GetIntegerProperty("m_hSequence");
                    var sequenceName = GetSequenceName(sequenceIndex);
                    node.Add("m_sequenceName", sequenceName);
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
                else if (key == "m_pChildNode")
                {
                    TryAddInputConnectionFromRef(node, subCollection.Value);
                    continue;
                }
                else if (key == "m_dataSet")
                {
                    var dataSet = subCollection.Value;

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

                                    if (compiledGroup.ContainsKey("m_motionGraphConfigs"))
                                    {
                                        var configs = compiledGroup.GetArray("m_motionGraphConfigs");
                                        var parameterCount = motionGraph.GetIntegerProperty("m_nParameterCount");

                                        for (var paramIdx = 0; paramIdx < parameterCount; paramIdx++)
                                        {
                                            var motionParam = MakeNode("CMotionParameter");
                                            motionParam.Add("m_name", paramIdx.ToString(CultureInfo.InvariantCulture));

                                            var paramId = GenerateNewNodeId([.. motionParamIds]);
                                            motionParamIds.Add(paramId);
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

                                    paramManager.Add("m_params", MakeArray(motionParams.ToArray()));
                                    motion.Add("m_paramManager", paramManager);

                                    if (motionGraph.ContainsKey("m_pRootNode"))
                                    {
                                        var rootNode = motionGraph.GetSubCollection("m_pRootNode");
                                        var nodeManager = MakeNode("CMotionNodeManager");

                                        var motionNodes = ConvertMotionNodeHierarchy(rootNode, motionParamIds);
                                        nodeManager.Add("m_nodes", MakeArray(motionNodes.ToArray()));

                                        motion.Add("m_nodeManager", nodeManager);
                                    }

                                    if (motionGraph.ContainsKey("m_tags"))
                                    {
                                        var compiledTagSpans = motionGraph.GetArray("m_tags");
                                        var tagSpans = new List<KVObject>();

                                        foreach (var compiledTagSpan in compiledTagSpans)
                                        {
                                            var tagSpan = MakeNode("CAnimTagSpan");

                                            var tagIndex = compiledTagSpan.GetIntegerProperty("m_tagIndex");
                                            var tagId = -1L;
                                            if (tagIndex >= 0 && tagIndex < Tags.Count)
                                            {
                                                tagId = Tags[(int)tagIndex].GetSubCollection("m_tagID").GetIntegerProperty("m_id");
                                            }

                                            tagSpan.Add("m_id", MakeNodeIdObjectValue(tagId));
                                            tagSpan.Add("m_fStartCycle", compiledTagSpan.GetFloatProperty("m_startCycle"));
                                            tagSpan.Add("m_fDuration",
                                                compiledTagSpan.GetFloatProperty("m_endCycle") - compiledTagSpan.GetFloatProperty("m_startCycle"));

                                            tagSpans.Add(tagSpan);
                                        }

                                        motion.Add("m_tagSpans", MakeArray(tagSpans.ToArray()));
                                    }
                                    else
                                    {
                                        motion.Add("m_tagSpans", MakeArray(Array.Empty<KVObject>()));
                                    }

                                    if (motionGraph.ContainsKey("m_paramSpans"))
                                    {
                                        var paramSpansContainer = motionGraph.GetSubCollection("m_paramSpans");

                                        if (paramSpansContainer.ContainsKey("m_spans"))
                                        {
                                            var compiledParamSpans = paramSpansContainer.GetArray("m_spans");
                                            var paramSpans = new List<KVObject>();

                                            foreach (var compiledParamSpan in compiledParamSpans)
                                            {
                                                var paramSpan = MakeNode("CAnimParamSpan");

                                                if (compiledParamSpan.ContainsKey("m_hParam"))
                                                {
                                                    paramSpan.Add("m_id", ExtractParameterID(compiledParamSpan.GetSubCollection("m_hParam")));
                                                }
                                                else
                                                {
                                                    paramSpan.Add("m_id", MakeNodeIdObjectValue(-1));
                                                }

                                                paramSpan.Add("m_flStartCycle", compiledParamSpan.GetFloatProperty("m_flStartCycle"));
                                                paramSpan.Add("m_flEndCycle", compiledParamSpan.GetFloatProperty("m_flEndCycle"));

                                                if (compiledParamSpan.ContainsKey("m_samples"))
                                                {
                                                    paramSpan.Add("m_samples", compiledParamSpan.GetSubCollection("m_samples"));
                                                }

                                                paramSpans.Add(paramSpan);
                                            }

                                            motion.Add("m_paramSpans", MakeArray(paramSpans.ToArray()));
                                        }
                                    }
                                    else
                                    {
                                        motion.Add("m_paramSpans", MakeArray(Array.Empty<KVObject>()));
                                    }

                                    motions.Add(motion);
                                }

                                group.Add("m_motions", MakeArray(motions.ToArray()));
                            }

                            if (compiledGroup.ContainsKey("m_hIsActiveScript") && scriptManager != null)
                            {
                                var scriptHandle = compiledGroup.GetSubCollection("m_hIsActiveScript");
                                var scriptIndex = scriptHandle.GetIntegerProperty("m_id");

                                if (scriptIndex >= 0)
                                {
                                    var scriptInfoArray = scriptManager.GetArray("m_scriptInfo");
                                    if (scriptIndex < scriptInfoArray.Count)
                                    {
                                        var scriptInfo = scriptInfoArray[(int)scriptIndex];
                                        var scriptType = scriptInfo.GetStringProperty("m_eScriptType");
                                        var scriptCode = scriptInfo.GetStringProperty("m_code");

                                        if (scriptType == "ANIMSCRIPT_FUSE_GENERAL" && !string.IsNullOrEmpty(scriptCode))
                                        {
                                            var conditions = ParseConditionExpression(scriptCode);
                                            if (conditions != null && conditions.Count > 0)
                                            {
                                                var conditionContainer = MakeNode("CConditionContainer");
                                                conditionContainer.Add("m_conditions", MakeArray(conditions.ToArray()));
                                                group.Add("m_conditions", conditionContainer);
                                            }
                                        }
                                    }
                                }
                            }

                            groups.Add(group);
                        }

                        node.Add("m_groups", MakeArray(groups.ToArray()));
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

                    node.Add("m_metrics", MakeArray(metrics.ToArray()));
                    continue;
                }
                else if (key == "m_blendCurve")
                {
                    var compiledCurve = subCollection.Value;
                    var blendCurve = MakeNode("CBlendCurve");
                    blendCurve.Add("m_flControlPoint1", compiledCurve.GetFloatProperty("m_flControlPoint1"));
                    blendCurve.Add("m_flControlPoint2", compiledCurve.GetFloatProperty("m_flControlPoint2"));
                    node.Add("m_blendCurve", blendCurve);
                    continue;
                }
                else if (key == "m_distanceScale_Damping")
                {
                    node.Add(key, value);
                    continue;
                }

                if (key is "m_nRandomSeed" or "m_flSampleRate" or "m_bSearchEveryTick" or "m_flSearchInterval"
                    or "m_bSearchWhenClipEnds" or "m_bSearchWhenGoalChanges" or "m_flBlendTime"
                    or "m_flSelectionThreshold" or "m_flReselectionTimeWindow" or "m_bLockClipWhenWaning"
                    or "m_bEnableRotationCorrection" or "m_bGoalAssist" or "m_flGoalAssistDistance"
                    or "m_flGoalAssistTolerance" or "m_bEnableDistanceScaling" or "m_flDistanceScale_OuterRadius"
                    or "m_flDistanceScale_InnerRadius" or "m_flDistanceScale_MaxScale" or "m_flDistanceScale_MinScale"
                    or "m_networkMode")
                {
                    node.Add(key, value);
                    continue;
                }
            }
            else if (className == "CAimCamera")
            {
                if (key == "m_name")
                {
                    var nameValue = value.ToString() ?? "Unnamed";
                    node.Add("m_sName", nameValue);
                    continue;
                }
                else if (key == "m_pChildNode")
                {
                    TryAddInputConnectionFromRef(node, subCollection.Value);
                    continue;
                }
                else if (key == "m_opFixedSettings")
                {
                    var opFixedSettings = subCollection.Value;

                    if (opFixedSettings.ContainsKey("m_nChainIndex"))
                    {
                        var chainIndex = (int)opFixedSettings.GetIntegerProperty("m_nChainIndex");
                        var chainName = GetIKChainName(chainIndex);
                        node.Add("m_ikChain", chainName);
                    }

                    var boneProperties = new[]
                    {
                        ("m_nCameraJointIndex", "m_cameraJointName"),
                        ("m_nPelvisJointIndex", "m_pelvisJointName"),
                        ("m_nClavicleLeftJointIndex", "m_clavicleLeftJointName"),
                        ("m_nClavicleRightJointIndex", "m_clavicleRightJointName"),
                        ("m_nDepenetrationJointIndex", "m_depenetrationJointName")
                    };

                    foreach (var (compiledKey, sourceKey) in boneProperties)
                    {
                        if (opFixedSettings.ContainsKey(compiledKey))
                        {
                            var boneIndex = (int)opFixedSettings.GetIntegerProperty(compiledKey);
                            var boneName = GetBoneName(boneIndex);
                            node.Add(sourceKey, boneName);
                        }
                    }

                    if (opFixedSettings.ContainsKey("m_propJoints"))
                    {
                        var propJointIndices = opFixedSettings.GetIntegerArray("m_propJoints");
                        var propJoints = new List<KVObject>();

                        foreach (var jointIndex in propJointIndices)
                        {
                            var propJoint = new KVObject();
                            var boneName = GetBoneName((int)jointIndex);
                            propJoint.Add("m_jointName", boneName);
                            propJoints.Add(propJoint);
                        }
                        node.Add("m_propJoints", MakeArray(propJoints.ToArray()));
                    }
                    continue;
                }
                else if (key == "m_hParameterPosition")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add("m_parameterNamePosition", ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_hParameterOrientation")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add("m_parameterNameOrientation", ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_hParameterPelvisOffset")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add("m_parameterNamePelvisOffset", ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_hParameterCameraOnly")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add("m_parameterCameraOnly", ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_hParameterCameraClearanceDistance")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add("m_parameterCameraClearanceDistance", ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_hParameterWeaponDepenetrationDistance")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add("m_parameterWeaponDepenetrationDistance", ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_hParameterWeaponDepenetrationDelta")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add("m_parameterWeaponDepenetrationDelta", ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_networkMode")
                {
                    node.Add(key, value);
                    continue;
                }
            }
            else if (className == "CTargetWarp")
            {
                if (key == "m_name")
                {
                    var nameValue = value.ToString() ?? "Unnamed";
                    node.Add("m_sName", nameValue);
                    continue;
                }
                else if (key == "m_pChildNode")
                {
                    TryAddInputConnectionFromRef(node, subCollection.Value);
                    continue;
                }
                else if (key == "m_eAngleMode")
                {
                    node.Add("m_eAngleMode", value);
                    continue;
                }
                else if (key == "m_hTargetPositionParameter")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add("m_targetPositionParamID", ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_hTargetUpVectorParameter")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add("m_targetUpVectorParamID", ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_hTargetFacePositionParameter")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add("m_targetFacePositionParamID", ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_hMoveHeadingParameter")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add("m_moveHeadingParamID", ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_hDesiredMoveHeadingParameter")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add("m_desiredMoveHeadingParamID", ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_eCorrectionMethod")
                {
                    node.Add("m_eCorrectionMethod", value);
                    continue;
                }
                else if (key == "m_eTargetWarpTimingMethod")
                {
                    node.Add("m_eTargetWarpTimingMethod", value);
                    continue;
                }
                else if (key == "m_bTargetFacePositionIsWorldSpace")
                {
                    node.Add("m_bTargetFacePositionIsWorldSpace", value);
                    continue;
                }
                else if (key == "m_bTargetPositionIsWorldSpace")
                {
                    node.Add("m_bTargetPositionIsWorldSpace", value);
                    continue;
                }
                else if (key == "m_bOnlyWarpWhenTagIsFound")
                {
                    node.Add("m_bOnlyWarpWhenTagIsFound", value);
                    continue;
                }
                else if (key == "m_bWarpOrientationDuringTranslation")
                {
                    node.Add("m_bWarpOrientationDuringTranslation", value);
                    continue;
                }
                else if (key == "m_bWarpAroundCenter")
                {
                    node.Add("m_bWarpAroundCenter", value);
                    continue;
                }
                else if (key == "m_flMaxAngle")
                {
                    node.Add("m_flMaxAngle", value);
                    continue;
                }
                else if (key == "m_networkMode")
                {
                    node.Add(key, value);
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
                    var nameValue = value.ToString() ?? "Unnamed";
                    node.Add("m_sName", nameValue);
                    continue;
                }
                else if (key == "m_pChildNode")
                {
                    TryAddInputConnectionFromRef(node, subCollection.Value);
                    continue;
                }
                else if (key == "m_eMode")
                {
                    node.Add("m_eMode", value);
                    continue;
                }
                else if (key == "m_hTargetParam")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add("m_targetParamID", ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_hTargetPositionParam")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add("m_targetPositionParamID", ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_hFallbackTargetPositionParam")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add("m_fallbackTargetPositionParamID", ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_eTargetOffsetMode")
                {
                    node.Add("m_eTargetOffsetMode", value);
                    continue;
                }
                else if (key == "m_flTargetOffset")
                {
                    node.Add("m_flTargetOffset", value);
                    continue;
                }
                else if (key == "m_hTargetOffsetParam")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    node.Add("m_targetOffsetParamID", ParameterIDFromIndex(paramType, paramIndex));
                    continue;
                }
                else if (key == "m_damping")
                {
                    node.Add("m_damping", value);
                    continue;
                }
                else if (key == "m_eRootMotionSource")
                {
                    node.Add("m_eRootMotionSource", value);
                    continue;
                }
                else if (key == "m_flMaxRootMotionScale")
                {
                    node.Add("m_flMaxRootMotionScale", value);
                    continue;
                }
                else if (key == "m_bEnablePreferredRotationDirection")
                {
                    node.Add("m_bEnablePreferredRotationDirection", value);
                    continue;
                }
                else if (key == "m_ePreferredRotationDirection")
                {
                    node.Add("m_ePreferredRotationDirection", value);
                    continue;
                }
                else if (key == "m_flPreferredRotationThreshold")
                {
                    node.Add("m_flPreferredRotationThreshold", value);
                    continue;
                }
                else if (key == "m_networkMode")
                {
                    node.Add(key, value);
                    continue;
                }
            }
            else if (className == "CPairedSequence")
            {
                if (key == "m_name")
                {
                    var nameValue = value.ToString() ?? "Unnamed";
                    node.Add("m_sName", nameValue);
                    continue;
                }
                else if (key == "m_sPairedSequenceRole")
                {
                    node.Add("m_sPairedRole", value);
                    continue;
                }
                else if (key == "m_playbackSpeed")
                {
                    node.Add("m_flPlaybackSpeed", value);
                    continue;
                }
                else if (key == "m_bLoop")
                {
                    node.Add("m_bLoop", value);
                    continue;
                }
                else if (key == "m_networkMode")
                {
                    node.Add(key, value);
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
                    var nameValue = value.ToString() ?? "Unnamed";
                    node.Add("m_sName", nameValue);
                    continue;
                }
                else if (key == "m_pChildNode")
                {
                    TryAddInputConnectionFromRef(node, subCollection.Value);
                    continue;
                }
                else if (key == "m_opFixedData")
                {
                    var opFixedData = subCollection.Value;

                    if (opFixedData.ContainsKey("m_boneIndex"))
                    {
                        var boneIndex = (int)opFixedData.GetIntegerProperty("m_boneIndex");
                        var boneName = GetBoneName(boneIndex);
                        node.Add("m_boneName", boneName);
                    }

                    var targetSettings = new KVObject();

                    string targetSource;
                    if (opFixedData.GetIntegerProperty("m_bBoneTarget", 0) > 0)
                    {
                        targetSource = "Bone";
                    }
                    else
                    {
                        targetSource = "AnimgraphParameter";
                    }
                    targetSettings.Add("m_TargetSource", targetSource);

                    var boneNameAndIndex = new KVObject();
                    if (opFixedData.ContainsKey("m_boneTargetIndex"))
                    {
                        var boneTargetIndex = (int)opFixedData.GetIntegerProperty("m_boneTargetIndex");
                        if (boneTargetIndex != -1)
                        {
                            var targetBoneName = GetBoneName(boneTargetIndex);
                            boneNameAndIndex.Add("m_Name", targetBoneName);
                        }
                    }
                    targetSettings.Add("m_Bone", boneNameAndIndex);

                    string targetCoordSystem;
                    if (opFixedData.GetIntegerProperty("m_bWorldCoodinateTarget", 0) > 0)
                    {
                        targetCoordSystem = "World";
                    }
                    else
                    {
                        targetCoordSystem = "Model";
                    }
                    targetSettings.Add("m_TargetCoordSystem", targetCoordSystem);

                    node.Add("m_TargetSettings", targetSettings);

                    if (opFixedData.ContainsKey("m_bMatchTargetOrientation"))
                    {
                        node.Add("m_bMatchTargetOrientation", opFixedData.GetIntegerProperty("m_bMatchTargetOrientation") > 0);
                    }

                    continue;
                }
                else if (key == "m_hParameterPosition")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    var paramIdValue = ParameterIDFromIndex(paramType, paramIndex);

                    if (!node.ContainsKey("m_TargetSettings"))
                    {
                        var targetSettings = new KVObject();
                        targetSettings.Add("m_TargetSource", "AnimgraphParameter");
                        targetSettings.Add("m_Bone", KVObject.Null());
                        targetSettings.Add("m_TargetCoordSystem", "Model");
                        node.Add("m_TargetSettings", targetSettings);
                    }

                    var targetSettingsObj = node.GetSubCollection("m_TargetSettings");
                    targetSettingsObj.Add("m_AnimgraphParameterNamePosition", paramIdValue);

                    continue;
                }
                else if (key == "m_hParameterOrientation")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    var paramIdValue = ParameterIDFromIndex(paramType, paramIndex);

                    if (!node.ContainsKey("m_TargetSettings"))
                    {
                        var targetSettings = new KVObject();
                        targetSettings.Add("m_TargetSource", "AnimgraphParameter");
                        targetSettings.Add("m_Bone", KVObject.Null());
                        targetSettings.Add("m_TargetCoordSystem", "Model");
                        node.Add("m_TargetSettings", targetSettings);
                    }

                    var targetSettingsObj = node.GetSubCollection("m_TargetSettings");
                    targetSettingsObj.Add("m_AnimgraphParameterNameOrientation", paramIdValue);

                    continue;
                }
                else if (key == "m_networkMode")
                {
                    node.Add(key, value);
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
                        var compiledTagSpans = compiledNode.GetArray("m_tags");
                        var tagSpans = new List<KVObject>();

                        foreach (var compiledTagSpan in compiledTagSpans)
                        {
                            var tagIndex = compiledTagSpan.GetIntegerProperty("m_tagIndex");
                            var startCycle = compiledTagSpan.GetFloatProperty("m_startCycle");
                            var endCycle = compiledTagSpan.GetFloatProperty("m_endCycle");
                            var duration = endCycle - startCycle;
                            var tagId = -1L;

                            if (tagIndex >= 0 && tagIndex < Tags.Count)
                            {
                                tagId = Tags[(int)tagIndex].GetSubCollection("m_tagID").GetIntegerProperty("m_id");
                            }

                            var tagSpan = MakeNode("CAnimTagSpan");
                            tagSpan.Add("m_id", MakeNodeIdObjectValue(tagId));
                            tagSpan.Add("m_fStartCycle", startCycle);
                            tagSpan.Add("m_fDuration", duration);
                            tagSpans.Add(tagSpan);
                        }

                        node.Add("m_tagSpans", MakeArray(tagSpans.ToArray()));
                    }
                    catch
                    {
                        node.Add("m_tagSpans", MakeArray(Array.Empty<KVObject>()));
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
                        node.Add(key, MakeArray(Array.Empty<KVObject>()));
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

                    if (compiledParamSpans is not null && compiledParamSpans.ContainsKey("m_spans"))
                    {
                        var compiledSpans = compiledParamSpans.GetArray("m_spans");
                        var paramSpans = new List<KVObject>();

                        foreach (var compiledSpan in compiledSpans)
                        {
                            var paramSpan = MakeNode("CAnimParamSpan");

                            if (compiledSpan.ContainsKey("m_samples"))
                            {
                                paramSpan.Add("m_samples", compiledSpan.GetSubCollection("m_samples"));
                            }

                            if (compiledSpan.ContainsKey("m_hParam"))
                            {
                                var paramHandle = compiledSpan.GetSubCollection("m_hParam");
                                var paramType = paramHandle.GetStringProperty("m_type");
                                var paramIndex = paramHandle.GetIntegerProperty("m_index");
                                var paramIdValue = ParameterIDFromIndex(paramType, paramIndex);
                                paramSpan.Add("m_id", paramIdValue);
                            }

                            if (compiledSpan.ContainsKey("m_flStartCycle"))
                            {
                                paramSpan.Add("m_flStartCycle", compiledSpan.GetFloatProperty("m_flStartCycle"));
                            }

                            if (compiledSpan.ContainsKey("m_flEndCycle"))
                            {
                                paramSpan.Add("m_flEndCycle", compiledSpan.GetFloatProperty("m_flEndCycle"));
                            }

                            paramSpans.Add(paramSpan);
                        }

                        node.Add("m_paramSpans", MakeArray(paramSpans.ToArray()));
                    }
                }
                catch
                {
                    node.Add("m_paramSpans", MakeArray(Array.Empty<KVObject>()));
                }

                continue;
            }

            if (key == "m_paramIndex" || key == "m_hParam")
            {
                var paramRef = subCollection.Value;
                var paramType = paramRef.GetStringProperty("m_type");
                var paramIndex = paramRef.GetIntegerProperty("m_index");
                node.Add("m_param", ParameterIDFromIndex(paramType, paramIndex));
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
            node.Add("m_items", MakeArray(footPinningItems.ToArray()));
        }
        return node;
    }

    private KVObject ParameterIDFromIndex(string paramType, long paramIndex, bool requireFloat = false)
    {
        if (paramIndex == 255)
        {
            return MakeNodeIdObjectValue(-1);
        }

        var uncompiledType = paramType.Replace("ANIMPARAM_", "", StringComparison.Ordinal);
        var currentCount = 0;

        for (var i = 0; i < Parameters.Count; i++)
        {
            var parameter = Parameters[i];
            var paramClass = parameter.GetStringProperty("_class");
            var paramTypeName = paramClass switch
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

            if (paramTypeName == uncompiledType)
            {
                if (currentCount == paramIndex)
                {
                    var id = parameter.GetSubCollection("m_id").GetIntegerProperty("m_id");
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
                    var id = parameter.GetSubCollection("m_id").GetIntegerProperty("m_id");
                    return MakeNodeIdObjectValue(id);
                }
            }
        }
        return MakeNodeIdObjectValue(-1);
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
