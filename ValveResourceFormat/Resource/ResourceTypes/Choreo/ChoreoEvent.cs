using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.Choreo.Enums;

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    /// <summary>
    /// Represents an event in a choreography scene.
    /// </summary>
    public class ChoreoEvent
    {
        /// <summary>
        /// Gets the type of the event.
        /// </summary>
        public ChoreoEventType Type { get; init; }

        /// <summary>
        /// Gets the name of the event.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Gets the start time of the event.
        /// </summary>
        public float StartTime { get; init; }

        /// <summary>
        /// Gets the end time of the event.
        /// </summary>
        public float EndTime { get; init; }

        /// <summary>
        /// Gets the first parameter of the event.
        /// </summary>
        public required string Param1 { get; init; }

        /// <summary>
        /// Gets the second parameter of the event.
        /// </summary>
        public required string Param2 { get; init; }

        /// <summary>
        /// Gets the third parameter of the event.
        /// </summary>
        public required string Param3 { get; init; }

        /// <summary>
        /// Gets the ramp curve data for the event.
        /// </summary>
        public required ChoreoCurveData Ramp { get; init; }

        /// <summary>
        /// Gets the flags for the event.
        /// </summary>
        public ChoreoFlags Flags { get; init; }

        /// <summary>
        /// Gets the distance to target.
        /// </summary>
        public float DistanceToTarget { get; init; }

        /// <summary>
        /// Gets the relative tags for the event.
        /// </summary>
        public required ChoreoTag[] RelativeTags { get; init; }

        /// <summary>
        /// Gets the flex timing tags for the event.
        /// </summary>
        public required ChoreoTag[] FlexTimingTags { get; init; }

        /// <summary>
        /// Gets the playback time tags for the event.
        /// </summary>
        public required ChoreoTag[] PlaybackTimeTags { get; init; }

        /// <summary>
        /// Gets the shifted time tags for the event.
        /// </summary>
        public required ChoreoTag[] ShiftedTimeTags { get; init; }

        /// <summary>
        /// Gets the sequence duration.
        /// </summary>
        public float SequenceDuration { get; init; }

        /// <summary>
        /// Gets the relative tag for the event.
        /// </summary>
        public ChoreoEventRelativeTag? RelativeTag { get; init; }

        /// <summary>
        /// Gets the event flex data.
        /// </summary>
        public required ChoreoEventFlex EventFlex { get; init; }

        /// <summary>
        /// Gets the loop count for the event.
        /// </summary>
        public byte LoopCount { get; init; }

        /// <summary>
        /// Gets the closed captions type.
        /// </summary>
        public ChoreoClosedCaptionsType ClosedCaptionsType { get; init; }

        /// <summary>
        /// Gets the closed captions token.
        /// </summary>
        public string? ClosedCaptionsToken { get; init; }

        /// <summary>
        /// Gets the speak flags for the event.
        /// </summary>
        public ChoreoSpeakFlags SpeakFlags { get; init; }

        /// <summary>
        /// Gets the sound start delay.
        /// </summary>
        public float SoundStartDelay { get; init; }

        /// <summary>
        /// Gets the ID of the event.
        /// </summary>
        public int Id { get; init; }

        /// <summary>
        /// Gets the constrained event ID.
        /// </summary>
        public int ConstrainedEventId { get; init; }

        /// <summary>
        /// Gets the preferred name of the event.
        /// </summary>
        public string? PreferredName { get; init; }

        /// <summary>
        /// Converts this event to a <see cref="KVObject"/>.
        /// </summary>
        /// <returns>A <see cref="KVObject"/> representing this event.</returns>
        public KVObject ToKeyValues()
        {
            var kv = KVObject.Collection();

            var type = TypeToKVString();
            kv.Add("type", type);

            kv.Add("name", Name);
            kv.Add("start_time", StartTime);
            kv.Add("end_time", EndTime);
            kv.Add("param", Param1);
            kv.Add("param2", Param2);
            kv.Add("param3", Param3);

            if (ClosedCaptionsType != ChoreoClosedCaptionsType.None)
            {
                var ccType = ClosedCaptionsType switch
                {
                    ChoreoClosedCaptionsType.Master => "cc_master",
                    ChoreoClosedCaptionsType.Slave => "cc_slave",
                    ChoreoClosedCaptionsType.Disabled => "cc_disabled",
                    _ => ""
                };
                kv.Add("cctype", ccType);
                kv.Add("cctoken", ClosedCaptionsToken ?? string.Empty);
            }

            AddKVFlag(kv, "cc_noattenuate", SpeakFlags, ChoreoSpeakFlags.SuppressingCaptionAttenuation, false);
            AddKVFlag(kv, "cc_usingcombinedfile", SpeakFlags, ChoreoSpeakFlags.UsingCombinedFile, false);
            AddKVFlag(kv, "cc_combinedusesgender", SpeakFlags, ChoreoSpeakFlags.CombinedUsingGenderToken, false);
            AddKVFlag(kv, "hardstopspeakevent", SpeakFlags, ChoreoSpeakFlags.HardStopSpeakEvent, false);
            AddKVFlag(kv, "volumematcheseventramp", SpeakFlags, ChoreoSpeakFlags.VolumeMatchesEventRamp, false);
            kv.Add("startdelay", SoundStartDelay);

            AddKVFlag(kv, "resumecondition", Flags, ChoreoFlags.ResumeCondition, false);
            AddKVFlag(kv, "active", Flags, ChoreoFlags.IsActive, true);
            AddKVFlag(kv, "fixedlength", Flags, ChoreoFlags.FixedLength, false);
            AddKVFlag(kv, "playoverscript", Flags, ChoreoFlags.PlayOverScript, false);
            AddKVFlag(kv, "forceshortmovement", Flags, ChoreoFlags.ForceShortMovement, false);
            AddKVFlag(kv, "lockbodyfacing", Flags, ChoreoFlags.LockBodyFacing, false);

            if (Type == ChoreoEventType.Loop)
            {
                var loopCount = LoopCount == byte.MaxValue ? -1 : LoopCount;
                kv.Add("loopcount", loopCount);
            }
            else if (Type == ChoreoEventType.Gesture)
            {
                kv.Add("sequenceduration", SequenceDuration);
            }

            if (RelativeTag != null)
            {
                kv.Add("relativetag_name", RelativeTag.Name);
                kv.Add("relativetag_sound", RelativeTag.SoundName);
            }

            if (DistanceToTarget != 0.0f)
            {
                kv.Add("distancetotarget", DistanceToTarget);
            }

            if (ConstrainedEventId != 0)
            {
                kv.Add("constrainedEventID", ConstrainedEventId);
            }

            kv.Add("eventID", Id);

            if (Ramp.LeftEdge != null)
            {
                kv.Add("left_edge", Ramp.LeftEdge.ToKeyValues());
            }
            if (Ramp.RightEdge != null)
            {
                kv.Add("right_edge", Ramp.RightEdge.ToKeyValues());
            }
            if (Ramp.Samples.Length > 0)
            {
                kv.Add("event_ramp", Ramp.ToKeyValues());
            }

            AddTagArrayToKV(kv, "flextimingtags", FlexTimingTags);
            AddTagArrayToKV(kv, "tags", RelativeTags);
            AddTagArrayToKV(kv, "playback_time", PlaybackTimeTags);
            AddTagArrayToKV(kv, "shifted_time", ShiftedTimeTags);

            if (EventFlex.Tracks.Length > 0)
            {
                //If samples_use_time is 1, samples in the flex animations are interpreted as real time (probably meaning values are not clamped to 0.0-1.0)
                //They're stored as real time in the vcd, so this has to be set to true
                kv.Add("samples_use_time", true);
                kv.Add("flexanimations", EventFlex.ToKeyValues());
            }

            if (PreferredName != null)
            {
                kv.Add("preferred_name", PreferredName);
            }

            return kv;
        }

        private static void AddTagArrayToKV(KVObject parent, string name, ChoreoTag[] tags)
        {
            if (tags.Length == 0)
            {
                return;
            }

            var tagList = KVObject.Array();

            foreach (var tag in tags)
            {
                var tagKV = KVObject.Collection();
                tagKV.Add("name", tag.Name);
                tagKV.Add("fraction", tag.Fraction);

                tagList.Add(tagKV);
            }

            parent.Add(name, tagList);
        }

        private static void AddKVFlag(KVObject kv, string name, Enum setFlags, Enum flag, bool defaultValue = false)
        {
            var set = setFlags.HasFlag(flag);
            if (set == defaultValue)
            {
                return;
            }

            kv.Add(name, set);
        }

        private string TypeToKVString()
        {
            return Type switch
            {
                ChoreoEventType.Unspecified => "unspecified",
                ChoreoEventType.Expression => "expression",
                ChoreoEventType.Speak => "speak",
                ChoreoEventType.Gesture => "gesture",
                ChoreoEventType.LookAt => "lookat",
                ChoreoEventType.MoveTo => "moveto",
                ChoreoEventType.Face => "face",
                ChoreoEventType.FireTrigger => "firetrigger",
                ChoreoEventType.Generic => "generic",
                ChoreoEventType.Sequence => "sequence",
                ChoreoEventType.FlexAnimation => "flexanimation",
                ChoreoEventType.AnimgraphController => "animgraphcontroller",
                ChoreoEventType.SubScene => "subscene",
                ChoreoEventType.Interrupt => "interrupt",
                ChoreoEventType.PermitResponses => "permitresponses",
                ChoreoEventType.Camera => "camera",
                ChoreoEventType.Script => "script",
                ChoreoEventType.Loop => "loop",
                ChoreoEventType.Section => "section",
                ChoreoEventType.StopPoint => "stoppoint",
                ChoreoEventType.MoodBody => "moodbody",
                ChoreoEventType.IKLockLeftArm => "iklockleftarm",
                ChoreoEventType.IKLockRightArm => "iklockrightarm",
                ChoreoEventType.NoBlink => "noblink",
                ChoreoEventType.IgnoreAI => "ignoreai",
                ChoreoEventType.HolsterWeapon => "holsterweapon",
                ChoreoEventType.UnholsterWeapon => "unholsterweapon",
                ChoreoEventType.AimAt => "aimat",
                ChoreoEventType.IgnoreCollision => "ignorecollision",
                ChoreoEventType.IgnoreLookAts => "ignorelookats",
                _ => throw new UnexpectedMagicException($"Unknown event type", (int)Type, nameof(Type)),
            };
        }
    }
}
