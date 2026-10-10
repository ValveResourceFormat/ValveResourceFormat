using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class BodyGroupEvent : Event
{
    public EventTargetEntity Target { get; }
    public string GroupName { get; }
    public string ChoiceName { get; }
}

[KV3Transfer]
partial class CameraDOFEvent : Event
{
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve Curve { get; }
}

[KV3Transfer]
partial class CameraFOVEvent : Event
{
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve Curve { get; }
}

[KV3Transfer]
partial class ClothEvent : Event
{
    public ClothEvent.ClothEventType Type { get; }
    public float Stiffness { get; } = 1f;
    public float SpeedIn { get; } = 10f;
    public float SpeedOut { get; } = 10f;
    public float LengthSeconds { get; } = 1f;
    public string VertexSetName { get; }
    public string EffectName { get; }
}

partial class ClothEvent
{
    internal enum ClothEventType : uint
    {
        Stiffen = 0,
        Effect = 1,
    }
}

[KV3Transfer]
partial class ContactEvent : Event
{
    public GlobalSymbol ConfigID { get; }
    public GlobalSymbol ProbeBoneID { get; }
    [KVProperty("m_vBoneLocalProbeDir")]
    public Vector3 VBoneLocalProbeDir { get; } = new(1f, 0f, 0f);
    public float ProbeMaxDist { get; } = 3f;
    public ContactAudioInfo AudioInfo { get; }
}

[KV3Transfer]
partial class EntityAttributeEventBase : Event
{
    public EventTargetEntity Target { get; }
    public string AttributeName { get; }
}

[KV3Transfer]
partial class EntityAttributeFloatEvent : EntityAttributeEventBase
{
    [KVProperty("m_FloatValue")]
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve FloatValue { get; }
}

[KV3Transfer]
partial class EntityAttributeIntEvent : EntityAttributeEventBase
{
    public int IntValue { get; }
}

[KV3Transfer]
partial class Event
{
    [KVProperty("m_flStartTime")]
    public Percent StartTime { get; }
    [KVProperty("m_flDuration")]
    public Percent Duration { get; }
    public GlobalSymbol SyncID { get; }
}

[KV3Transfer]
partial class FloatCurveEvent : Event
{
    public GlobalSymbol ID { get; }
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve Curve { get; }
}

[KV3Transfer]
partial class FootEvent : Event
{
    public FootPhase Phase { get; }
}

[KV3Transfer]
partial class FrameSnapEvent : Event
{
    public FrameSnapEventMode FrameSnapMode { get; }
}

[KV3Transfer]
partial class IDEvent : Event
{
    public GlobalSymbol ID { get; }
    public GlobalSymbol SecondaryID { get; }
}

[KV3Transfer]
partial class LegacyEvent : Event
{
    public string AnimEventClassName { get; }
    public KVObject KV { get; }
}

[KV3Transfer]
partial class MaterialAttributeEvent : Event
{
    public EventTargetEntity Target { get; }
    public string AttributeName { get; }
    public GlobalSymbol AttributeNameToken { get; }
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve X { get; }
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve Y { get; }
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve Z { get; }
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve W { get; }
}

[KV3Transfer] partial class OrientationWarpEvent : Event { }

[KV3Transfer]
partial class ParticleEvent : Event
{
    public EventRelevance Relevance { get; } = EventRelevance.ClientAndServer;
    public ParticleEvent.ParticleEventType Type { get; }
    public EventTargetEntity Target { get; }
    [KVProperty("m_hParticleSystem")]
    public string ParticleSystem { get; } // InfoForResourceTypeIParticleSystemDefinition
    public string Tags { get; }
    public bool StopImmediately { get; }
    public bool DetachFromOwner { get; }
    public bool PlayEndCap { get; }
    public string AttachmentPoint0 { get; }
    public ValveResourceFormat.Particles.ParticleAttachment AttachmentType0 { get; }
    public string AttachmentPoint1 { get; }
    public ValveResourceFormat.Particles.ParticleAttachment AttachmentType1 { get; }
    public string Config { get; }
    public string EffectForConfig { get; }
}

partial class ParticleEvent
{
    internal enum ParticleEventType : uint
    {
        Create = 0,
        Create_CFG = 1,
    }
}

[KV3Transfer]
partial class RootMotionEvent : Event
{
    public float BlendTimeSeconds { get; } = 0.1f;
}

[KV3Transfer]
partial class SoundEvent : Event
{
    public EventRelevance Relevance { get; } = EventRelevance.ClientAndServer;
    public string Name { get; }
    public SoundEvent.PositionType Position { get; }
    public string AttachmentName { get; }
    public string Tags { get; }
    public bool ContinuePlayingSoundAtDurationEnd { get; }
    public float DurationInterruptionThreshold { get; } = 0.9f;
}

partial class SoundEvent
{
    internal enum PositionType : uint
    {
        None = 0,
        World = 1,
        EntityPos = 2,
        EntityEyePos = 3,
        EntityAttachment = 4,
    }
}

[KV3Transfer]
partial class TargetWarpEvent : Event
{
    public TargetWarpRule Rule { get; } = TargetWarpRule.WarpXYZ;
    public TargetWarpAlgorithm Algorithm { get; } = TargetWarpAlgorithm.Bezier;
}

[KV3Transfer]
partial class TransitionEvent : Event
{
    public TransitionRule Rule { get; } = TransitionRule.BlockTransition;
    public GlobalSymbol ID { get; }
}
