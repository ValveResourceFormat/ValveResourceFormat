using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class GraphEventConditionNode
{
    [KV3Transfer]
    internal partial class Condition
    {
        public GlobalSymbol EventID { get; }
        public GraphEventTypeCondition EventTypeCondition { get; }
    }
}
