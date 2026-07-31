using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

public sealed partial class NmGraphExtract
{
    private sealed class VariationGraph : IDisposable
    {
        public Resource Resource { get; }
        public KVObject Graph { get; }
        public string VariationId { get; }
        public KVObject?[] CompiledNodes { get; }
        public KVObject[] ReferencedGraphSlots { get; }
        public string[] Resources { get; }

        public VariationGraph(Resource resource)
        {
            Resource = resource;

            var resourceData = resource.DataBlock as BinaryKV3
                ?? throw new InvalidDataException("Variation graph DataBlock is not a BinaryKV3.");
            Graph = resourceData.Data;
            VariationId = Graph.GetRequiredStringProperty("m_variationID");
            CompiledNodes = Graph.GetArray("m_nodes")?.Select(value => value).ToArray()
                ?? throw new InvalidDataException("Variation NmGraph is missing m_nodes.");
            ReferencedGraphSlots = Graph.GetArray("m_referencedGraphSlots")?.ToArray() ?? [];
            Resources = Graph.GetArray<string>("m_resources")?.ToArray() ?? [];
        }

        public KVObject? GetCompiledNode(int nodeIndex)
            => nodeIndex >= 0 && nodeIndex < CompiledNodes.Length ? CompiledNodes[nodeIndex] : null;

        public string GetResourcePath(int dataSlotIndex)
            => dataSlotIndex >= 0 && dataSlotIndex < Resources.Length ? Resources[dataSlotIndex] : string.Empty;

        public string GetReferencedGraphPath(int referencedGraphIndex)
        {
            if (referencedGraphIndex < 0 || referencedGraphIndex >= ReferencedGraphSlots.Length)
            {
                return string.Empty;
            }

            var dataSlotIndex = (int)ReferencedGraphSlots[referencedGraphIndex].GetInt64Property("m_dataSlotIdx", -1);
            return GetResourcePath(dataSlotIndex);
        }

        public void Dispose()
            => Resource.Dispose();
    }
    private KVObject BuildVariationHierarchy()
    {
        var hierarchy = KVObject.Collection();
        var variations = KVObject.Array();

        var defaultVariation = KVObject.Collection();
        defaultVariation.Add("m_ID", "Default");
        defaultVariation.Add("m_parentID", string.Empty);
        defaultVariation.Add("m_skeleton", GetOptionalString(_graph, "m_skeleton"));
        variations.Add(defaultVariation);

        var variationId = _graph.GetRequiredStringProperty("m_variationID");
        if (variationId.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            if (_graph.TryGetValue("m_pUserData", out var userData) && !userData.IsNull)
            {
                defaultVariation.Add("m_pUserData", userData);
            }

            foreach (var variationGraph in _variationGraphs)
            {
                if (variationGraph.VariationId.Equals("Default", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var variation = KVObject.Collection();
                variation.Add("m_ID", variationGraph.VariationId);
                variation.Add("m_parentID", "Default");
                variation.Add("m_skeleton", GetOptionalString(variationGraph.Graph, "m_skeleton"));

                if (variationGraph.Graph.TryGetValue("m_pUserData", out var variationUserData) && !variationUserData.IsNull)
                {
                    variation.Add("m_pUserData", variationUserData);
                }

                variations.Add(variation);
            }
        }
        else
        {
            var variation = KVObject.Collection();
            variation.Add("m_ID", variationId);
            variation.Add("m_parentID", "Default");
            variation.Add("m_skeleton", GetOptionalString(_graph, "m_skeleton"));

            if (_graph.TryGetValue("m_pUserData", out var userData) && !userData.IsNull)
            {
                variation.Add("m_pUserData", userData);
            }

            variations.Add(variation);
        }

        hierarchy.Add("m_variations", variations);
        return hierarchy;
    }
    private void LoadVariationGraphs()
    {
        if (!_graph.GetRequiredStringProperty("m_variationID").Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_resource.GetBlockByType(BlockType.RED2) is not ResourceEditInfo2 editInfo || editInfo.ChildResourceList.Count == 0)
        {
            return;
        }

        foreach (var childResourcePath in editInfo.ChildResourceList.Distinct(StringComparer.Ordinal))
        {
            var childResource = _fileLoader.LoadFileCompiled(childResourcePath);
            if (childResource?.DataBlock is not BinaryKV3)
            {
                childResource?.Dispose();
                continue;
            }

            _variationGraphs.Add(new VariationGraph(childResource));
        }
    }

    private KVObject CreateVariationOverrides(int nodeIndex, KVObject defaultVariationData, Func<VariationGraph, KVObject?> variationDataFactory)
    {
        var overrides = KVObject.Array();

        foreach (var variationGraph in _variationGraphs)
        {
            var variationData = variationDataFactory(variationGraph);
            if (variationData is null || VariationDataEquals(defaultVariationData, variationData))
            {
                continue;
            }

            var variationOverride = KVObject.Collection();
            variationOverride.Add("m_variationID", variationGraph.VariationId);
            variationOverride.Add("m_pData", variationData);
            overrides.Add(variationOverride);
        }

        return overrides;
    }

    private static bool VariationDataEquals(KVObject left, KVObject right)
        => left.ToKV3String() == right.ToKV3String();

    private static KVObject CreateReferencedGraphVariationData(KVObject compiledNode, Func<int, string> getReferencedGraphPath)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocReferencedGraphNode::CData");
        variationData.Add("m_variation", getReferencedGraphPath((int)compiledNode.GetInt64Property("m_nReferencedGraphIdx")));
        return variationData;
    }

    private static KVObject CreateSelectorVariationData(string dataClassName, KVObject compiledNode)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", dataClassName);
        variationData.Add("m_optionWeights", CloneArray("m_optionWeights", compiledNode));
        return variationData;
    }

    private static KVObject CreateClipVariationData(KVObject compiledNode, Func<int, string> getResourcePath)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocClipNode::CData");
        variationData.Add("m_clip", getResourcePath((int)compiledNode.GetInt64Property("m_nDataSlotIdx")));
        variationData.Add("m_flSpeedMultiplier", compiledNode.GetRequiredFloatProperty("m_flSpeedMultiplier"));
        variationData.Add("m_nStartSyncEventOffset", compiledNode.GetInt64Property("m_nStartSyncEventOffset"));
        return variationData;
    }

    private static KVObject CreateAnimationPoseVariationData(KVObject compiledNode, Func<int, string> getResourcePath)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocAnimationPoseNode::CData");
        variationData.Add("m_clip", getResourcePath((int)compiledNode.GetInt64Property("m_nDataSlotIdx")));
        variationData.Add("m_variationTimeValue", -1.0f);
        return variationData;
    }

    private static KVObject CreateIDComparisonVariationData(KVObject compiledNode)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocVariationIDComparisonNode::CData");
        variationData.Add("m_values", CloneArray("m_comparisionIDs", compiledNode));
        return variationData;
    }

    private static KVObject CreateConstFloatVariationData(KVObject compiledNode)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CnmGraphDocVariationConstFloatNode::CData");
        variationData.Add("m_flValue", GetConstValue(compiledNode, "Float"));
        return variationData;
    }

    private static KVObject CreateBoneMaskVariationData(string overrideMaskId)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocBoneMaskNode::CData");
        variationData.Add("m_overrideMaskID", overrideMaskId);
        return variationData;
    }

    private static KVObject CreateFootIkVariationData(KVObject compiledNode)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CnmGraphDocFootIKNode::CData");
        variationData.Add("m_leftEffectorBoneName", GetOptionalString(compiledNode, "m_leftEffectorBoneID"));
        variationData.Add("m_rightEffectorBoneName", GetOptionalString(compiledNode, "m_rightEffectorBoneID"));
        variationData.Add("m_flBlendTimeSeconds", compiledNode.GetRequiredFloatProperty("m_flBlendTimeSeconds"));
        return variationData;
    }

    private static KVObject CreateTwoBoneIkVariationData(KVObject compiledNode)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CnmGraphDocTwoBoneIKNode::CData");
        variationData.Add("m_effectorBoneName", GetOptionalString(compiledNode, "m_effectorBoneID"));
        variationData.Add("m_flBlendTimeSeconds", compiledNode.GetRequiredFloatProperty("m_flBlendTimeSeconds"));
        return variationData;
    }

    private static KVObject CreateFollowBoneVariationData(KVObject compiledNode)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CnmGraphDocFollowBoneNode::CData");
        variationData.Add("m_boneName", GetOptionalString(compiledNode, "m_bone"));
        variationData.Add("m_followTargetBoneName", GetOptionalString(compiledNode, "m_followTargetBone"));
        return variationData;
    }

    private static KVObject CreateChainLookatVariationData(KVObject compiledNode)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CnmGraphDocChainLookatNode::CData");
        variationData.Add("m_chainEndBoneName", GetOptionalString(compiledNode, "m_chainEndBoneID"));
        variationData.Add("m_chainForwardDir", CloneVector3(compiledNode.GetArray("m_chainForwardDir")?.ToArray()));
        variationData.Add("m_nChainLength", compiledNode.GetInt64Property("m_nChainLength", 2));
        variationData.Add("m_flBlendTimeSeconds", compiledNode.GetFloatProperty("m_flBlendTimeSeconds"));
        return variationData;
    }

    private static KVObject CreateTargetWarpVariationData(KVObject compiledNode)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocTargetWarpNode::CData");
        variationData.Add("m_strAlignmentBoneName", GetOptionalString(compiledNode, "m_alignmentBoneID"));
        return variationData;
    }
}
