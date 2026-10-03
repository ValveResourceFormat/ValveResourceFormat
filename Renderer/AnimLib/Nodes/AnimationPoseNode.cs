using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class AnimationPoseNode : PoseNode
{
    public short PoseTimeValueNodeIdx { get; } = -1;
    public short DataSlotIdx { get; } = -1;
    public Range InputTimeRemapRange { get; }
    public float UserSpecifiedTime { get; }
    public bool UseFramesAsInput { get; }
}
