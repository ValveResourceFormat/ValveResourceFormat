using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation2
{
    /// <summary>
    /// An event embedded in an animation clip (m_events). Typed subclasses exist for the classes with
    /// known consumers (<see cref="NmSoundEvent"/>, <see cref="NmIDEvent"/>, <see cref="NmParticleEvent"/>,
    /// <see cref="NmLegacyEvent"/>); other classes are represented by this base with their raw data available
    /// through <see cref="Data"/>. Consumers filter the event array for the types they are interested in.
    /// </summary>
    public class NmClipEvent
    {
        /// <summary>Gets the runtime class name, e.g. "CNmSoundEvent".</summary>
        public required string ClassName { get; init; }

        /// <summary>Gets the time in seconds into the clip at which the event starts.</summary>
        public required float StartTime { get; init; }

        /// <summary>Gets the duration in seconds of the event window. 0 means a single point in time.</summary>
        public required float Duration { get; init; }

        /// <summary>Gets the sync ID used by the animation system to align clips, empty when unused.</summary>
        public string SyncId { get; init; } = string.Empty;

        /// <summary>Gets the raw event data, for properties not surfaced by the typed subclasses.</summary>
        public required KVObject Data { get; init; }

        /// <summary>
        /// Creates a typed clip event from its resource data. Event times are stored in the resource as
        /// normalized fractions of the clip; they are converted to seconds using <paramref name="clipDuration"/>.
        /// </summary>
        public static NmClipEvent Build(KVObject eventData, float clipDuration)
        {
            var className = eventData.GetStringProperty("_class", string.Empty);
            var startTime = GetTimeValue(eventData, "m_flStartTime") * clipDuration;
            var duration = GetTimeValue(eventData, "m_flDuration") * clipDuration;
            var syncId = eventData.GetStringProperty("m_syncID", string.Empty);

            return className switch
            {
                "CNmSoundEvent" => new NmSoundEvent
                {
                    ClassName = className,
                    StartTime = startTime,
                    Duration = duration,
                    SyncId = syncId,
                    Data = eventData,
                    Name = eventData.GetStringProperty("m_name", string.Empty),
                    Position = eventData.GetStringProperty("m_position", "EntityEyePos"),
                    AttachmentName = eventData.GetStringProperty("m_attachmentName", string.Empty),
                    Relevance = eventData.GetStringProperty("m_relevance", "ClientOnly"),
                    ContinuePlayingSoundAtDurationEnd = eventData.GetBooleanProperty("m_bContinuePlayingSoundAtDurationEnd"),
                    DurationInterruptionThreshold = eventData.GetFloatProperty("m_flDurationInterruptionThreshold", 0.9f),
                },
                "CNmIDEvent" => new NmIDEvent
                {
                    ClassName = className,
                    StartTime = startTime,
                    Duration = duration,
                    SyncId = syncId,
                    Data = eventData,
                    ID = eventData.GetStringProperty("m_ID", string.Empty),
                    SecondaryID = eventData.GetStringProperty("m_secondaryID", string.Empty),
                },
                "CNmParticleEvent" => new NmParticleEvent
                {
                    ClassName = className,
                    StartTime = startTime,
                    Duration = duration,
                    SyncId = syncId,
                    Data = eventData,
                    Type = eventData.GetStringProperty("m_type", string.Empty),
                    Target = eventData.GetStringProperty("m_target", string.Empty),
                    ParticleSystemName = eventData.GetStringProperty("m_hParticleSystem", string.Empty),
                    Relevance = eventData.GetStringProperty("m_relevance", "ClientOnly"),
                    StopImmediately = eventData.GetBooleanProperty("m_bStopImmediately"),
                    DetachFromOwner = eventData.GetBooleanProperty("m_bDetachFromOwner"),
                    PlayEndCap = eventData.GetBooleanProperty("m_bPlayEndCap"),
                    AttachmentPoint0 = eventData.GetStringProperty("m_attachmentPoint0", string.Empty),
                    AttachmentType0 = eventData.GetStringProperty("m_attachmentType0", string.Empty),
                    AttachmentPoint1 = eventData.GetStringProperty("m_attachmentPoint1", string.Empty),
                    AttachmentType1 = eventData.GetStringProperty("m_attachmentType1", string.Empty),
                },
                "CNmLegacyEvent" => new NmLegacyEvent
                {
                    ClassName = className,
                    StartTime = startTime,
                    Duration = duration,
                    SyncId = syncId,
                    Data = eventData,
                    AnimEventClassName = eventData.GetStringProperty("m_animEventClassName", string.Empty),
                },
                // CNmOrientationWarpEvent, CNmFloatCurveEvent, CNmMaterialAttributeEvent, and future classes
                _ => new NmClipEvent
                {
                    ClassName = className,
                    StartTime = startTime,
                    Duration = duration,
                    SyncId = syncId,
                    Data = eventData,
                },
            };
        }

        private static float GetTimeValue(KVObject eventData, string name)
        {
            return eventData.TryGetValue(name, out _)
                ? eventData.GetSubCollection(name)?.GetFloatProperty("m_flValue") ?? 0f
                : 0f;
        }
    }

    /// <summary>
    /// A sound event fired during clip playback (CNmSoundEvent).
    /// </summary>
    public sealed class NmSoundEvent : NmClipEvent
    {
        /// <summary>Gets the name of the sound event to play.</summary>
        public required string Name { get; init; }

        /// <summary>Gets where the sound is positioned: "EntityEyePos", "EntityPos", or "None".</summary>
        public string Position { get; init; } = "EntityEyePos";

        /// <summary>Gets the attachment the sound follows when set.</summary>
        public string AttachmentName { get; init; } = string.Empty;

        /// <summary>Gets the network relevance: "ClientOnly" or "ClientAndServer".</summary>
        public string Relevance { get; init; } = "ClientOnly";

        /// <summary>Gets whether the sound keeps playing to its natural end when the event window closes (otherwise it is stopped).</summary>
        public bool ContinuePlayingSoundAtDurationEnd { get; init; }

        /// <summary>
        /// Gets the fraction of <see cref="NmClipEvent.Duration"/> that must have elapsed for the sound to survive
        /// an interruption of the animation; interrupted earlier, the sound is stopped.
        /// </summary>
        public float DurationInterruptionThreshold { get; init; } = 0.9f;
    }

    /// <summary>
    /// A named marker window used by game code and animation graph transitions (CNmIDEvent).
    /// </summary>
    public sealed class NmIDEvent : NmClipEvent
    {
        /// <summary>Gets the primary ID, e.g. "WPN_INSPECT_INTRO".</summary>
        public required string ID { get; init; }

        /// <summary>Gets the secondary ID, usually empty.</summary>
        public string SecondaryID { get; init; } = string.Empty;
    }

    /// <summary>
    /// A particle effect event (CNmParticleEvent), e.g. the C4 LED pulse on the viewmodel.
    /// </summary>
    public sealed class NmParticleEvent : NmClipEvent
    {
        /// <summary>Gets the operation, e.g. "Create".</summary>
        public required string Type { get; init; }

        /// <summary>Gets the target entity, e.g. "Self".</summary>
        public required string Target { get; init; }

        /// <summary>Gets the particle system resource name.</summary>
        public required string ParticleSystemName { get; init; }

        /// <summary>Gets the network relevance.</summary>
        public string Relevance { get; init; } = "ClientOnly";

        /// <summary>Gets whether the effect is destroyed instantly instead of fading out.</summary>
        public bool StopImmediately { get; init; }

        /// <summary>Gets whether the effect detaches from its owner after creation.</summary>
        public bool DetachFromOwner { get; init; }

        /// <summary>Gets whether the end cap effect plays when the effect stops.</summary>
        public bool PlayEndCap { get; init; }

        /// <summary>Gets the first attachment point name.</summary>
        public string AttachmentPoint0 { get; init; } = string.Empty;

        /// <summary>Gets the first attachment type, e.g. "PATTACH_POINT_FOLLOW".</summary>
        public string AttachmentType0 { get; init; } = string.Empty;

        /// <summary>Gets the second attachment point name.</summary>
        public string AttachmentPoint1 { get; init; } = string.Empty;

        /// <summary>Gets the second attachment type.</summary>
        public string AttachmentType1 { get; init; } = string.Empty;
    }

    /// <summary>
    /// A legacy animation event carried over from the old animation system (CNmLegacyEvent),
    /// e.g. "AE_WEAPON_PERFORM_ATTACK".
    /// </summary>
    public sealed class NmLegacyEvent : NmClipEvent
    {
        /// <summary>Gets the legacy animation event class name.</summary>
        public required string AnimEventClassName { get; init; }
    }
}
