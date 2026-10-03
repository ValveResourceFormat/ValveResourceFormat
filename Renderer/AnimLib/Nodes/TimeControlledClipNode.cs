using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class TimeControlledClipNode : PoseNode
{
    public short PlayInReverseValueNodeIdx { get; }
    public bool SampleRootMotion { get; }
    public short DataSlotIdx { get; }
    public short TimeValueNodeIdx { get; }
    public GlobalSymbol[] GraphEvents { get; }

    public TimeControlledClipNode(KVObject data) : base(data)
    {
        PlayInReverseValueNodeIdx = data.GetInt16Property("m_nPlayInReverseValueNodeIdx");
        SampleRootMotion = data.GetProperty<bool>("m_bSampleRootMotion");
        DataSlotIdx = data.GetInt16Property("m_nDataSlotIdx");
        TimeValueNodeIdx = data.GetInt16Property("m_nTimeValueNodeIdx");
        GraphEvents = data.GetSymbolArray("m_graphEvents");
    }
}
