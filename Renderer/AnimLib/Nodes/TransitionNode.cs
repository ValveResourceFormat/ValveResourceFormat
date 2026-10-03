using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class TransitionNode : PoseNode
{
    public short TargetStateNodeIdx { get; }
    public short DurationOverrideNodeIdx { get; }
    public short TimeOffsetOverrideNodeIdx { get; }
    public short StartBoneMaskNodeIdx { get; }
    public float DurationSeconds { get; } // Definition duration from file
    public Percent BoneMaskBlendInTimePercentage { get; }
    public float TimeOffset { get; }
    public BitFlags TransitionOptions { get; }
    public short TargetSyncIDNodeIdx { get; }
    public EasingOperation BlendWeightEasing { get; }
    public RootMotionBlendMode RootMotionBlend { get; }

    public TransitionNode(KVObject data) : base(data)
    {
        TargetStateNodeIdx = data.GetInt16Property("m_nTargetStateNodeIdx");
        DurationOverrideNodeIdx = data.GetInt16Property("m_nDurationOverrideNodeIdx");
        TimeOffsetOverrideNodeIdx = data.GetInt16Property("m_timeOffsetOverrideNodeIdx");
        StartBoneMaskNodeIdx = data.GetInt16Property("m_startBoneMaskNodeIdx");
        DurationSeconds = data.GetFloatProperty("m_flDuration");
        BoneMaskBlendInTimePercentage = new(data.GetProperty<KVObject>("m_boneMaskBlendInTimePercentage"));
        TimeOffset = data.GetFloatProperty("m_flTimeOffset");
        TransitionOptions = new(data.GetProperty<KVObject>("m_transitionOptions"));
        TargetSyncIDNodeIdx = data.GetInt16Property("m_targetSyncIDNodeIdx");
        BlendWeightEasing = data.GetEnumValue<EasingOperation>("m_blendWeightEasing");
        RootMotionBlend = data.GetEnumValue<RootMotionBlendMode>("m_rootMotionBlend");
    }
}
