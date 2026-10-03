using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class ReferencedGraphNode : PoseNode
{
    public short ReferencedGraphIdx { get; } = -1;
    public short FallbackNodeIdx { get; } = -1;
}
