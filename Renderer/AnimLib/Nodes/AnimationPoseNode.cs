using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class AnimationPoseNode : PoseNode
{
    public short PoseTimeValueNodeIdx { get; }
    public short DataSlotIdx { get; }
    public Range InputTimeRemapRange { get; }
    public float UserSpecifiedTime { get; }
    public bool UseFramesAsInput { get; }

    public AnimationPoseNode(KVObject data) : base(data)
    {
        PoseTimeValueNodeIdx = data.GetInt16Property("m_nPoseTimeValueNodeIdx");
        DataSlotIdx = data.GetInt16Property("m_nDataSlotIdx");
        InputTimeRemapRange = new(data.GetProperty<KVObject>("m_inputTimeRemapRange"));
        UserSpecifiedTime = data.GetFloatProperty("m_flUserSpecifiedTime");
        UseFramesAsInput = data.GetProperty<bool>("m_bUseFramesAsInput");
    }
}
