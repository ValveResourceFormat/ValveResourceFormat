using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FootIKNode : PassthroughNode
{
    public GlobalSymbol LeftEffectorBoneID { get; }
    public GlobalSymbol RightEffectorBoneID { get; }
    public short LeftTargetNodeIdx { get; } = -1;
    public short RightTargetNodeIdx { get; } = -1;
    public short EnabledNodeIdx { get; } = -1;
    public float BlendTimeSeconds { get; }
    public IKBlendMode BlendMode { get; }
    public bool IsTargetInWorldSpace { get; }
}
