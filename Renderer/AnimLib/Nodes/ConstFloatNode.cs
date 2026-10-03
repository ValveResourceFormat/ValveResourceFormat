using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class ConstFloatNode : FloatValueNode
{
    public float Value { get; }

    public ConstFloatNode(KVObject data) : base(data)
    {
        Value = data.GetFloatProperty("m_flValue");
    }
}
