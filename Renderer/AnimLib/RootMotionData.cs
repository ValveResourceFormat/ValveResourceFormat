using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class RootMotionData
{
    public Transform[] Transforms { get; }
    public int NumFrames { get; }
    public float AverageLinearVelocity { get; }
    public float AverageAngularVelocityRadians { get; }
    public Transform TotalDelta { get; }

    public RootMotionData(KVObject data)
    {
        Transforms = data.GetArray<Transform>("m_transforms");
        NumFrames = data.GetInt32Property("m_nNumFrames");
        AverageLinearVelocity = data.GetFloatProperty("m_flAverageLinearVelocity");
        AverageAngularVelocityRadians = data.GetFloatProperty("m_flAverageAngularVelocityRadians");
        TotalDelta = new(data.GetProperty<KVObject>("m_totalDelta"));
    }
}
