using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class VirtualParameterBoneMaskNode : BoneMaskValueNode
{
    public short ChildNodeIdx { get; }

    public VirtualParameterBoneMaskNode(KVObject data) : base(data)
    {
        ChildNodeIdx = data.GetInt16Property("m_nChildNodeIdx");
    }
}
