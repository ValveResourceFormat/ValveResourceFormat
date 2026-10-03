using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class ClipNode : ClipReferenceNode
{
    public short PlayInReverseValueNodeIdx { get; } = -1;
    public bool SampleRootMotion { get; } = true;
    public bool AllowLooping { get; }
    public short DataSlotIdx { get; } = -1;
    public short ResetTimeValueNodeIdx { get; } = -1;
    public GlobalSymbol[] GraphEvents { get; } = [];
    public float SpeedMultiplier { get; } = 1f;
    public int StartSyncEventOffset { get; }
}
