using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class Skeleton
{
    public GlobalSymbol ID { get; }
    public GlobalSymbol[] BoneIDs { get; }
    public int[] ParentIndices { get; }
    public Transform[] ParentSpaceReferencePose { get; }
    public Transform[] ModelSpaceReferencePose { get; }
    public int NumBonesToSampleAtLowLOD { get; }
    public bool IsPropSkeleton { get; }
    public BoneMaskSetDefinition[] MaskDefinitions { get; }
    public Skeleton__SecondarySkeleton[] SecondarySkeletons { get; }
    public FloatChannelSet[] FloatChannelSets { get; }
    public Skeleton__ContactConfig[] ContactConfigs { get; }
    public int[] GameplayRelevantBoneIndices { get; }
    public long SpecialDependencyHash { get; }

    public Skeleton(KVObject data)
    {
        ID = data.GetProperty<string>("m_ID");
        BoneIDs = data.GetSymbolArray("m_boneIDs");
        ParentIndices = data.GetArray<int>("m_parentIndices") ?? [];
        ParentSpaceReferencePose = data.GetTransformArray("m_parentSpaceReferencePose");
        ModelSpaceReferencePose = data.GetTransformArray("m_modelSpaceReferencePose");
        NumBonesToSampleAtLowLOD = data.GetInt32Property("m_numBonesToSampleAtLowLOD");
        IsPropSkeleton = data.GetProperty<bool>("m_bIsPropSkeleton");
        MaskDefinitions = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_maskDefinitions") ?? [], kv => new BoneMaskSetDefinition(kv))];
        SecondarySkeletons = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_secondarySkeletons") ?? [], kv => new Skeleton__SecondarySkeleton(kv))];
        FloatChannelSets = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_floatChannelSets") ?? [], kv => new FloatChannelSet(kv))];
        ContactConfigs = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_contactConfigs") ?? [], kv => new Skeleton__ContactConfig(kv))];
        GameplayRelevantBoneIndices = data.GetArray<int>("m_gameplayRelevantBoneIndices") ?? [];
        SpecialDependencyHash = data.GetIntegerProperty("m_nSpecialDependencyHash");
    }

    private float[][] resolvedMaskWeights;

    /// <summary>
    /// Gets per-bone weights for a mask definition, mapping its bone ID list onto this skeleton.
    /// Bones the mask does not list take their weights from the listed bones around them.
    /// </summary>
    public float[] GetResolvedMaskWeights(int maskIndex)
    {
        resolvedMaskWeights ??= new float[MaskDefinitions.Length][];

        if (maskIndex < 0 || maskIndex >= MaskDefinitions.Length)
        {
            return [];
        }

        if (resolvedMaskWeights[maskIndex] == null)
        {
            var weights = new float[BoneIDs.Length];
            var list = MaskDefinitions[maskIndex].PrimaryWeightList;

            if (list.BoneIDs.Length == 0)
            {
                Array.Fill(weights, 1f);
            }
            else
            {
                Array.Fill(weights, UnsetWeight);

                for (var i = 0; i < list.BoneIDs.Length && i < list.Weights.Length; i++)
                {
                    var boneIdx = GetBoneIndex(list.BoneIDs[i]);
                    if (boneIdx != -1)
                    {
                        weights[boneIdx] = list.Weights[i];
                    }
                }

                FillUnsetWeights(weights);
            }

            resolvedMaskWeights[maskIndex] = weights;
        }

        return resolvedMaskWeights[maskIndex];
    }

    const float UnsetWeight = -1f;

    // A mask lists only some bones. Unlisted bones below a listed one take its weight, and a listed bone
    // with unlisted parents feathers its weight up the chain towards the nearest listed ancestor, or to
    // zero when there is none.
    void FillUnsetWeights(float[] weights)
    {
        var originalWeights = (float[])weights.Clone();
        var boneChainIndices = new List<int>();

        for (var boneIdx = BoneIDs.Length - 1; boneIdx > 0; boneIdx--)
        {
            if (weights[boneIdx] == UnsetWeight)
            {
                boneChainIndices.Clear();
                boneChainIndices.Add(boneIdx);

                var chainWeight = 0f;
                var parentBoneIdx = GetParentBoneIndex(boneIdx);
                while (parentBoneIdx != -1)
                {
                    if (originalWeights[parentBoneIdx] != UnsetWeight)
                    {
                        chainWeight = originalWeights[parentBoneIdx];
                        break;
                    }

                    boneChainIndices.Add(parentBoneIdx);
                    parentBoneIdx = GetParentBoneIndex(parentBoneIdx);
                }

                // The root keeps its own weight
                if (parentBoneIdx == -1 && boneChainIndices.Count > 0)
                {
                    boneChainIndices.RemoveAt(boneChainIndices.Count - 1);
                }

                foreach (var i in boneChainIndices)
                {
                    weights[i] = chainWeight;
                }
            }
            else if (GetParentBoneIndex(boneIdx) is var parent and not -1 && weights[parent] == UnsetWeight)
            {
                var endWeight = weights[boneIdx];
                var startWeight = UnsetWeight;

                boneChainIndices.Clear();
                boneChainIndices.Add(boneIdx);

                var parentBoneIdx = parent;
                while (parentBoneIdx != -1)
                {
                    boneChainIndices.Add(parentBoneIdx);

                    if (originalWeights[parentBoneIdx] != UnsetWeight)
                    {
                        startWeight = originalWeights[parentBoneIdx];
                        break;
                    }

                    parentBoneIdx = GetParentBoneIndex(parentBoneIdx);
                }

                var numBonesInChain = boneChainIndices.Count;
                for (var i = numBonesInChain - 2; i > 0; i--)
                {
                    var percentageThrough = (float)i / (numBonesInChain - 1);
                    weights[boneChainIndices[i]] = startWeight != UnsetWeight ? float.Lerp(endWeight, startWeight, percentageThrough) : 0f;
                }
            }
        }

        if (weights[0] == UnsetWeight)
        {
            weights[0] = 0f;
        }
    }

    public int GetBoneMaskIndex(GlobalSymbol boneMaskID)
    {
        for (var i = 0; i < MaskDefinitions.Length; i++)
        {
            if (MaskDefinitions[i].ID == boneMaskID)
            {
                return i;
            }
        }

        return -1; // InvalidIndex
    }

    public int GetBoneIndex(GlobalSymbol boneID) => Array.IndexOf(BoneIDs, boneID);

    public int GetParentBoneIndex(int boneIdx) => boneIdx >= 0 ? ParentIndices[boneIdx] : -1;
}
