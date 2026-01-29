using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class IDToFloatNode : FloatValueNode
{
    public short InputValueNodeIdx { get; }
    public float DefaultValue { get; }
    public GlobalSymbol[] IDs { get; }
    public float[] Values { get; }

    public IDToFloatNode(KVObject data) : base(data)
    {
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
        DefaultValue = data.GetFloatProperty("m_defaultValue");
        IDs = data.GetArray<GlobalSymbol>("m_IDs");
        Values = data.GetArray<float>("m_values");
    }
}
