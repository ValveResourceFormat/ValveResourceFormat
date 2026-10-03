using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class ChainLookatNode : PassthroughNode
{
    public GlobalSymbol EndEffectorBoneID { get; }
    public Vector3 EndEffectorForwardAxis { get; } = new(1f, 0f, 0f);
    public Vector3 EndEffectorOffset { get; } = new(1f, 0f, 0f);
    public short LookatTargetNodeIdx { get; } = -1;
    public short EnabledNodeIdx { get; } = -1;
    public float BlendTimeSeconds { get; }
    public float[] ChainWeights { get; } = [];
    public byte ChainLength { get; } = 2;
    public bool IsTargetInWorldSpace { get; }
}
