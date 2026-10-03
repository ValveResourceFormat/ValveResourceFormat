using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A skeleton attached to a bone of another skeleton.</summary>
[KV3Transfer]
public partial class Skeleton__SecondarySkeleton
{
    /// <summary>The bone it attaches to.</summary>
    public GlobalSymbol AttachToBoneID { get; }
    /// <summary>The resource name of the attached skeleton.</summary>
    public string Skeleton { get; } // InfoForResourceTypeCNmSkeleton
}
