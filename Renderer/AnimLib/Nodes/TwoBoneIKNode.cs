using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class TwoBoneIKNode : PassthroughNode
{
    public GlobalSymbol EffectorBoneID { get; }
    public short EffectorTargetNodeIdx { get; } = -1;
    public short EnabledNodeIdx { get; } = -1;
    public float BlendTimeSeconds { get; }
    public IKBlendMode BlendMode { get; }
    public bool IsTargetInWorldSpace { get; }
    public float ChainRotationWeight { get; }
}
