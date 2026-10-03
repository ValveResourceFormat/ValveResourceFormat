using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class TransitionNode : PoseNode
{
    public short TargetStateNodeIdx { get; } = -1;
    public short DurationOverrideNodeIdx { get; } = -1;
    [KVProperty("m_timeOffsetOverrideNodeIdx")]
    public short TimeOffsetOverrideNodeIdx { get; } = -1;
    [KVProperty("m_startBoneMaskNodeIdx")]
    public short StartBoneMaskNodeIdx { get; } = -1;
    [KVProperty("m_flDuration")]
    public float DurationSeconds { get; } // Definition duration from file
    public Percent BoneMaskBlendInTimePercentage { get; }
    public float TimeOffset { get; }
    public BitFlags TransitionOptions { get; }
    [KVProperty("m_targetSyncIDNodeIdx")]
    public short TargetSyncIDNodeIdx { get; } = -1;
    public EasingOperation BlendWeightEasing { get; }
    public RootMotionBlendMode RootMotionBlend { get; }
}
