using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Animation;
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
        private readonly Dictionary<int, Animation.GraphNode?> definitions = [];
        public GraphDefinition Definition { get; }

        public VariationGraph(Resource resource)
        {
            Resource = resource;

            var resourceData = resource.DataBlock as BinaryKV3
                ?? throw new InvalidDataException("Variation graph DataBlock is not a BinaryKV3.");
            Graph = resourceData.Data;
            VariationId = Graph.GetRequiredStringProperty("m_variationID");
            var compiledNodes = Graph.GetArray("m_nodes")
                ?? throw new InvalidDataException("Variation NmGraph is missing m_nodes.");
            CompiledNodes = [.. compiledNodes];
            Definition = new GraphDefinition(Graph);
        }

        public KVObject? GetCompiledNode(int nodeIndex)
            => nodeIndex >= 0 && nodeIndex < CompiledNodes.Length ? CompiledNodes[nodeIndex] : null;

        public Animation.GraphNode? GetDefinition(int nodeIndex)
        {
            if (GetCompiledNode(nodeIndex) is not { } compiledNode)
            {
                return null;
            }

            if (!definitions.TryGetValue(nodeIndex, out var definition))
            {
                definition = CreateDefinition(compiledNode);
                definitions[nodeIndex] = definition;
            }

            return definition;
        }

        public string GetResourcePath(int dataSlotIndex)
            => NmGraphExtract.GetResourcePath(Definition, dataSlotIndex);

        public string GetReferencedGraphPath(int referencedGraphIndex)
            => NmGraphExtract.GetReferencedGraphPath(Definition, referencedGraphIndex);

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
        defaultVariation.Add("m_skeleton", _definition.Skeleton ?? string.Empty);
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
                variation.Add("m_skeleton", variationGraph.Definition.Skeleton ?? string.Empty);

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
            variation.Add("m_skeleton", _definition.Skeleton ?? string.Empty);

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

    private static KVObject CreateReferencedGraphVariationData(ReferencedGraphNode definition, Func<int, string> getReferencedGraphPath)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocReferencedGraphNode::CData");
        variationData.Add("m_variation", getReferencedGraphPath(definition.ReferencedGraphIdx));
        return variationData;
    }

    private static KVObject CreateSelectorVariationData(string dataClassName, byte[] optionWeights)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", dataClassName);
        variationData.Add("m_optionWeights", CloneValueArray(optionWeights));
        return variationData;
    }

    private static KVObject CreateClipVariationData(ClipNode definition, Func<int, string> getResourcePath)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocClipNode::CData");
        variationData.Add("m_clip", getResourcePath(definition.DataSlotIdx));
        variationData.Add("m_flSpeedMultiplier", definition.SpeedMultiplier);
        variationData.Add("m_nStartSyncEventOffset", definition.StartSyncEventOffset);
        return variationData;
    }

    private static KVObject CreateTimeControlledClipVariationData(TimeControlledClipNode definition, Func<int, string> getResourcePath)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocTimeControlledClipNode::CData");
        variationData.Add("m_clip", getResourcePath(definition.DataSlotIdx));
        return variationData;
    }

    private static KVObject CreateAnimationPoseVariationData(AnimationPoseNode definition, Func<int, string> getResourcePath)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocAnimationPoseNode::CData");
        variationData.Add("m_clip", getResourcePath(definition.DataSlotIdx));
        variationData.Add("m_variationTimeValue", -1.0f);
        return variationData;
    }

    private static KVObject CreateIDComparisonVariationData(IDComparisonNode definition)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocVariationIDComparisonNode::CData");
        variationData.Add("m_values", CloneStringArray(GetNames(definition.ComparisionIDs)));
        return variationData;
    }

    private static KVObject CreateConstFloatVariationData(ConstFloatNode definition)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CnmGraphDocVariationConstFloatNode::CData");
        variationData.Add("m_flValue", definition.Value);
        return variationData;
    }

    private static KVObject CreateBoneMaskVariationData(string overrideMaskId)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocBoneMaskNode::CData");
        variationData.Add("m_overrideMaskID", overrideMaskId);
        return variationData;
    }

    private static KVObject CreateFootIkVariationData(FootIKNode definition)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CnmGraphDocFootIKNode::CData");
        variationData.Add("m_leftEffectorBoneName", definition.LeftEffectorBoneID.Name);
        variationData.Add("m_rightEffectorBoneName", definition.RightEffectorBoneID.Name);
        variationData.Add("m_flBlendTimeSeconds", definition.BlendTimeSeconds);
        return variationData;
    }

    private static KVObject CreateTwoBoneIkVariationData(TwoBoneIKNode definition)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CnmGraphDocTwoBoneIKNode::CData");
        variationData.Add("m_effectorBoneName", definition.EffectorBoneID.Name);
        variationData.Add("m_flBlendTimeSeconds", definition.BlendTimeSeconds);
        return variationData;
    }

    private static KVObject CreateFollowBoneVariationData(FollowBoneNode definition)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CnmGraphDocFollowBoneNode::CData");
        variationData.Add("m_boneName", definition.Bone.Name);
        variationData.Add("m_followTargetBoneName", definition.FollowTargetBone.Name);
        return variationData;
    }

    private static KVObject CreateBodyGroupVariationData(BodyGroupNode definition)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocBodyGroupNode::CData");
        variationData.Add("m_groupName", definition.Event.GroupName);
        variationData.Add("m_choiceName", definition.Event.ChoiceName);
        return variationData;
    }

    private static KVObject CreateChainLookatVariationData(ChainLookatNode definition)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CnmGraphDocChainLookatNode::CData");
        variationData.Add("m_chainEndBoneName", definition.EndEffectorBoneID.Name);
        variationData.Add("m_chainForwardDir", CloneVector3(definition.EndEffectorForwardAxis));
        variationData.Add("m_nChainLength", definition.ChainLength);
        variationData.Add("m_flBlendTimeSeconds", definition.BlendTimeSeconds);
        return variationData;
    }

    private static KVObject CreateTargetWarpVariationData(TargetWarpNode definition)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocTargetWarpNode::CData");
        variationData.Add("m_strAlignmentBoneName", definition.AlignmentBoneID.Name);
        return variationData;
    }

    private static KVObject CreateTargetSelectorVariationData(TargetSelectorNode definition)
    {
        var variationData = KVObject.Collection();
        variationData.Add("_class", "CNmGraphDocTargetSelectorNode::CData");
        variationData.Add("m_strAlignmentBoneName", definition.AlignmentBoneID.Name);
        return variationData;
    }
}
