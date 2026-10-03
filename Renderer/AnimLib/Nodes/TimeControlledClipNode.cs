using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class TimeControlledClipNode : PoseNode
{
    public short PlayInReverseValueNodeIdx { get; } = -1;
    public bool SampleRootMotion { get; } = true;
    public short DataSlotIdx { get; } = -1;
    public short TimeValueNodeIdx { get; } = -1;
    public GlobalSymbol[] GraphEvents { get; } = [];
}
