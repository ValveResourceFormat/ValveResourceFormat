using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class IDToFloatNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    [KVProperty("m_defaultValue")]
    public float DefaultValue { get; }
    public GlobalSymbol[] IDs { get; } = [];
    public float[] Values { get; } = [];
}
