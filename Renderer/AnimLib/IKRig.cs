using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class IKRig
{
    public string Skeleton { get; } // InfoForResourceTypeCNmSkeleton
    public IKBody[] VecBodies { get; }
    public IKJoint[] VecJoints { get; }

    public IKRig(KVObject data)
    {
        Skeleton = data.GetProperty<string>("m_skeleton");
        VecBodies = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_vecBodies"), kv => new IKBody(kv))];
        VecJoints = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_vecJoints"), kv => new IKJoint(kv))];
    }
}
