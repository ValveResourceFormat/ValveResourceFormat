using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class BoneWeightList
{
    public string SkeletonName { get; }
    public GlobalSymbol[] BoneIDs { get; }
    public float[] Weights { get; }

    public BoneWeightList(KVObject data)
    {
        SkeletonName = data.GetProperty<string>("m_skeletonName");
        BoneIDs = data.GetSymbolArray("m_boneIDs");
        Weights = data.GetArray<float>("m_weights");
    }
}
