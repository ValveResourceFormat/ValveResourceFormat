using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class StateNode : PoseNode
{
    public short ChildNodeIdx { get; } = -1;
    public GlobalSymbol[] EntryEvents { get; } = [];
    public GlobalSymbol[] ExecuteEvents { get; } = [];
    public GlobalSymbol[] ExitEvents { get; } = [];
    public StateNode.TimedEvent[] TimedRemainingEvents { get; } = [];
    public StateNode.TimedEvent[] TimedElapsedEvents { get; } = [];
    public short LayerWeightNodeIdx { get; } = -1;
    public short LayerRootMotionWeightNodeIdx { get; } = -1;
    public short LayerBoneMaskNodeIdx { get; } = -1;
    public bool IsOffState { get; }
    public bool UseActualElapsedTimeInStateForTimedEvents { get; }
}
