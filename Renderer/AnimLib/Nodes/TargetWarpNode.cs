using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class TargetWarpNode : PoseNode
{
    public short ClipReferenceNodeIdx { get; } = -1;
    public short TargetValueNodeIdx { get; } = -1;
    public RootMotionData.SamplingMode SamplingMode { get; }
    public TargetWarpNode.TargetUpdateRuleType TargetUpdateRule { get; }
    public bool AlignWithTargetAtLastWarpEvent { get; }
    public float SamplingPositionErrorThresholdSq { get; }
    public float MaxTangentLength { get; } = 1.25f;
    public float LerpFallbackDistanceThreshold { get; } = 0.1f;
    public float TargetUpdateDistanceThreshold { get; } = 0.1f;
    public float TargetUpdateAngleThresholdRadians { get; } = 0.087266f;
    public GlobalSymbol AlignmentBoneID { get; }
}
