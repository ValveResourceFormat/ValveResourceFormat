using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class ExternalPoseNode : PoseNode
{
    public bool ShouldSampleRootMotion { get; }

    public ExternalPoseNode(KVObject data) : base(data)
    {
        ShouldSampleRootMotion = data.GetProperty<bool>("m_bShouldSampleRootMotion");
    }
}
