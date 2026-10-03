using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class BoneMaskSelectorNode : BoneMaskValueNode
{
    public short DefaultMaskNodeIdx { get; }
    public short ParameterValueNodeIdx { get; }
    public bool SwitchDynamically { get; }
    public short[] MaskNodeIndices { get; }
    public GlobalSymbol[] ParameterValues { get; }
    public float BlendTimeSeconds { get; }

    public BoneMaskSelectorNode(KVObject data) : base(data)
    {
        DefaultMaskNodeIdx = data.GetInt16Property("m_defaultMaskNodeIdx");
        ParameterValueNodeIdx = data.GetInt16Property("m_parameterValueNodeIdx");
        SwitchDynamically = data.GetProperty<bool>("m_bSwitchDynamically");
        MaskNodeIndices = data.GetArray<short>("m_maskNodeIndices");
        ParameterValues = data.GetSymbolArray("m_parameterValues");
        BlendTimeSeconds = data.GetFloatProperty("m_flBlendTimeSeconds");
    }
}
