using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>Per-bone weights of a bone mask for one skeleton.</summary>
[KV3Transfer]
public partial class BoneWeightList
{
    /// <summary>The skeleton the weights are for.</summary>
    public string SkeletonName { get; }
    /// <summary>The bones that have a weight.</summary>
    public GlobalSymbol[] BoneIDs { get; } = [];
    /// <summary>The weight of each listed bone.</summary>
    public float[] Weights { get; } = [];
}
