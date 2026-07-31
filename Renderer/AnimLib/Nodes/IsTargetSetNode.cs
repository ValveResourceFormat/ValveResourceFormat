using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class IsTargetSetNode : BoolValueNode
{
    public short InputValueNodeIdx { get; }

    public IsTargetSetNode(KVObject data) : base(data)
    {
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
    }
}
