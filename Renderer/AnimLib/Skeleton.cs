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
        ParentSpaceReferencePose = data.GetArray<Transform>("m_parentSpaceReferencePose");
        ModelSpaceReferencePose = data.GetArray<Transform>("m_modelSpaceReferencePose");
        NumBonesToSampleAtLowLOD = data.GetInt32Property("m_numBonesToSampleAtLowLOD");
        MaskDefinitions = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_maskDefinitions"), kv => new BoneMaskSetDefinition(kv))];
        SecondarySkeletons = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_secondarySkeletons"), kv => new Skeleton__SecondarySkeleton(kv))];
        IsPropSkeleton = data.GetProperty<bool>("m_bIsPropSkeleton");
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
