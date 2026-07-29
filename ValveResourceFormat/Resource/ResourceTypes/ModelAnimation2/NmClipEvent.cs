using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation2
{
    /// <summary>
    /// An event embedded in an animation clip (m_events). Typed subclasses exist for the classes with
    /// known consumers (<see cref="NmSoundEvent"/>, <see cref="NmIDEvent"/>, <see cref="NmParticleEvent"/>,
    /// <see cref="NmLegacyEvent"/>); other classes are represented by this base with their raw data available
    /// through <see cref="Data"/>. Consumers filter the event array for the types they are interested in.
    /// </summary>
    public class NmClipEvent : IAnimationEvent
    {
        /// <summary>Gets the runtime class name, e.g. "CNmSoundEvent".</summary>
        public string ClassName { get; }

        /// <inheritdoc/>
        /// <remarks>
        /// Clips author their event times as this cycle, <see cref="StartTime"/> is derived from it.
        /// </remarks>
        public float StartCycle { get; }

        /// <summary>Gets the time in seconds into the clip at which the event starts.</summary>
        public float StartTime { get; }

        /// <summary>Gets the duration in seconds of the event window. Zero means a single point in time.</summary>
        public float Duration { get; }

        /// <summary>Gets the sync ID used by the animation system to align clips, empty when unused.</summary>
        public string SyncId { get; }

        /// <summary>Gets the raw event data, for properties not surfaced by the typed subclasses.</summary>
        public KVObject Data { get; }

        /// <summary>
        /// Initializes the properties every event class has. Event times are stored in the resource as
        /// normalized fractions of the clip; they are converted to seconds using <paramref name="clipDuration"/>.
        /// </summary>
        protected NmClipEvent(KVObject eventData, float clipDuration)
        {
            ClassName = eventData.GetStringProperty("_class", string.Empty);
            StartCycle = GetTimeValue(eventData, "m_flStartTime");
            StartTime = StartCycle * clipDuration;
            Duration = GetTimeValue(eventData, "m_flDuration") * clipDuration;
            SyncId = eventData.GetStringProperty("m_syncID", string.Empty);
            Data = eventData;
        }

        /// <summary>
        /// Creates a typed clip event from its resource data.
        /// </summary>
        public static NmClipEvent Build(KVObject eventData, float clipDuration)
        {
            return eventData.GetStringProperty("_class", string.Empty) switch
            {
                "CNmSoundEvent" => new NmSoundEvent(eventData, clipDuration),
                "CNmIDEvent" => new NmIDEvent(eventData, clipDuration),
                "CNmParticleEvent" => new NmParticleEvent(eventData, clipDuration),
                "CNmLegacyEvent" => new NmLegacyEvent(eventData, clipDuration),
                // CNmOrientationWarpEvent, CNmFloatCurveEvent, CNmMaterialAttributeEvent, and future classes
                _ => new NmClipEvent(eventData, clipDuration),
            };
        }

        /// <summary>
        /// Reads a time value, which is stored as a struct wrapping the normalized clip fraction.
        /// </summary>
        private static float GetTimeValue(KVObject eventData, string name)
        {
            return eventData.TryGetValue(name, out _)
                ? eventData.GetSubCollection(name)?.GetFloatProperty("m_flValue") ?? 0f
                : 0f;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Returns the event class name and its time window.
        /// </remarks>
        public override string ToString() => $"{ClassName} @ {StartTime}s +{Duration}s";
    }

    /// <summary>
    /// A sound event fired during clip playback (CNmSoundEvent).
    /// </summary>
    public sealed class NmSoundEvent : NmClipEvent
    {
        /// <summary>Gets the name of the sound event to play.</summary>
        public string Name { get; }

        /// <summary>Gets where the sound is positioned: "EntityEyePos", "EntityPos", or "None".</summary>
        public string Position { get; }

        /// <summary>Gets the attachment the sound follows when set.</summary>
        public string AttachmentName { get; }

        /// <summary>Gets the network relevance: "ClientOnly" or "ClientAndServer".</summary>
        public string Relevance { get; }

        /// <summary>Gets whether the sound keeps playing to its natural end when the event window closes (otherwise it is stopped).</summary>
        public bool ContinuePlayingSoundAtDurationEnd { get; }

        /// <summary>
        /// Gets the fraction of <see cref="NmClipEvent.Duration"/> that must have elapsed for the sound to survive
        /// an interruption of the animation; interrupted earlier, the sound is stopped.
        /// </summary>
        public float DurationInterruptionThreshold { get; }

        internal NmSoundEvent(KVObject eventData, float clipDuration) : base(eventData, clipDuration)
        {
            Name = eventData.GetStringProperty("m_name", string.Empty);
            Position = eventData.GetStringProperty("m_position", "EntityEyePos");
            AttachmentName = eventData.GetStringProperty("m_attachmentName", string.Empty);
            Relevance = eventData.GetStringProperty("m_relevance", "ClientOnly");
            ContinuePlayingSoundAtDurationEnd = eventData.GetBooleanProperty("m_bContinuePlayingSoundAtDurationEnd");
            DurationInterruptionThreshold = eventData.GetFloatProperty("m_flDurationInterruptionThreshold", 0.9f);
        }
    }

    /// <summary>
    /// A named marker window used by game code and animation graph transitions (CNmIDEvent).
    /// </summary>
    public sealed class NmIDEvent : NmClipEvent
    {
        /// <summary>Gets the primary ID, e.g. "WPN_INSPECT_INTRO".</summary>
        public string ID { get; }

        /// <summary>Gets the secondary ID, usually empty.</summary>
        public string SecondaryID { get; }

        internal NmIDEvent(KVObject eventData, float clipDuration) : base(eventData, clipDuration)
        {
            ID = eventData.GetStringProperty("m_ID", string.Empty);
            SecondaryID = eventData.GetStringProperty("m_secondaryID", string.Empty);
        }
    }

    /// <summary>
    /// A particle effect event (CNmParticleEvent), e.g. the C4 LED pulse on the viewmodel.
    /// </summary>
    public sealed class NmParticleEvent : NmClipEvent
    {
        /// <summary>Gets the operation, e.g. "Create" or "Create_CFG".</summary>
        public string Type { get; }

        /// <summary>Gets the target entity, e.g. "Self" or "Weapon".</summary>
        public string Target { get; }

        /// <summary>Gets the particle system resource name.</summary>
        public string ParticleSystemName { get; }

        /// <summary>Gets the network relevance.</summary>
        public string Relevance { get; }

        /// <summary>Gets whether the effect is destroyed instantly instead of fading out.</summary>
        public bool StopImmediately { get; }

        /// <summary>Gets whether the effect detaches from its owner after creation.</summary>
        public bool DetachFromOwner { get; }

        /// <summary>Gets whether the end cap effect plays when the effect stops.</summary>
        public bool PlayEndCap { get; }

        /// <summary>Gets the first attachment point name.</summary>
        public string AttachmentPoint0 { get; }

        /// <summary>Gets the first attachment type, e.g. "PATTACH_POINT_FOLLOW".</summary>
        public string AttachmentType0 { get; }

        /// <summary>Gets the second attachment point name.</summary>
        public string AttachmentPoint1 { get; }

        /// <summary>Gets the second attachment type.</summary>
        public string AttachmentType1 { get; }

        internal NmParticleEvent(KVObject eventData, float clipDuration) : base(eventData, clipDuration)
        {
            Type = eventData.GetStringProperty("m_type", string.Empty);
            Target = eventData.GetStringProperty("m_target", string.Empty);
            ParticleSystemName = eventData.GetStringProperty("m_hParticleSystem", string.Empty);
            Relevance = eventData.GetStringProperty("m_relevance", "ClientOnly");
            StopImmediately = eventData.GetBooleanProperty("m_bStopImmediately");
            DetachFromOwner = eventData.GetBooleanProperty("m_bDetachFromOwner");
            PlayEndCap = eventData.GetBooleanProperty("m_bPlayEndCap");
            AttachmentPoint0 = eventData.GetStringProperty("m_attachmentPoint0", string.Empty);
            AttachmentType0 = eventData.GetStringProperty("m_attachmentType0", string.Empty);
            AttachmentPoint1 = eventData.GetStringProperty("m_attachmentPoint1", string.Empty);
            AttachmentType1 = eventData.GetStringProperty("m_attachmentType1", string.Empty);
        }
    }

    /// <summary>
    /// A legacy animation event carried over from the old animation system (CNmLegacyEvent),
    /// e.g. "AE_WEAPON_PERFORM_ATTACK".
    /// </summary>
    public sealed class NmLegacyEvent : NmClipEvent
    {
        /// <summary>Gets the legacy animation event class name.</summary>
        public string AnimEventClassName { get; }

        internal NmLegacyEvent(KVObject eventData, float clipDuration) : base(eventData, clipDuration)
        {
            AnimEventClassName = eventData.GetStringProperty("m_animEventClassName", string.Empty);
        }
    }
}
