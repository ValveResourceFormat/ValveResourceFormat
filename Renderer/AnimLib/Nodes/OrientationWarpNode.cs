using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class OrientationWarpNode : PoseNode
{
    public short ClipReferenceNodeIdx { get; }
    public short TargetValueNodeIdx { get; }
    public bool IsOffsetNode { get; }
    public bool IsOffsetRelativeToCharacter { get; }
    public bool WarpTranslation { get; }
    public RootMotionData__SamplingMode SamplingMode { get; }

    public OrientationWarpNode(KVObject data) : base(data)
    {
        ClipReferenceNodeIdx = data.GetInt16Property("m_nClipReferenceNodeIdx");
        TargetValueNodeIdx = data.GetInt16Property("m_nTargetValueNodeIdx");
        IsOffsetNode = data.GetProperty<bool>("m_bIsOffsetNode");
        IsOffsetRelativeToCharacter = data.GetProperty<bool>("m_bIsOffsetRelativeToCharacter");
        WarpTranslation = data.GetProperty<bool>("m_bWarpTranslation");
        SamplingMode = data.GetEnumValue<RootMotionData__SamplingMode>("m_samplingMode");
    }
}
