using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class RootMotionData
{
    public Transform[] Transforms { get; } = [];
    public int NumFrames { get; private set; }
    public float AverageLinearVelocity { get; private set; }
    public float AverageAngularVelocityRadians { get; private set; }
    public Transform TotalDelta { get; private set; }
}

partial class RootMotionData
{
    internal enum SamplingMode : byte
    {
        Delta = 0,
        WorldSpace = 1,
    }
}
