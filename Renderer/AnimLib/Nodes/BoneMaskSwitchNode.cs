using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class BoneMaskSwitchNode : BoneMaskValueNode
{
    public short SwitchValueNodeIdx { get; } = -1;
    public short TrueValueNodeIdx { get; } = -1;
    public short FalseValueNodeIdx { get; } = -1;
    public float BlendTimeSeconds { get; } = 0.1f;
    public bool SwitchDynamically { get; }
}
