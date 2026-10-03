using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

// Note: outputs a float (the percentage through the matched event), FloatValueNode in Esoterica
[KV3Transfer]
partial class IDEventPercentageThroughNode : FloatValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public BitFlags EventConditionRules { get; }
    public GlobalSymbol EventID { get; }
}
