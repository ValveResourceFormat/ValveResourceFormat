using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class OrientationWarpNode : PoseNode
{
    public short ClipReferenceNodeIdx { get; } = -1;
    public short TargetValueNodeIdx { get; } = -1;
    public bool IsOffsetNode { get; }
    public bool IsOffsetRelativeToCharacter { get; } = true;
    public bool WarpTranslation { get; }
    public OrientationWarpNode__AlignmentMode AlignmentMode { get; }
    public RootMotionData__SamplingMode SamplingMode { get; } = RootMotionData__SamplingMode.WorldSpace;
}
