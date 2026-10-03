using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class ScaleNode : PassthroughNode
{
    public short MaskNodeIdx { get; }
    public short EnableNodeIdx { get; }

    public ScaleNode(KVObject data) : base(data)
    {
        MaskNodeIdx = data.GetInt16Property("m_nMaskNodeIdx");
        EnableNodeIdx = data.GetInt16Property("m_nEnableNodeIdx");
    }
}
