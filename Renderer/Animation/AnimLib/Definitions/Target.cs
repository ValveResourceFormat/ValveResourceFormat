using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial struct Target
{
    /// <summary>
    /// Either the actual transform or the offsets that need to be applied
    /// </summary>
    public Transform Transform { get; set; }
    public GlobalSymbol BoneID { get; }
    public bool IsBoneTarget { get; }
    public bool IsUsingBoneSpaceOffsets { get; set; } = true;
    public bool HasOffsets { get; set; }
    public bool IsSet { get; set; }
}
