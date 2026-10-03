using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class IsExternalPoseSetNode : BoolValueNode
{
    public short ExternalPoseNodeIdx { get; }

    public IsExternalPoseSetNode(KVObject data) : base(data)
    {
        ExternalPoseNodeIdx = data.GetInt16Property("m_nExternalPoseNodeIdx");
    }
}
