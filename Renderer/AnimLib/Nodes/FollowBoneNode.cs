using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FollowBoneNode : PassthroughNode
{
    public GlobalSymbol Bone { get; }
    public GlobalSymbol FollowTargetBone { get; }
    public short EnabledNodeIdx { get; } = -1;
    public FollowBoneMode Mode { get; }
}
