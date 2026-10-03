using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class TargetSelectorNode : TargetValueNode
{
    public short[] OptionNodeIndices { get; } = [];
    public float OrientationScoreWeight { get; } = 1f;
    public float PositionScoreWeight { get; } = 1f;
    [KVProperty("m_parameterNodeIdx")]
    public short ParameterNodeIdx { get; } = -1;
    public bool IgnoreInvalidOptions { get; }
    public bool IsWorldSpaceTarget { get; } = true;
    public GlobalSymbol AlignmentBoneID { get; }
}
