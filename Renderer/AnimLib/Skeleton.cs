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
    public BoneMaskSetDefinition[] MaskDefinitions { get; }
    public Skeleton__SecondarySkeleton[] SecondarySkeletons { get; }
    public bool IsPropSkeleton { get; }

    public Skeleton(KVObject data)
    {
        ID = data.GetProperty<string>("m_ID");
        BoneIDs = data.GetSymbolArray("m_boneIDs");
        ParentIndices = data.GetArray<int>("m_parentIndices");
        ParentSpaceReferencePose = data.GetTransformArray("m_parentSpaceReferencePose");
        ModelSpaceReferencePose = data.GetTransformArray("m_modelSpaceReferencePose");
        NumBonesToSampleAtLowLOD = data.GetInt32Property("m_numBonesToSampleAtLowLOD");
        MaskDefinitions = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_maskDefinitions"), kv => new BoneMaskSetDefinition(kv))];
        SecondarySkeletons = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_secondarySkeletons"), kv => new Skeleton__SecondarySkeleton(kv))];
        IsPropSkeleton = data.GetProperty<bool>("m_bIsPropSkeleton");
    }

    private float[][] resolvedMaskWeights;

    /// <summary>
    /// Gets per-bone weights for a mask definition, mapping its bone ID list onto this skeleton.
    /// Bones the mask does not list get weight 0.
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

            for (var i = 0; i < list.BoneIDs.Length && i < list.Weights.Length; i++)
            {
                for (var b = 0; b < BoneIDs.Length; b++)
                {
                    if (BoneIDs[b] == list.BoneIDs[i])
                    {
                        weights[b] = list.Weights[i];
                        break;
                    }
                }
            }

            resolvedMaskWeights[maskIndex] = weights;
        }

        return resolvedMaskWeights[maskIndex];
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
}
