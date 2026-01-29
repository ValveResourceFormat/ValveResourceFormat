using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FloatSelectorNode : FloatValueNode
{
    public short[] ConditionNodeIndices { get; }
    public float[] Values { get; }
    public float DefaultValue { get; }
    public float EaseTime { get; }
    public EasingOperation EasingOp { get; }

    public FloatSelectorNode(KVObject data) : base(data)
    {
        ConditionNodeIndices = data.GetArray<short>("m_conditionNodeIndices");
        Values = data.GetArray<float>("m_values");
        DefaultValue = data.GetFloatProperty("m_flDefaultValue");
        EaseTime = data.GetFloatProperty("m_flEaseTime");
        EasingOp = data.GetEnumValue<EasingOperation>("m_easingOp");
    }
}
