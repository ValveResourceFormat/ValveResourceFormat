using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FloatEaseNode : FloatValueNode
{
    public float EaseTime { get; }
    public float StartValue { get; }
    public short InputValueNodeIdx { get; }
    public EasingOperation EasingOp { get; }
    public bool UseStartValue { get; }

    public FloatEaseNode(KVObject data) : base(data)
    {
        EaseTime = data.GetFloatProperty("m_flEaseTime");
        StartValue = data.GetFloatProperty("m_flStartValue");
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
        EasingOp = data.GetEnumValue<EasingOperation>("m_easingOp");
        UseStartValue = data.GetProperty<bool>("m_bUseStartValue");
    }
}
