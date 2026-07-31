using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class StateNode : PoseNode
{
    public short ChildNodeIdx { get; }
    public GlobalSymbol[] EntryEvents { get; }
    public GlobalSymbol[] ExecuteEvents { get; }
    public GlobalSymbol[] ExitEvents { get; }
    public StateNode__TimedEvent[] TimedRemainingEvents { get; }
    public StateNode__TimedEvent[] TimedElapsedEvents { get; }
    public short LayerWeightNodeIdx { get; }
    public short LayerRootMotionWeightNodeIdx { get; }
    public short LayerBoneMaskNodeIdx { get; }
    public bool IsOffState { get; }
    public bool UseActualElapsedTimeInStateForTimedEvents { get; }

    public StateNode(KVObject data) : base(data)
    {
        ChildNodeIdx = data.GetInt16Property("m_nChildNodeIdx");
        EntryEvents = data.GetSymbolArray("m_entryEvents");
        ExecuteEvents = data.GetSymbolArray("m_executeEvents");
        ExitEvents = data.GetSymbolArray("m_exitEvents");
        TimedRemainingEvents = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_timedRemainingEvents"), kv => new StateNode__TimedEvent(kv))];
        TimedElapsedEvents = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_timedElapsedEvents"), kv => new StateNode__TimedEvent(kv))];
        LayerWeightNodeIdx = data.GetInt16Property("m_nLayerWeightNodeIdx");
        LayerRootMotionWeightNodeIdx = data.GetInt16Property("m_nLayerRootMotionWeightNodeIdx");
        LayerBoneMaskNodeIdx = data.GetInt16Property("m_nLayerBoneMaskNodeIdx");
        IsOffState = data.GetProperty<bool>("m_bIsOffState");
        UseActualElapsedTimeInStateForTimedEvents = data.GetProperty<bool>("m_bUseActualElapsedTimeInStateForTimedEvents");
    }
}
