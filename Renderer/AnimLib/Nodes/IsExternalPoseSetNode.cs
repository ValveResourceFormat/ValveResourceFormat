using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class IsExternalPoseSetNode : BoolValueNode
{
    public short ExternalPoseNodeIdx { get; } = -1;
}
