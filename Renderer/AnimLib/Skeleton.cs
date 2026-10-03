using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>An animation skeleton: bone hierarchy, reference pose and bone masks.</summary>
[KV3Transfer]
public partial class Skeleton
{
    /// <summary>The skeleton ID.</summary>
    public GlobalSymbol ID { get; }
    /// <summary>The ID of each bone.</summary>
    public GlobalSymbol[] BoneIDs { get; } = [];
    /// <summary>The parent index of each bone, -1 for roots.</summary>
    public int[] ParentIndices { get; } = [];
    /// <summary>The reference pose with each bone relative to its parent.</summary>
    public Transform[] ParentSpaceReferencePose { get; } = [];
    /// <summary>The reference pose in model space.</summary>
    public Transform[] ModelSpaceReferencePose { get; } = [];
    /// <summary>The number of leading bones sampled at low LOD.</summary>
    [KVProperty("m_numBonesToSampleAtLowLOD")]
    public int NumBonesToSampleAtLowLOD { get; }
    /// <summary>Whether this is a prop skeleton.</summary>
    public bool IsPropSkeleton { get; }
    /// <summary>The bone mask definitions.</summary>
    public BoneMaskSetDefinition[] MaskDefinitions { get; } = [];
    /// <summary>Skeletons attached to bones of this one.</summary>
    public Skeleton__SecondarySkeleton[] SecondarySkeletons { get; } = [];
    /// <summary>The float channel sets.</summary>
    public FloatChannelSet[] FloatChannelSets { get; } = [];
    /// <summary>The contact point configurations.</summary>
    public Skeleton__ContactConfig[] ContactConfigs { get; } = [];
    /// <summary>The bones relevant to gameplay.</summary>
    public int[] GameplayRelevantBoneIndices { get; } = [];
    /// <summary>A hash of the special dependencies.</summary>
    public long SpecialDependencyHash { get; }

    partial void OnLoaded(KVObject data)
    {
        for (var i = 0; i < MaskDefinitions.Length; i++)
        {
            GetResolvedMaskWeights(i);
        }
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

    /// <summary>Gets the index of a bone mask definition, or -1 if not found.</summary>
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

    /// <summary>Gets the index of a bone, or -1 if not found.</summary>
    public int GetBoneIndex(GlobalSymbol boneID) => Array.IndexOf(BoneIDs, boneID);

    /// <summary>Gets the parent index of a bone, or -1 for roots and invalid bones.</summary>
    public int GetParentBoneIndex(int boneIdx) => boneIdx >= 0 ? ParentIndices[boneIdx] : -1;
}
