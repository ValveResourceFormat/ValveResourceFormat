using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class LayerBlendNode__LayerDefinition
{
    public short InputNodeIdx { get; }
    public short WeightValueNodeIdx { get; }
    public short BoneMaskValueNodeIdx { get; }
    public short RootMotionWeightValueNodeIdx { get; }
    public bool IsSynchronized { get; }
    public bool IgnoreEvents { get; }
    public bool IsStateMachineLayer { get; }
    public PoseBlendMode BlendMode { get; }

    public LayerBlendNode__LayerDefinition(KVObject data)
    {
        InputNodeIdx = data.GetInt16Property("m_nInputNodeIdx");
        WeightValueNodeIdx = data.GetInt16Property("m_nWeightValueNodeIdx");
        BoneMaskValueNodeIdx = data.GetInt16Property("m_nBoneMaskValueNodeIdx");
        RootMotionWeightValueNodeIdx = data.GetInt16Property("m_nRootMotionWeightValueNodeIdx");
        IsSynchronized = data.GetProperty<bool>("m_bIsSynchronized");
        IgnoreEvents = data.GetProperty<bool>("m_bIgnoreEvents");
        IsStateMachineLayer = data.GetProperty<bool>("m_bIsStateMachineLayer");
        BlendMode = data.GetEnumValue<PoseBlendMode>("m_blendMode");
    }
}
