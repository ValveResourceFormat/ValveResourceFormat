using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class ReferencedGraphNode : PoseNode
{
    public short ReferencedGraphIdx { get; }
    public short FallbackNodeIdx { get; }

    public ReferencedGraphNode(KVObject data) : base(data)
    {
        ReferencedGraphIdx = data.GetInt16Property("m_nReferencedGraphIdx");
        FallbackNodeIdx = data.GetInt16Property("m_nFallbackNodeIdx");
    }
}
