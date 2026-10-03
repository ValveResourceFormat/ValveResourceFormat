using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class BoneMaskSelectorNode : BoneMaskValueNode
{
    [KVProperty("m_defaultMaskNodeIdx")]
    public short DefaultMaskNodeIdx { get; } = -1;
    [KVProperty("m_parameterValueNodeIdx")]
    public short ParameterValueNodeIdx { get; } = -1;
    public bool SwitchDynamically { get; }
    public short[] MaskNodeIndices { get; } = [];
    public GlobalSymbol[] ParameterValues { get; } = [];
    public float BlendTimeSeconds { get; } = 0.1f;
}
