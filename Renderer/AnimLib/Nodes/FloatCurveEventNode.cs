using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FloatCurveEventNode : FloatValueNode
{
    public GlobalSymbol EventID { get; }
    public short DefaultNodeIdx { get; } = -1;
    public float DefaultValue { get; }
    public BitFlags EventConditionRules { get; }
}
