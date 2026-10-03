using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>Per-bone weights of a bone mask for one skeleton.</summary>
public class BoneWeightList
{
    /// <summary>The skeleton the weights are for.</summary>
    public string SkeletonName { get; }
    /// <summary>The bones that have a weight.</summary>
    public GlobalSymbol[] BoneIDs { get; }
    /// <summary>The weight of each listed bone.</summary>
    public float[] Weights { get; }

    /// <summary>Reads the list from resource data.</summary>
    public BoneWeightList(KVObject data)
    {
        SkeletonName = data.GetProperty<string>("m_skeletonName");
        BoneIDs = data.GetSymbolArray("m_boneIDs");
        Weights = data.GetFloatArray("m_weights");
    }
}
