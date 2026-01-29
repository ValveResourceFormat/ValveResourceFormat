using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class ClipNode : ClipReferenceNode
{
    public short PlayInReverseValueNodeIdx { get; }
    public short ResetTimeValueNodeIdx { get; }
    public bool SampleRootMotion { get; }
    public bool AllowLooping { get; }
    public short DataSlotIdx { get; }
    public GlobalSymbol[] GraphEvents { get; }
    public float SpeedMultiplier { get; }
    public int StartSyncEventOffset { get; }

    public ClipNode(KVObject data) : base(data)
    {
        PlayInReverseValueNodeIdx = data.GetInt16Property("m_nPlayInReverseValueNodeIdx");
        ResetTimeValueNodeIdx = data.GetInt16Property("m_nResetTimeValueNodeIdx");
        SampleRootMotion = data.GetProperty<bool>("m_bSampleRootMotion");
        AllowLooping = data.GetProperty<bool>("m_bAllowLooping");
        DataSlotIdx = data.GetInt16Property("m_nDataSlotIdx");
        GraphEvents = data.GetArray<GlobalSymbol>("m_graphEvents");
        SpeedMultiplier = data.GetFloatProperty("m_flSpeedMultiplier");
        StartSyncEventOffset = data.GetInt32Property("m_nStartSyncEventOffset");
    }
}
