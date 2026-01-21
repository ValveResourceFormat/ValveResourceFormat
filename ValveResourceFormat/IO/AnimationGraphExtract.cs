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
    private KVObject Graph => resourceData.Data;
    private readonly string? outputFileName;
    private int nodePositionOffset;
    private readonly IFileLoader fileLoader;
    private Dictionary<int, string>? weightListNamesCache;
    private Dictionary<int, string>? sequenceNamesCache;
    private Dictionary<long, KVObject>? compiledNodeIndexMap;
    private Dictionary<long, List<long>>? groupHierarchies;
    private Dictionary<long, long>? nodeToGroupMap;
    private Dictionary<long, long>? nodeIndexToIdMap;

    private class GroupNode
    {
        public long GroupId { get; set; }
        public List<long> ChildNodeIds { get; set; } = [];
        public List<GroupNode> ChildGroups { get; set; } = [];
        public long? ParentGroupId { get; set; }
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
                ? resourceData.GetKV3File().ToString()
                : ToEditableAnimGraphVersion19()),
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
    /// Builds the group hierarchy from compiled node paths.
    /// </summary>
    private void BuildGroupHierarchy(KVObject[] compiledNodes)
    {
        compiledNodeIndexMap = [];
        groupHierarchies = [];
        nodeToGroupMap = [];
        nodeIndexToIdMap = [];

        for (var i = 0; i < compiledNodes.Length; i++)
        {
            var compiledNode = compiledNodes[i];
            var nodePath = compiledNode.GetSubCollection("m_nodePath");

            if (nodePath is null)
            {
                continue;
            }

            var path = nodePath.GetArray("m_path");
            var count = nodePath.GetIntegerProperty("m_nCount");

            if (count <= 0)
            {
                continue;
            }

            var nodeId = path[(int)count - 1].GetIntegerProperty("m_id");
            compiledNodeIndexMap[nodeId] = compiledNode;
            nodeIndexToIdMap[i] = nodeId;

            if (count > 1)
            {
                var groupPath = new List<long>();
                for (var j = 0; j < count - 1; j++)
                {
                    groupPath.Add(path[j].GetIntegerProperty("m_id"));
                }
                groupHierarchies[nodeId] = groupPath;
                var parentGroupId = groupPath[^1];
                if (parentGroupId != 0)
                {
                    nodeToGroupMap[nodeId] = parentGroupId;
                }
            }
        }
    }

    private Dictionary<long, GroupNode> BuildGroupHierarchyTree()
    {
        var groupTree = new Dictionary<long, GroupNode>();

        if (groupHierarchies == null)
        {
            return groupTree;
        }
        foreach (var (nodeId, groupPath) in groupHierarchies)
        {
            for (var i = 0; i < groupPath.Count; i++)
            {
                var groupId = groupPath[i];
                if (!groupTree.TryGetValue(groupId, out var groupNode))
                {
                    groupNode = new GroupNode { GroupId = groupId };
                    groupTree[groupId] = groupNode;
                }
                if (i > 0)
                {
                    var parentGroupId = groupPath[i - 1];
                    groupNode.ParentGroupId = parentGroupId;
                    if (groupTree.TryGetValue(parentGroupId, out var parentGroup))
                    {
                        if (!parentGroup.ChildGroups.Any(g => g.GroupId == groupId))
                        {
                            parentGroup.ChildGroups.Add(groupNode);
                        }
                    }
                }
            }
        }
        foreach (var (nodeId, groupPath) in groupHierarchies)
        {
            if (groupPath.Count > 0)
            {
                var immediateParentId = groupPath[^1];
                if (groupTree.TryGetValue(immediateParentId, out var parentGroup))
                {
                    parentGroup.ChildNodeIds.Add(nodeId);
                }
            }
        }
        if (nodeToGroupMap != null)
        {
            foreach (var (nodeId, groupId) in nodeToGroupMap)
            {
                if (!groupHierarchies.ContainsKey(nodeId))
                {
                    if (groupTree.TryGetValue(groupId, out var parentGroup))
                    {
                        parentGroup.ChildNodeIds.Add(nodeId);
                    }
                }
            }
        }
        return groupTree;
    }

    /// <summary>
    /// Creates a group node from its hierarchy.
    /// </summary>
    private KVObject CreateGroupNode(GroupNode groupNode, Dictionary<long, KVObject> allCompiledNodes)
    {
        var groupId = groupNode.GroupId;
        var groupNodeKV = MakeNode("CGroupAnimNode");
        groupNodeKV.AddProperty("m_sName", $"Group_{groupId}");
        groupNodeKV.AddProperty("m_vecPosition", MakeVector2(0.0f, 0.0f));
        groupNodeKV.AddProperty("m_nNodeID", MakeNodeIdObjectValue(groupId));
        groupNodeKV.AddProperty("m_networkMode", "ServerAuthoritative");

        var inputNodeId = GenerateGroupNodeId(groupId, true);
        var outputNodeId = GenerateGroupNodeId(groupId, false);

        var nodeManager = MakeListNode("CAnimNodeManager", "m_nodes");
        var inputConnectionMap = new List<KVObject>();
        var inputConnectionMapDict = new Dictionary<long, KVValue>();

        var groupNodes = new Dictionary<long, KVObject>();

        foreach (var childNodeId in groupNode.ChildNodeIds)
        {
            if (allCompiledNodes.TryGetValue(childNodeId, out var compiledNode))
            {
                var uncompiledNode = ConvertToUncompiled(compiledNode);
                uncompiledNode.AddProperty("m_nNodeID", MakeNodeIdObjectValue(childNodeId));
                groupNodes[childNodeId] = uncompiledNode;
            }
        }

        foreach (var childGroup in groupNode.ChildGroups)
        {
            var nestedGroupNode = CreateGroupNode(childGroup, allCompiledNodes);
            nodeManager.Children.AddItem(CreateNodeManagerItem(childGroup.GroupId, nestedGroupNode));
        }

        var groupInputNode = CreateGroupInputNode(inputNodeId, groupNodes, inputConnectionMapDict, groupNode.ChildGroups);
        var groupOutputNode = CreateGroupOutputNode(outputNodeId, groupNodes, groupId, groupNode.ChildGroups);

        nodeManager.Children.AddItem(CreateNodeManagerItem(inputNodeId, groupInputNode));
        nodeManager.Children.AddItem(CreateNodeManagerItem(outputNodeId, groupOutputNode));

        foreach (var (nodeId, nodeData) in groupNodes)
        {
            nodeManager.Children.AddItem(CreateNodeManagerItem(nodeId, nodeData));
        }

        foreach (var (keyId, connection) in inputConnectionMapDict)
        {
            var connectionMapEntry = new KVObject(null, 2);
            connectionMapEntry.AddProperty("key", MakeNodeIdObjectValue(keyId));
            connectionMapEntry.AddProperty("value", connection);
            inputConnectionMap.Add(connectionMapEntry);
        }

        groupNodeKV.AddProperty("m_inputNodeID", MakeNodeIdObjectValue(inputNodeId));
        groupNodeKV.AddProperty("m_outputNodeID", MakeNodeIdObjectValue(outputNodeId));
        groupNodeKV.AddProperty("m_nodeMgr", nodeManager.Node);
        groupNodeKV.AddProperty("m_inputConnectionMap", KVValue.MakeArray(inputConnectionMap.ToArray()));

        return groupNodeKV;
    }

    private KVObject CreateGroupInputNode(long nodeId, Dictionary<long, KVObject> groupNodes,
    Dictionary<long, KVValue> inputConnectionMap, List<GroupNode> childGroups)
    {
        var inputNode = MakeNode("CGroupInputAnimNode");
        inputNode.AddProperty("m_sName", "Group Input");
        inputNode.AddProperty("m_vecPosition", MakeVector2(-200.0f, 0.0f));
        inputNode.AddProperty("m_nNodeID", MakeNodeIdObjectValue(nodeId));
        inputNode.AddProperty("m_networkMode", "ServerAuthoritative");

        var proxyItems = new List<KVObject>();
        var proxyIndex = 1;

        foreach (var (childNodeId, childNode) in groupNodes)
        {
            var externalConnections = FindExternalConnections(childNode, childNodeId);

            foreach (var externalConn in externalConnections)
            {
                var proxyName = $"Input {proxyIndex++}";
                var outputId = GenerateProxyId(nodeId, proxyIndex);

                var proxyItem = CreateProxyItem(proxyName, outputId);
                inputConnectionMap[outputId] = externalConn;
                proxyItems.Add(proxyItem);
            }
        }

        foreach (var childGroup in childGroups)
        {
            var proxyName = $"Group {childGroup.GroupId}";
            var outputId = GenerateProxyId(nodeId, proxyIndex++);

            var proxyItem = CreateProxyItem(proxyName, outputId);

            var childGroupInputId = GenerateGroupNodeId(childGroup.GroupId, true);
            var connection = MakeInputConnection(childGroupInputId);
            inputConnectionMap[outputId] = connection;

            proxyItems.Add(proxyItem);
        }

        if (proxyItems.Count == 0)
        {
            var proxyItem = CreateProxyItem("Input 1", GenerateProxyId(nodeId, 1));
            proxyItems.Add(proxyItem);
        }

        inputNode.AddProperty("m_proxyItems", KVValue.MakeArray(proxyItems.ToArray()));
        return inputNode;
    }

    private static KVObject CreateGroupOutputNode(long nodeId, Dictionary<long, KVObject> groupNodes, long groupId, List<GroupNode> childGroups)
    {
        var outputNode = MakeNode("CGroupOutputAnimNode");
        outputNode.AddProperty("m_sName", "Group Output");
        outputNode.AddProperty("m_vecPosition", MakeVector2(200.0f, 0.0f));
        outputNode.AddProperty("m_nNodeID", MakeNodeIdObjectValue(nodeId));
        outputNode.AddProperty("m_networkMode", "ServerAuthoritative");

        var proxyItems = new List<KVObject>();

        var proxyItem = CreateProxyItem("Output 1", GenerateProxyId(nodeId, 1));
        proxyItems.Add(proxyItem);

        outputNode.AddProperty("m_proxyItems", KVValue.MakeArray(proxyItems.ToArray()));
        return outputNode;
    }

    private List<KVValue> FindExternalConnections(KVObject node, long nodeId)
    {
        var externalConnections = new List<KVValue>();

        var connectionKeys = new[]
        {
            "m_children", "m_pChildNode", "m_pChild1", "m_pChild2",
            "m_baseInput", "m_additiveInput", "m_baseInputConnection",
            "m_subtractInputConnection", "m_inputConnection1", "m_inputConnection2",
            "m_inputConnection"
        };

        foreach (var key in connectionKeys)
        {
            if (node.Properties.TryGetValue(key, out var value))
            {
                if (value.Value is KVObject connectionObj)
                {
                    long connectedNodeId = -1;

                    if (connectionObj.ContainsKey("m_nodeID"))
                    {
                        var nodeIdObj = connectionObj.GetSubCollection("m_nodeID");
                        if (nodeIdObj != null && nodeIdObj.ContainsKey("m_id"))
                        {
                            connectedNodeId = nodeIdObj.GetIntegerProperty("m_id");
                        }
                    }
                    else if (connectionObj.ContainsKey("m_nodeIndex") && nodeIndexToIdMap != null)
                    {
                        var nodeIndex = connectionObj.GetIntegerProperty("m_nodeIndex");
                        nodeIndexToIdMap.TryGetValue(nodeIndex, out connectedNodeId);
                    }

                    if (connectedNodeId != -1 && connectedNodeId != nodeId)
                    {
                        if (nodeToGroupMap != null &&
                            nodeToGroupMap.TryGetValue(nodeId, out var nodeGroup) &&
                            nodeToGroupMap.TryGetValue(connectedNodeId, out var connectedNodeGroup) &&
                            nodeGroup != connectedNodeGroup)
                        {
                            externalConnections.Add(value);
                        }
                    }
                }
                else if (value.Value is KVObject[] connections)
                {
                    foreach (var conn in connections)
                    {
                        long connectedNodeId = -1;

                        if (conn.ContainsKey("m_nodeID"))
                        {
                            var nodeIdObj = conn.GetSubCollection("m_nodeID");
                            if (nodeIdObj != null && nodeIdObj.ContainsKey("m_id"))
                            {
                                connectedNodeId = nodeIdObj.GetIntegerProperty("m_id");
                            }
                        }
                        else if (conn.ContainsKey("m_nodeIndex") && nodeIndexToIdMap != null)
                        {
                            var nodeIndex = conn.GetIntegerProperty("m_nodeIndex");
                            nodeIndexToIdMap.TryGetValue(nodeIndex, out connectedNodeId);
                        }

                        if (connectedNodeId != -1 && connectedNodeId != nodeId)
                        {
                            if (nodeToGroupMap != null &&
                                nodeToGroupMap.TryGetValue(nodeId, out var nodeGroup) &&
                                nodeToGroupMap.TryGetValue(connectedNodeId, out var connectedNodeGroup) &&
                                nodeGroup != connectedNodeGroup)
                            {
                                externalConnections.Add(new KVValue(conn));
                            }
                        }
                    }
                }
            }
        }
        return externalConnections;
    }

    private static KVObject CreateProxyItem(string name, long outputId)
    {
        var proxyItem = new KVObject(null, 3);
        proxyItem.AddProperty("m_name", name);
        proxyItem.AddProperty("m_outputID", MakeNodeIdObjectValue(outputId));

        var inputConnection = new KVObject(null, 2);
        inputConnection.AddProperty("m_nodeID", MakeNodeIdObjectValue(-1));
        inputConnection.AddProperty("m_outputID", MakeNodeIdObjectValue(-1));
        proxyItem.AddProperty("m_inputConnection", inputConnection);
        return proxyItem;
    }

    private static KVObject CreateNodeManagerItem(long keyId, KVObject nodeData)
    {
        var managerItem = new KVObject(null, 2);
        managerItem.AddProperty("key", MakeNodeIdObjectValue(keyId));
        managerItem.AddProperty("value", nodeData);
        return managerItem;
    }

    private static long GenerateGroupNodeId(long baseId, bool isInput)
    {
        var hash = (int)baseId;
        var modifier = isInput ? 0x10000000L : 0x20000000L;
        return (baseId & 0x0FFFFFFF) | modifier;
    }
    private static long GenerateProxyId(long baseId, int index)
    {
        return (baseId & 0x0FFFFFFF) | (0x30000000L + (index << 16));
    }
    /// <summary>
    /// Converts the compiled animation graph to editable version 19 format.
    /// </summary>
    /// <returns>The animation graph as a <see cref="KV3File"/> string in version 19 format.</returns>
    public string ToEditableAnimGraphVersion19()
    {
        var data = Graph.GetSubCollection("m_pSharedData");
        var compiledNodes = data.GetArray("m_nodes");

        BuildGroupHierarchy(compiledNodes);
        var groupTree = BuildGroupHierarchyTree();

        var tagManager = data.GetSubCollection("m_pTagManagerUpdater");
        var paramListUpdater = data.GetSubCollection("m_pParamListUpdater");
        var scriptManager = data.GetSubCollection("m_scriptManager");

        if (data.GetArray("m_managers") is KVObject[] managers)
        {
            tagManager = managers.FirstOrDefault(m => m.GetProperty<string>("_class") == "CAnimTagManagerUpdater");
            paramListUpdater = managers.FirstOrDefault(m => m.GetProperty<string>("_class") == "CAnimParameterListUpdater");
            scriptManager = managers.FirstOrDefault(m => m.GetProperty<string>("_class") == "CAnimScriptManager");
        }

        if (tagManager is null || paramListUpdater is null)
        {
            throw new InvalidDataException("Missing tag manager or parameter list updater");
        }

        Tags = tagManager.GetArray("m_tags");
        Parameters = paramListUpdater.GetArray("m_parameters");

        var clipDataManager = tagManager.ContainsKey("sequence_tag_spans")
            ? ConvertClipDataManager(tagManager.GetArray("sequence_tag_spans"))
            : MakeNode("CAnimClipDataManager", ("m_itemTable", new KVObject(null, isArray: false, 0)));

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

        var allCompiledNodes = compiledNodeIndexMap ?? [];

        var rootGroups = groupTree.Values
            .Where(g => !g.ParentGroupId.HasValue || !groupTree.ContainsKey(g.ParentGroupId.Value))
            .ToList();

        foreach (var rootGroup in rootGroups)
        {
            var groupNode = CreateGroupNode(rootGroup, allCompiledNodes);
            nodeManager.Children.AddItem(CreateNodeManagerItem(rootGroup.GroupId, groupNode));
        }

        foreach (var compiledNode in compiledNodes)
        {
            var nodePath = compiledNode.GetSubCollection("m_nodePath");
            if (nodePath?.GetIntegerProperty("m_nCount") == 1)
            {
                var path = nodePath.GetArray("m_path");
                var nodeId = path[0].GetIntegerProperty("m_id");

                if (!nodeToGroupMap?.ContainsKey(nodeId) == true &&
                    !groupTree.ContainsKey(nodeId) &&
                    !groupTree.Values.Any(g => g.ChildNodeIds.Contains(nodeId)))
                {
                    var uncompiledNode = ConvertToUncompiled(compiledNode);
                    uncompiledNode.AddProperty("m_nNodeID", MakeNodeIdObjectValue(nodeId));
                    nodeManager.Children.AddItem(CreateNodeManagerItem(nodeId, uncompiledNode));
                }
            }
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
                ("m_modelName", Graph.GetProperty<string>("m_modelName")),
            ]);

        return new KV3File(kv, format: KV3IDLookup.Get("animgraph19")).ToString();
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
            var modelResource = fileLoader.LoadFileCompiled(modelName);

            if (modelResource is null)
            {
                weightListNames[0] = "default";
                return weightListNames;
            }

            var aseqData = GetAseqDataFromResource(modelResource);

            if (aseqData is not null)
            {
                var localBoneMaskArray = aseqData.GetArray("m_localBoneMaskArray");

                if (localBoneMaskArray is not null)
                {
                    for (var i = 0; i < localBoneMaskArray.Length; i++)
                    {
                        var boneMask = localBoneMaskArray[i];
                        var weightListName = boneMask.GetStringProperty("m_sName");
                        weightListNames[i] = !string.IsNullOrEmpty(weightListName)
                            ? weightListName
                            : i == 0 ? "default" : $"weightlist_{i}";
                    }

                    if (weightListNames.Count == 0 && localBoneMaskArray.Length == 0)
                    {
                        weightListNames[0] = "default";
                    }
                }
                else
                {
                    weightListNames[0] = "default";
                }
            }
            else
            {
                weightListNames[0] = "default";
            }
        }
        catch
        {
            weightListNames[0] = "default";
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
            var modelResource = fileLoader.LoadFileCompiled(modelName);
            if (modelResource is null)
            {
                return sequenceNames;
            }
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
                            sequenceNames[i] = sequenceName;
                        }
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
            kvData.GetStringProperty("m_sName")?.Contains("embedded_sequence_data") == true)
        {
            return kvData;
        }

        return kvData.ContainsKey("ASEQ")
            ? kvData.GetSubCollection("ASEQ")
            : null;
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

        var paramIdObject = (KVObject)paramIdValue.Value!;
        var paramId = paramIdObject.GetIntegerProperty("m_id");
        var source = paramId == uint.MaxValue ? "Constant" : "Parameter";

        blendDuration.AddProperty("m_eSource", source);

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
    private KVObject[] ConvertStateMachine(KVObject compiledStateMachine, KVObject[]? stateDataArray, KVObject[]? transitionDataArray, bool isComponent = false)
    {
        var compiledStates = compiledStateMachine.GetArray("m_states");
        var compiledTransitions = compiledStateMachine.GetArray("m_transitions");
        var states = new KVObject[compiledStates.Length];

        var startStateIndex = -1;
        for (var i = 0; i < compiledStates.Length; i++)
        {
            if (compiledStates[i].GetIntegerProperty("m_bIsStartState") > 0)
            {
                startStateIndex = i;
                break;
            }
        }

        for (var i = 0; i < compiledStates.Length; i++)
        {
            var compiledState = compiledStates[i];
            var stateData = stateDataArray != null && i < stateDataArray.Length ? stateDataArray[i] : null;

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
                var positionFromStart = i > startStateIndex ? i - startStateIndex : i + (compiledStates.Length - startStateIndex);
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
                    if (transitionIndex < 0 || transitionIndex >= compiledTransitions.Length)
                    {
                        continue;
                    }

                    var compiledTransition = compiledTransitions[transitionIndex];
                    var transitionData = transitionDataArray != null && transitionIndex < transitionDataArray.Length
                        ? transitionDataArray[transitionIndex]
                        : null;

                    var transitionNodeType = isComponent ? "CAnimComponentStateTransition" : "CAnimNodeStateTransition";
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
                        var nodeIndex = compiledTransition.GetIntegerProperty("m_nodeIndex");
                        if (nodeIndexToIdMap?.TryGetValue(nodeIndex, out var childNodeId) == true)
                        {
                            AddInputConnection(transitionNode, childNodeId);
                        }

                        if (transitionData is not null)
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
                                    _ => "Beginning",
                                };

                                transitionNode.AddProperty("m_resetCycleOption", resetOptionStr);
                            }

                            if (transitionData.ContainsKey("m_blendDuration"))
                            {
                                var blendDuration = transitionData.GetSubCollection("m_blendDuration");

                                if (blendDuration is not null)
                                {
                                    var convertedBlendDuration = ConvertBlendDuration(blendDuration);
                                    transitionNode.AddProperty("m_blendDuration", convertedBlendDuration);
                                }
                            }

                            if (transitionData.ContainsKey("m_resetCycleValue"))
                            {
                                var resetcycleValue = transitionData.GetSubCollection("m_resetCycleValue");

                                if (resetcycleValue is not null)
                                {
                                    var convertedfixedcycleValue = ConvertBlendDuration(resetcycleValue);
                                    transitionNode.AddProperty("m_flFixedCycleValue", convertedfixedcycleValue);
                                }
                            }

                            if (transitionData.ContainsKey("m_curve"))
                            {
                                var compiledCurve = transitionData.GetSubCollection("m_curve");
                                var blendCurve = MakeNode("CBlendCurve");

                                blendCurve.AddProperty("m_flControlPoint1",
                                    compiledCurve.ContainsKey("m_flControlPoint1")
                                        ? compiledCurve.GetFloatProperty("m_flControlPoint1")
                                        : 0.0f);

                                blendCurve.AddProperty("m_flControlPoint2",
                                    compiledCurve.ContainsKey("m_flControlPoint2")
                                        ? compiledCurve.GetFloatProperty("m_flControlPoint2")
                                        : 1.0f);

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

            if (!isComponent && stateData is not null)
            {
                var childRef = stateData.GetSubCollection("m_pChild");
                var nodeIndex = childRef.GetIntegerProperty("m_nodeIndex");
                if (nodeIndexToIdMap?.TryGetValue(nodeIndex, out var childNodeId) == true)
                {
                    AddInputConnection(stateNode, childNodeId);
                }
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

        // Action Component
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
                        if (actionKey == "_class")
                        {
                            continue;
                        }

                        if (actionKey == "m_nTagIndex")
                        {
                            var tagIndex = action.GetIntegerProperty("m_nTagIndex");
                            var tagId = tagIndex != -1
                                ? Tags[tagIndex].GetSubCollection("m_tagID").GetIntegerProperty("m_id")
                                : -1;

                            newAction.AddProperty("m_tag", MakeNodeIdObjectValue(tagId));
                            continue;
                        }

                        if (actionKey == "m_hParam")
                        {
                            var paramRef = (KVObject)actionValue.Value!;
                            var paramType = paramRef.GetStringProperty("m_type");
                            var paramIndex = paramRef.GetIntegerProperty("m_index");
                            newAction.AddProperty("m_param", ParameterIDFromIndex(paramType, paramIndex));
                            continue;
                        }

                        if (actionKey is "m_hScript" or "m_eParamType")
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

        // Look Component
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

        // Slope Component
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

        // Ragdoll Component
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
                    var weightsArray = new KVObject(null, isArray: true, weightArray.Length);

                    for (var i = 0; i < weightArray.Length; i++)
                    {
                        var weightDefinition = new KVObject(null, 2);
                        var boneName = i < boneNames.Length ? boneNames[i] : $"bone_{i}";

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

        // Damped Value Component
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

                    var valueType = paramInType == "ANIMPARAM_VECTOR" ? "VectorParameter" : "FloatParameter";
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

        // VR Input Component
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
                "m_FingerSplay_Ring_Pinky",
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

        // State Machine Component
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

            if (key == "m_motors")
            {
                var motors = compiledComponent.GetArray("m_motors");
                var convertedMotors = motors.Select(motor =>
                {
                    var motorClassName = motor.GetStringProperty("_class");
                    var newMotorClassName = motorClassName.Replace("Updater", string.Empty, StringComparison.Ordinal);
                    var newMotor = MakeNode(newMotorClassName);

                    foreach (var (motorKey, motorValue) in motor.Properties)
                    {
                        if (motorKey == "_class")
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

    private KVObject ConvertToUncompiled(KVObject compiledNode)
    {
        var className = compiledNode.GetProperty<string>("_class");
        className = className.Replace("UpdateNode", string.Empty, StringComparison.Ordinal);

        var newClass = className + "AnimNode";
        var node = MakeNode(newClass);

        var children = compiledNode.GetArray("m_children");
        var inputNodeIds = children?.Select(child =>
        {
            var nodeIndex = child.GetIntegerProperty("m_nodeIndex");
            return nodeIndexToIdMap?.TryGetValue(nodeIndex, out var nodeId) == true ? nodeId : -1L;
        }).Where(id => id != -1).ToArray();

        // TODO: Preferably the node position algorithm should be similar to the AG2 graph viewer node placement algorithm
        // instead of random + left shift.

        if (className == "CRoot")
        {
            node.AddProperty("m_vecPosition", MakeVector2(0.0f, 0.0f));
            nodePositionOffset = 0;
        }
        else
        {
            var xPosition = -150.0f * (nodePositionOffset + 1);
            var yPosition = 100.0f * (nodePositionOffset + 1);
            var random = new Random(nodePositionOffset);
            yPosition += random.Next(-50, 51);
            node.AddProperty("m_vecPosition", MakeVector2(xPosition, yPosition));
            nodePositionOffset++;
        }

        foreach (var (key, value) in compiledNode.Properties)
        {
            if (key is "_class" or "m_nodePath")
            {
                continue;
            }

            var newKey = key;
            var subCollection = new Lazy<KVObject>(() => (KVObject)value.Value!);

            // Common remapped key
            if (key == "m_name" && className is "CSequence" or "CChoice" or "CSelector" or "CStateMachine" or "CRoot" or "CAdd" or "CSubtract" or "CMover")
            {
                newKey = "m_sName";
            }
            if (key is "m_pChildNode" or "m_pChild1" or "m_pChild2" or "m_pChild" && value.Value is KVObject childRef)
            {
                var nodeIndex = childRef.GetIntegerProperty("m_nodeIndex");
                if (nodeIndexToIdMap?.TryGetValue(nodeIndex, out var nodeId) == true)
                {
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
                        node.AddProperty(connectionKey, connection);
                        continue;
                    }
                }
            }
            if (key == "m_hSequence")
            {
                var sequenceIndex = compiledNode.GetIntegerProperty("m_hSequence");
                var sequenceName = GetSequenceName(sequenceIndex);
                node.AddProperty("m_sequenceName", sequenceName);
                continue;
            }

            if (className == "CRoot")
            {
                // Get the input connection of the final pose (root node)
                if (key == "m_pChildNode")
                {
                    var finalNodeInputIndex = subCollection.Value.GetIntegerProperty("m_nodeIndex");
                    AddInputConnection(node, finalNodeInputIndex);
                    continue;
                }
            }
            else if (className == "CSelector")
            {
                if (key == "m_eTagBehavior")
                {
                    newKey = "m_tagBehavior";
                }
                else if (key == "m_hParameter")
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
                        node.AddProperty("m_selectionSource", selectionSource);
                        node.AddProperty($"m_{source.ToLowerInvariant()}ParamID", ParameterIDFromIndex(paramType, paramIndex));
                    }
                    continue;
                }
                else if (key == "m_flBlendTime")
                {
                    var convertedBlendDuration = ConvertBlendDuration(subCollection.Value);
                    node.AddProperty("m_blendDuration", convertedBlendDuration);
                    continue;
                }
                else if (key == "m_blendCurve")
                {
                    var compiledCurve = subCollection.Value;
                    var blendCurve = MakeNode("CBlendCurve");
                    blendCurve.AddProperty("m_flControlPoint1",
                        compiledCurve.ContainsKey("m_flControlPoint1")
                            ? compiledCurve.GetFloatProperty("m_flControlPoint1")
                            : 0.0f);
                    blendCurve.AddProperty("m_flControlPoint2",
                        compiledCurve.ContainsKey("m_flControlPoint2")
                            ? compiledCurve.GetFloatProperty("m_flControlPoint2")
                            : 1.0f);
                    node.AddProperty("m_blendCurve", blendCurve);
                    continue;
                }
                else if (key == "m_nTagIndex")
                {
                    var tagIndex = compiledNode.GetIntegerProperty("m_nTagIndex");
                    var tagId = -1L;

                    if (tagIndex >= 0 && tagIndex < Tags.Length)
                    {
                        tagId = Tags[tagIndex].GetSubCollection("m_tagID").GetIntegerProperty("m_id");
                    }

                    node.AddProperty("m_tag", MakeNodeIdObjectValue(tagId));
                    if (tagIndex != -1)
                    {
                        if (!node.Properties.ContainsKey("m_selectionSource"))
                        {
                            node.AddProperty("m_selectionSource", "SelectionSource_Tag");
                        }
                    }
                    continue;
                }
                else if (key == "m_bResetOnChange" || key == "m_bLockWhenWaning" || key == "m_bSyncCyclesOnChange")
                {
                    node.AddProperty(key, value);
                    continue;
                }
            }
            else if (className == "CMover")
            {
                if (key == "m_pChildNode")
                {
                    var childNodeId = subCollection.Value.GetIntegerProperty("m_nodeIndex");
                    AddInputConnection(node, childNodeId);
                    continue;
                }
                else if (key == "m_hMoveVecParam")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    var paramIdValue = ParameterIDFromIndex(paramType, paramIndex);
                    node.AddProperty("m_moveVectorParam", paramIdValue);
                    continue;
                }
                else if (key == "m_hMoveHeadingParam")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    var paramIdValue = ParameterIDFromIndex(paramType, paramIndex);
                    node.AddProperty("m_moveHeadingParam", paramIdValue);
                    continue;
                }
                else if (key == "m_hTurnToFaceParam")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    var paramIdValue = ParameterIDFromIndex(paramType, paramIndex);
                    node.AddProperty("m_param", paramIdValue);
                    continue;
                }
                else if (key == "m_facingTarget")
                {
                    node.AddProperty(key, value);
                    continue;
                }
                else if (key == "m_flTurnToFaceOffset" || key == "m_flTurnToFaceLimit")
                {
                    node.AddProperty(key, value);
                    continue;
                }
                else if (key == "m_bAdditive" || key == "m_bApplyMovement" || key == "m_bOrientMovement" ||
                         key == "m_bApplyRotation" || key == "m_bLimitOnly")
                {
                    if (key == "m_bApplyRotation")
                    {
                        node.AddProperty("m_bTurnToFace", value);
                    }
                    else
                    {
                        node.AddProperty(key, value);
                    }
                    continue;
                }
                else if (key == "m_damping")
                {
                    node.AddProperty(key, value);
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
                    node.AddProperty("m_sName", value);
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
                            var choiceNode = new KVObject(null, 4);
                            AddInputConnection(choiceNode, nodeId);
                            choiceNode.AddProperty("m_name", (index + 1).ToString());
                            choiceNode.AddProperty("m_weight", weight);
                            choiceNode.AddProperty("m_blendTime", blendTime);
                            return choiceNode;
                        });

                        node.AddProperty("m_children", KVValue.MakeArray(newInputs));
                    }

                    continue;
                }

                if (key is "m_weights" or "m_blendTimes")
                {
                    continue;
                }
            }
            else if (className == "CAdd")
            {
                if (key == "m_bResetChild1")
                {
                    node.AddProperty("m_bResetBase", value);
                    continue;
                }
                else if (key == "m_bResetChild2")
                {
                    node.AddProperty("m_bResetAdditive", value);
                    continue;
                }
            }
            else if (className == "CSubtract")
            {
                if (key == "m_bResetChild1")
                {
                    node.AddProperty("m_bResetBase", value);
                    continue;
                }
                else if (key == "m_bResetChild2")
                {
                    node.AddProperty("m_bResetSubtract", value);
                    continue;
                }
            }
            else if (className == "CBoneMask")
            {
                if (key == "m_hBlendParameter")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    var paramIdValue = ParameterIDFromIndex(paramType, paramIndex);
                    node.AddProperty("m_blendParameter", paramIdValue);
                    continue;
                }

                if (key == "m_nWeightListIndex")
                {
                    var weightListIndex = compiledNode.GetIntegerProperty("m_nWeightListIndex");
                    var weightListName = GetWeightListName(weightListIndex);
                    node.AddProperty("m_weightListName", weightListName);
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
                            blendChild.AddProperty("m_name", "Unnamed");
                            blendChild.AddProperty("m_blendValue", blendValue);
                            blendChildren.Add(blendChild);
                        }

                        node.AddProperty("m_children", KVValue.MakeArray(blendChildren.ToArray()));
                    }

                    continue;
                }

                if (key is "m_targetValues" or "m_sortedOrder")
                {
                    continue;
                }

                if (key == "m_paramIndex")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    var paramIdValue = ParameterIDFromIndex(paramType, paramIndex);
                    node.AddProperty("m_param", paramIdValue);
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
                        var convertedItem = new KVObject(null, item.Properties.Count);

                        foreach (var (itemKey, itemValue) in item.Properties)
                        {
                            if (itemKey == "m_hSequence")
                            {
                                var sequenceIndex = item.GetIntegerProperty("m_hSequence");
                                var sequenceName = GetSequenceName(sequenceIndex);
                                convertedItem.AddProperty("m_sequenceName", sequenceName);
                            }
                            else if (itemKey == "m_tags")
                            {
                                try
                                {
                                    var compiledTagSpans = item.GetArray("m_tags");
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

                                    convertedItem.AddProperty("m_tagSpans", KVValue.MakeArray(tagSpans.ToArray()));
                                }
                                catch
                                {
                                    convertedItem.AddProperty("m_tagSpans", KVValue.MakeArray(Array.Empty<KVObject>()));
                                }
                                continue;
                            }
                            else if (itemKey == "m_vPos")
                            {
                                convertedItem.AddProperty("m_blendValue", itemValue);
                            }
                            else if (itemKey == "m_flDuration")
                            {
                                var useCustomDuration = item.GetIntegerProperty("m_bUseCustomDuration") > 0;
                                if (useCustomDuration)
                                {
                                    convertedItem.AddProperty("m_flCustomDuration", itemValue);
                                }
                            }
                            else if (itemKey == "m_bUseCustomDuration")
                            {
                                convertedItem.AddProperty(itemKey, itemValue);
                            }
                            else if (itemKey == "m_pChild")
                            {
                                continue;
                            }
                            else
                            {
                                convertedItem.AddProperty(itemKey, itemValue);
                            }
                        }
                        convertedItem.AddProperty("_class", "CSequenceBlend2DItem");
                        convertedItems.Add(convertedItem);
                    }

                    node.AddProperty("m_items", KVValue.MakeArray(convertedItems.ToArray()));
                    continue;
                }
                if (key == "m_tags")
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
                    catch
                    {
                        node.AddProperty("m_tagSpans", KVValue.MakeArray(Array.Empty<KVObject>()));
                    }
                    continue;
                }
                if (key == "m_eBlendMode")
                {
                    node.AddProperty(key, value);
                    continue;
                }
                if (key == "m_paramX" || key == "m_paramY")
                {
                    var paramRef = subCollection.Value;
                    var paramType = paramRef.GetStringProperty("m_type");
                    var paramIndex = paramRef.GetIntegerProperty("m_index");
                    var paramIdValue = ParameterIDFromIndex(paramType, paramIndex);
                    node.AddProperty(key, paramIdValue);
                    continue;
                }
                if (key == "m_blendSourceX" || key == "m_blendSourceY")
                {
                    node.AddProperty(key, value);
                    continue;
                }
            }

            if (key == "m_children")
            {
                if (inputNodeIds is not null)
                {
                    node.AddProperty(key, KVValue.MakeArray(inputNodeIds.Select(MakeInputConnection)));
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
                    catch
                    {
                        node.AddProperty("m_tagSpans", KVValue.MakeArray(Array.Empty<KVObject>()));
                    }
                    continue;
                }
                else if (className == "CSelector")
                {
                    try
                    {
                        var tagIndices = compiledNode.GetIntegerArray(key);
                        var tagIds = new List<KVObject>();

                        foreach (var tagIndex in tagIndices)
                        {
                            var tagId = -1L;
                            if (tagIndex >= 0 && tagIndex < Tags.Length)
                            {
                                tagId = Tags[tagIndex].GetSubCollection("m_tagID").GetIntegerProperty("m_id");
                            }
                            tagIds.Add(MakeNodeIdObjectValue(tagId).Value as KVObject ?? new KVObject("tag", 0));
                        }

                        node.AddProperty(key, KVValue.MakeArray(tagIds.ToArray()));
                    }
                    catch (InvalidCastException)
                    {
                        node.AddProperty(key, KVValue.MakeArray(Array.Empty<KVObject>()));
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
                catch
                {
                    node.AddProperty("m_paramSpans", KVValue.MakeArray(Array.Empty<KVObject>()));
                }

                continue;
            }

            if (key == "m_paramIndex")
            {
                var paramRef = subCollection.Value;
                var paramType = paramRef.GetStringProperty("m_type");
                var paramIndex = paramRef.GetIntegerProperty("m_index");
                node.AddProperty("m_paramID", ParameterIDFromIndex(paramType, paramIndex));
                continue;
            }

            node.AddProperty(newKey, value);
        }
        if (className == "CStateMachine")
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

        var uncompiledType = paramType.Replace("ANIMPARAM_", "");
        var currentCount = 0;

        for (var i = 0; i < Parameters.Length; i++)
        {
            var parameter = Parameters[i];
            var paramClass = parameter.GetStringProperty("_class");
            var paramTypeName = paramClass switch
            {
                "CFloatAnimParameter" => "FLOAT",
                "CEnumAnimParameter" => "ENUM",
                "CBoolAnimParameter" => "BOOL",
                "CIntAnimParameter" => "INTEGER",
                "CVectorAnimParameter" => "VECTOR",
                "CQuaternionAnimParameter" => "QUATERNION",
                "CSymbolAnimParameter" => "SYMBOL",
                _ => paramClass.Replace("C", "").Replace("AnimParameter", "").ToUpper(System.Globalization.CultureInfo.CurrentCulture),
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
}
