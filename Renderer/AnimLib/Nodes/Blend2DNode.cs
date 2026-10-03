using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class Blend2DNode : PoseNode
{
    public short[] SourceNodeIndices { get; } = [];
    public short InputParameterNodeIdx0 { get; } = -1;
    public short InputParameterNodeIdx1 { get; } = -1;
    public Vector2[] Values { get; } = [];
    public uint[] Indices { get; } = [];
    public uint[] HullIndices { get; } = [];
    public bool AllowLooping { get; } = true;
}
