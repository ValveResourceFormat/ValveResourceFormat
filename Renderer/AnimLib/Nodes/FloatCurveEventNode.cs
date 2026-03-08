using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FloatCurveEventNode : FloatValueNode
{
    public GlobalSymbol EventID { get; }
    public short DefaultNodeIdx { get; }
    public float DefaultValue { get; }
    public BitFlags EventConditionRules { get; }

    public FloatCurveEventNode(KVObject data) : base(data)
    {
        EventID = data.GetProperty<string>("m_eventID");
        DefaultNodeIdx = data.GetInt16Property("m_nDefaultNodeIdx");
        DefaultValue = data.GetFloatProperty("m_flDefaultValue");
        EventConditionRules = new(data.GetProperty<KVObject>("m_eventConditionRules"));
    }
}
