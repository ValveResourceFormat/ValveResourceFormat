using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class ParameterizedBlendNode : PoseNode
{
    public short[] SourceNodeIndices { get; } = [];
    public short InputParameterValueNodeIdx { get; } = -1;
    public bool AllowLooping { get; } = true;
}
