using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class TargetWarpNode : PoseNode
{
    public short ClipReferenceNodeIdx { get; }
    public short TargetValueNodeIdx { get; }
    public RootMotionData__SamplingMode SamplingMode { get; }
    public bool AllowTargetUpdate { get; }
    public bool AlignWithTargetAtLastWarpEvent { get; }
    public float SamplingPositionErrorThresholdSq { get; }
    public float MaxTangentLength { get; }
    public float LerpFallbackDistanceThreshold { get; }
    public float TargetUpdateDistanceThreshold { get; }
    public float TargetUpdateAngleThresholdRadians { get; }

    public TargetWarpNode(KVObject data) : base(data)
    {
        ClipReferenceNodeIdx = data.GetInt16Property("m_nClipReferenceNodeIdx");
        TargetValueNodeIdx = data.GetInt16Property("m_nTargetValueNodeIdx");
        SamplingMode = data.GetEnumValue<RootMotionData__SamplingMode>("m_samplingMode");
        AllowTargetUpdate = data.GetProperty<bool>("m_bAllowTargetUpdate");
        AlignWithTargetAtLastWarpEvent = data.GetProperty<bool>("m_bAlignWithTargetAtLastWarpEvent");
        SamplingPositionErrorThresholdSq = data.GetFloatProperty("m_flSamplingPositionErrorThresholdSq");
        MaxTangentLength = data.GetFloatProperty("m_flMaxTangentLength");
        LerpFallbackDistanceThreshold = data.GetFloatProperty("m_flLerpFallbackDistanceThreshold");
        TargetUpdateDistanceThreshold = data.GetFloatProperty("m_flTargetUpdateDistanceThreshold");
        TargetUpdateAngleThresholdRadians = data.GetFloatProperty("m_flTargetUpdateAngleThresholdRadians");
    }
}
