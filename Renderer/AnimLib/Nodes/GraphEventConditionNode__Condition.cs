using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class GraphEventConditionNode__Condition
{
    public GlobalSymbol EventID { get; }
    public GraphEventTypeCondition EventTypeCondition { get; }
}
