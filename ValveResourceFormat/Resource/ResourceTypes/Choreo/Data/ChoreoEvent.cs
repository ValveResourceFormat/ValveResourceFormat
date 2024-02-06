using ValveResourceFormat.ResourceTypes.Choreo.Enums;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo.Data
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
        public ChoreoRamp Ramp { get; private set; }
        public ChoreoFlags Flags { get; private set; }
        public float DistanceToTarget { get; private set; }
        public ChoreoRelativeTag[] RelativeTags { get; private set; }
        public ChoreoFlexTimingTag[] FlexTimingTags { get; private set; }
        public ChoreoAbsoluteTag[] AbsoluteTags { get; private set; }
        public float SequenceDuration { get; private set; }
        public bool UsingRelativeTag { get; private set; }
        public ChoreoRelativeTag RelativeTag { get; private set; }
        public ChoreoEventFlex EventFlex { get; private set; }
        public byte LoopCount { get; private set; }
        public ChoreoClosedCaptions ClosedCaptions { get; private set; }
        public int Id { get; private set; }
        public int Unk01 { get; private set; }

        //todo: ew
        public ChoreoEvent(ChoreoEventType type, string name, float startTime, float endTime, string param1, string param2, string param3, ChoreoRamp ramp, ChoreoFlags flags, float distanceToTarget, ChoreoRelativeTag[] relativeTags, ChoreoFlexTimingTag[] flexTimingTags, ChoreoAbsoluteTag[] absoluteTags, float sequenceDuration, bool usingRelativeTag, ChoreoRelativeTag relativeTag, ChoreoEventFlex eventFlex, byte loopCount, ChoreoClosedCaptions closedCaptions, int id, int unk01)
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
            Unk01 = unk01;
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
                var ccType = ClosedCaptions.Type switch {
                    ChoreoClosedCaptionsType.Master => "cc_master",
                    ChoreoClosedCaptionsType.Slave => "cc_slave", //TODO: is this actually named cc_slave?
                    _ => ""
                };
                kv.AddProperty("cctype", new KVValue(KVType.STRING, ccType));

                kv.AddProperty("cctoken", new KVValue(KVType.STRING, ClosedCaptions.Token));

                var noAttentuate = ClosedCaptions.Flags.HasFlag(ChoreoClosedCaptionsFlags.SuppressingCaptionAttenuation);
                kv.AddProperty("cc_noattenuate", new KVValue(KVType.BOOLEAN, noAttentuate));

                //TODO: Print the rest of the caption flags
            }

            AddKVFlag(kv, "resumecondition", ChoreoFlags.ResumeCondition, false);
            AddKVFlag(kv, "active", ChoreoFlags.IsActive, true);
            AddKVFlag(kv, "fixedlength", ChoreoFlags.FixedLength, false);
            AddKVFlag(kv, "playoverscript", ChoreoFlags.PlayOverScript, false);
            AddKVFlag(kv, "forceshortmovement", ChoreoFlags.ForceShortMovement, false);
            AddKVFlag(kv, "lockbodyfacing", ChoreoFlags.LockBodyFacing, false);

            kv.AddProperty("eventID", new KVValue(KVType.INT64, Id));
            //TODO: Missing properties:
            //yaw (LookAt)
            //synctofollowinggesture (Gesture)
            //hardstopspeakevent (Speak)
            //volumematcheseventramp (Speak)
            //pitch (is this even in the bvcd?)


            return kv;
        }

        private void AddKVFlag(KVObject kv, string name, ChoreoFlags flag, bool defaultValue = false)
        {
            var set = Flags.HasFlag(ChoreoFlags.ResumeCondition);
            if (set == defaultValue)
            {
                return;
            }
            kv.AddProperty(name, new KVValue(KVType.BOOLEAN, set));
        }

        private string TypeToKVString()
        {
            switch (Type)
            {
                case ChoreoEventType.Expression: return "expression";
                case ChoreoEventType.Speak: return "speak";
                case ChoreoEventType.Gesture: return "gesture";
                case ChoreoEventType.LookAt: return "lookat";
                //case ChoreoEventType.LookAtTransition: return "lookattransition";
                case ChoreoEventType.MoveTo: return "moveto";
                case ChoreoEventType.Face: return "face";
                //case ChoreoEventType.FaceTransition: return "facetransition";
                case ChoreoEventType.FireTrigger: return "firetrigger";
                case ChoreoEventType.Generic: return "generic";
                case ChoreoEventType.Sequence: return "sequence";
                case ChoreoEventType.FlexAnimation: return "flexanimation";
                case ChoreoEventType.AnimgraphController: return "animgraphcontroller";
                //case ChoreoEventType.iklockleftarm: return "iklockleftarm";
                //case ChoreoEventType.iklockrightarm: return "iklockrightarm";
                case ChoreoEventType.SubScene: return "subscene";
                case ChoreoEventType.Interrupt: return "interrupt";
                case ChoreoEventType.PermitResponses: return "permitresponses";
                case ChoreoEventType.Camera: return "camera";
                case ChoreoEventType.Loop: return "loop";
                case ChoreoEventType.Section: return "section";
                case ChoreoEventType.StopPoint: return "stoppoint"; //TODO: verify stoppoint event's name
                default: throw new NotImplementedException();
            }
        }
    }
}
