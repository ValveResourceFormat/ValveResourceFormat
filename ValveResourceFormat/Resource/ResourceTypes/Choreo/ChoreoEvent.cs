using ValveResourceFormat.ResourceTypes.Choreo.Enums;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    public class ChoreoEvent
    {
        public ChoreoEventType Type { get; private set; }
        public string Name { get; private set; }
        public float StartTime { get; private set; }
        public float EndTime { get; private set; }
        public string Param1 { get; private set; }
        public string Param2 { get; private set; }
        public string Param3 { get; private set; }
        public ChoreoCurveData Ramp { get; private set; }
        public ChoreoFlags Flags { get; private set; }
        public float DistanceToTarget { get; private set; }
        public ChoreoEventRelativeTag[] RelativeTags { get; private set; }
        public ChoreoFlexTimingTag[] FlexTimingTags { get; private set; }
        public ChoreoEventAbsoluteTag[] AbsoluteTags { get; private set; }
        public float SequenceDuration { get; private set; }
        public bool UsingRelativeTag { get; private set; }
        public ChoreoEventRelativeTag RelativeTag { get; private set; }
        public ChoreoEventFlex EventFlex { get; private set; }
        public byte LoopCount { get; private set; }
        public ChoreoClosedCaptions ClosedCaptions { get; private set; }
        public int Id { get; private set; }
        public int ConstrainedEventId { get; private set; }

        //todo: ew
        public ChoreoEvent(ChoreoEventType type, string name, float startTime, float endTime, string param1, string param2, string param3, ChoreoCurveData ramp, ChoreoFlags flags, float distanceToTarget, ChoreoEventRelativeTag[] relativeTags, ChoreoFlexTimingTag[] flexTimingTags, ChoreoEventAbsoluteTag[] absoluteTags, float sequenceDuration, bool usingRelativeTag, ChoreoEventRelativeTag relativeTag, ChoreoEventFlex eventFlex, byte loopCount, ChoreoClosedCaptions closedCaptions, int id, int unk01)
        {
            Type = type;
            Name = name;
            StartTime = startTime;
            EndTime = endTime;
            Param1 = param1;
            Param2 = param2;
            Param3 = param3;
            Ramp = ramp;
            Flags = flags;
            DistanceToTarget = distanceToTarget;
            RelativeTags = relativeTags;
            FlexTimingTags = flexTimingTags;
            AbsoluteTags = absoluteTags;
            SequenceDuration = sequenceDuration;
            UsingRelativeTag = usingRelativeTag;
            RelativeTag = relativeTag;
            EventFlex = eventFlex;
            LoopCount = loopCount;
            ClosedCaptions = closedCaptions;
            Id = id;
            ConstrainedEventId = unk01;
        }

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

            if (ClosedCaptions != null)
            {
                var ccType = ClosedCaptions.Type switch
                {
                    ChoreoClosedCaptionsType.Master => "cc_master",
                    ChoreoClosedCaptionsType.Slave => "cc_slave",
                    ChoreoClosedCaptionsType.Disabled => "cc_disabled",
                    _ => ""
                };
                kv.AddProperty("cctype", new KVValue(KVType.STRING, ccType));

                kv.AddProperty("cctoken", new KVValue(KVType.STRING, ClosedCaptions.Token));

                var noAttentuate = ClosedCaptions.Flags.HasFlag(ChoreoClosedCaptionsFlags.SuppressingCaptionAttenuation);
                kv.AddProperty("cc_noattenuate", new KVValue(KVType.BOOLEAN, noAttentuate));

                //TODO: These are not closed caption related flags. Should closedcaptions class be renamed?
                var hardStopSpeakEvent = ClosedCaptions.Flags.HasFlag(ChoreoClosedCaptionsFlags.HardStopSpeakEvent);
                kv.AddProperty("hardstopspeakevent", new KVValue(KVType.BOOLEAN, hardStopSpeakEvent));

                var volumeMatchesEventRamp = ClosedCaptions.Flags.HasFlag(ChoreoClosedCaptionsFlags.VolumeMatchesEventRamp);
                kv.AddProperty("volumematcheseventramp", new KVValue(KVType.BOOLEAN, volumeMatchesEventRamp));

                //TODO: Print the rest of the caption flags
            }

            AddKVFlag(kv, "resumecondition", ChoreoFlags.ResumeCondition, false);
            AddKVFlag(kv, "active", ChoreoFlags.IsActive, true);
            AddKVFlag(kv, "fixedlength", ChoreoFlags.FixedLength, false);
            AddKVFlag(kv, "playoverscript", ChoreoFlags.PlayOverScript, false);
            AddKVFlag(kv, "forceshortmovement", ChoreoFlags.ForceShortMovement, false);
            AddKVFlag(kv, "lockbodyfacing", ChoreoFlags.LockBodyFacing, false);

            if (Type == ChoreoEventType.Loop)
            {
                kv.AddProperty("loopcount", new KVValue(KVType.INT64, LoopCount));
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
            //TODO: Missing properties:
            //synctofollowinggesture (missing from bvcd?)
            //pitch (missing from bvcd?)
            //tag arrays


            if (Ramp.Samples.Length > 0)
            {
                kv.AddProperty("event_ramp", new KVValue(KVType.OBJECT, Ramp.ToKeyValues()));
            }

            AddTagArrayToKV(kv, "flextimingtags", FlexTimingTags);
            AddTagArrayToKV(kv, "tags", RelativeTags);

            if (EventFlex.Tracks.Length > 0)
            {
                //samples_use_time changes how sample times are interpreted when (re)compiling the .vcd.
                //TODO: Verify whether samples_use_time is only used by flexanimations
                kv.AddProperty("samples_use_time", new KVValue(KVType.BOOLEAN, true));
                kv.AddProperty("flexanimations", new KVValue(KVType.OBJECT, EventFlex.ToKeyValues()));
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
                tagKV.AddProperty("fraction", new KVValue(KVType.FLOAT, tag.Duration));

                kv.AddProperty(null, new KVValue(KVType.OBJECT, tagKV));
            }

            parent.AddProperty(name, new KVValue(KVType.ARRAY, kv));
        }

        private void AddKVFlag(KVObject kv, string name, ChoreoFlags flag, bool defaultValue = false)
        {
            var set = Flags.HasFlag(flag);
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
                ChoreoEventType.Expression => "expression",
                ChoreoEventType.Speak => "speak",
                ChoreoEventType.Gesture => "gesture",
                ChoreoEventType.LookAt => "lookat",
                //case ChoreoEventType.LookAtTransition: return "lookattransition";
                ChoreoEventType.MoveTo => "moveto",
                ChoreoEventType.Face => "face",
                //case ChoreoEventType.FaceTransition: return "facetransition";
                ChoreoEventType.FireTrigger => "firetrigger",
                ChoreoEventType.Generic => "generic",
                ChoreoEventType.Sequence => "sequence",
                ChoreoEventType.FlexAnimation => "flexanimation",
                ChoreoEventType.AnimgraphController => "animgraphcontroller",
                //case ChoreoEventType.iklockleftarm: return "iklockleftarm";
                //case ChoreoEventType.iklockrightarm: return "iklockrightarm";
                ChoreoEventType.SubScene => "subscene",
                ChoreoEventType.Interrupt => "interrupt",
                ChoreoEventType.PermitResponses => "permitresponses",
                ChoreoEventType.Camera => "camera",
                ChoreoEventType.Loop => "loop",
                ChoreoEventType.Section => "section",
                ChoreoEventType.StopPoint => "stoppoint",//TODO: verify stoppoint event's name
                _ => throw new UnexpectedMagicException($"Unknown event type", (int)Type, nameof(Type)),
            };
        }
    }
}
