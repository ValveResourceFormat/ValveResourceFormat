using ValveResourceFormat.Serialization.KeyValues;
using Transform = ValveResourceFormat.ResourceTypes.ModelAnimation.FrameBone;
namespace ValveResourceFormat.Animation;

[KV3Transfer]
public partial class RootMotionData
{
    public Transform[] Transforms { get; } = [];
    public int NumFrames { get; private set; }
    public float AverageLinearVelocity { get; private set; }
    public float AverageAngularVelocityRadians { get; private set; }
    public Transform TotalDelta { get; private set; }
}

public partial class RootMotionData
{
    public enum SamplingMode : byte
    {
        Delta = 0,
        WorldSpace = 1,
    }
}
