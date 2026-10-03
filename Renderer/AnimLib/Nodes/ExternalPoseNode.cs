using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class ExternalPoseNode : PoseNode
{
    public bool ShouldSampleRootMotion { get; }
}
