using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class Skeleton__SecondarySkeleton
{
    public GlobalSymbol AttachToBoneID { get; }
    public string Skeleton { get; } // InfoForResourceTypeCNmSkeleton

    public Skeleton__SecondarySkeleton(KVObject data)
    {
        AttachToBoneID = data.GetProperty<string>("m_attachToBoneID");
        Skeleton = data.GetProperty<string>("m_skeleton");
    }
}
