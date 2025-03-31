using ValveResourceFormat.ResourceTypes.Choreo.Enums;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    public class ChoreoEvent
    {
        public ChoreoEventType Type { get; init; }
        public string Name { get; init; }
        public float StartTime { get; init; }
        public float EndTime { get; init; }
        public string Param1 { get; init; }
        public string Param2 { get; init; }
        public string Param3 { get; init; }
        public ChoreoCurveData Ramp { get; init; }
        public ChoreoFlags Flags { get; init; }
        public float DistanceToTarget { get; init; }
        public ChoreoTag[] RelativeTags { get; init; }
        public ChoreoTag[] FlexTimingTags { get; init; }
        public ChoreoTag[] PlaybackTimeTags { get; init; }
        public ChoreoTag[] ShiftedTimeTags { get; init; }
        public float SequenceDuration { get; init; }
        public ChoreoEventRelativeTag RelativeTag { get; init; }
        public ChoreoEventFlex EventFlex { get; init; }
        public byte LoopCount { get; init; }
        public ChoreoClosedCaptionsType ClosedCaptionsType { get; init; }
        public string ClosedCaptionsToken { get; init; }
        public ChoreoSpeakFlags SpeakFlags { get; init; }
        public float SoundStartDelay { get; init; }
        public int Id { get; init; }
        public int ConstrainedEventId { get; init; }
        public string PreferredName { get; init; }

        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null);

            var type = TypeToKVString();
            kv.AddProperty("type", new KVValue(KVType.STRING, type));

            kv.AddProperty("name", new KVValue(KVType.STRING, Name));
            kv.AddProperty("start_time", new KVValue(KVType.FLOAT, StartTime));
            kv.AddProperty("end_time", new KVValue(KVType.FLOAT, EndTime));
            kv.AddProperty("param", new KVValue(KVType.STRING, Param1));
            kv.AddProperty("param2", new KVValue(KVType.STRING, Param2));
            kv.AddProperty("param3", new KVValue(KVType.STRING, Param3));

            if (ClosedCaptionsType != ChoreoClosedCaptionsType.None)
            {
                var ccType = ClosedCaptionsType switch
                {
                    ChoreoClosedCaptionsType.Master => "cc_master",
                    ChoreoClosedCaptionsType.Slave => "cc_slave",
                    ChoreoClosedCaptionsType.Disabled => "cc_disabled",
                    _ => ""
                };
                kv.AddProperty("cctype", new KVValue(KVType.STRING, ccType));
                kv.AddProperty("cctoken", new KVValue(KVType.STRING, ClosedCaptionsToken));
            }

            AddKVFlag(kv, "cc_noattenuate", SpeakFlags, ChoreoSpeakFlags.SuppressingCaptionAttenuation, false);
            AddKVFlag(kv, "cc_usingcombinedfile", SpeakFlags, ChoreoSpeakFlags.UsingCombinedFile, false);
            AddKVFlag(kv, "cc_combinedusesgender", SpeakFlags, ChoreoSpeakFlags.CombinedUsingGenderToken, false);
            AddKVFlag(kv, "hardstopspeakevent", SpeakFlags, ChoreoSpeakFlags.HardStopSpeakEvent, false);
            AddKVFlag(kv, "volumematcheseventramp", SpeakFlags, ChoreoSpeakFlags.VolumeMatchesEventRamp, false);
            kv.AddProperty("startdelay", new KVValue(KVType.FLOAT, SoundStartDelay));

            AddKVFlag(kv, "resumecondition", Flags, ChoreoFlags.ResumeCondition, false);
            AddKVFlag(kv, "active", Flags, ChoreoFlags.IsActive, true);
            AddKVFlag(kv, "fixedlength", Flags, ChoreoFlags.FixedLength, false);
            AddKVFlag(kv, "playoverscript", Flags, ChoreoFlags.PlayOverScript, false);
            AddKVFlag(kv, "forceshortmovement", Flags, ChoreoFlags.ForceShortMovement, false);
            AddKVFlag(kv, "lockbodyfacing", Flags, ChoreoFlags.LockBodyFacing, false);

            if (Type == ChoreoEventType.Loop)
            {
                var loopCount = LoopCount == byte.MaxValue ? -1 : LoopCount;
                kv.AddProperty("loopcount", new KVValue(KVType.INT64, loopCount));
            }
            else if (Type == ChoreoEventType.Gesture)
            {
                kv.AddProperty("sequenceduration", new KVValue(KVType.FLOAT, SequenceDuration));
            }

            if (RelativeTag != null)
            {
                kv.AddProperty("relativetag_name", new KVValue(KVType.STRING, RelativeTag.Name));
                kv.AddProperty("relativetag_sound", new KVValue(KVType.STRING, RelativeTag.SoundName));
            }

            if (DistanceToTarget != 0.0f)
            {
                kv.AddProperty("distancetotarget", new KVValue(KVType.FLOAT, DistanceToTarget));
            }

            if (ConstrainedEventId != 0)
            {
                kv.AddProperty("constrainedEventID", new KVValue(KVType.INT64, ConstrainedEventId));
            }

            kv.AddProperty("eventID", new KVValue(KVType.INT64, Id));

            if (Ramp?.LeftEdge != null)
            {
                kv.AddProperty("left_edge", new KVValue(KVType.OBJECT, Ramp.LeftEdge.ToKeyValues()));
            }
            if (Ramp?.RightEdge != null)
            {
                kv.AddProperty("right_edge", new KVValue(KVType.OBJECT, Ramp.RightEdge.ToKeyValues()));
            }
            if (Ramp.Samples.Length > 0)
            {
                kv.AddProperty("event_ramp", new KVValue(KVType.OBJECT, Ramp.ToKeyValues()));
            }

            AddTagArrayToKV(kv, "flextimingtags", FlexTimingTags);
            AddTagArrayToKV(kv, "tags", RelativeTags);
            AddTagArrayToKV(kv, "playback_time", PlaybackTimeTags);
            AddTagArrayToKV(kv, "shifted_time", ShiftedTimeTags);

            if (EventFlex.Tracks.Length > 0)
            {
                //If samples_use_time is 1, samples in the flex animations are interpreted as real time (probably meaning values are not clamped to 0.0-1.0)
                //They're stored as real time in the vcd, so this has to be set to true
                kv.AddProperty("samples_use_time", new KVValue(KVType.BOOLEAN, true));
                kv.AddProperty("flexanimations", new KVValue(KVType.OBJECT, EventFlex.ToKeyValues()));
            }

            if (PreferredName != null)
            {
                kv.AddProperty("preferred_name", new KVValue(KVType.STRING, PreferredName));
            }

            return kv;
        }

        private static void AddTagArrayToKV(KVObject parent, string name, ChoreoTag[] tags)
        {
            if (tags.Length == 0)
            {
                return;
            }

            var kv = new KVObject(null, true, tags.Length);

            foreach (var tag in tags)
            {
                var tagKV = new KVObject(null);
                tagKV.AddProperty("name", new KVValue(KVType.STRING, tag.Name));
                tagKV.AddProperty("fraction", new KVValue(KVType.FLOAT, tag.Fraction));

                kv.AddProperty(null, new KVValue(KVType.OBJECT, tagKV));
            }

            parent.AddProperty(name, new KVValue(KVType.ARRAY, kv));
        }

        private static void AddKVFlag(KVObject kv, string name, Enum setFlags, Enum flag, bool defaultValue = false)
        {
            var set = setFlags.HasFlag(flag);
            if (set == defaultValue)
            {
                return;
            }
            kv.AddProperty(name, new KVValue(KVType.BOOLEAN, set));
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
