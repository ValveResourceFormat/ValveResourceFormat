using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A skeleton attached to a bone of another skeleton.</summary>
public class Skeleton__SecondarySkeleton
{
    /// <summary>The bone it attaches to.</summary>
    public GlobalSymbol AttachToBoneID { get; }
    /// <summary>The resource name of the attached skeleton.</summary>
    public string Skeleton { get; } // InfoForResourceTypeCNmSkeleton

    /// <summary>Reads the attachment from resource data.</summary>
    public Skeleton__SecondarySkeleton(KVObject data)
    {
        AttachToBoneID = data.GetProperty<string>("m_attachToBoneID");
        Skeleton = data.GetProperty<string>("m_skeleton");
    }
}
