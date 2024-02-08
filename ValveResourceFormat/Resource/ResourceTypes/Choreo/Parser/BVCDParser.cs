using System.Diagnostics;
using System.IO;
using System.Linq;
using ValveResourceFormat.ResourceTypes.Choreo.Enums;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes.Choreo.Parser
{
    public class BVCDParser
    {
        public const int MAGIC = 0x64637662; // "bvcd"
        private byte version;
        private BinaryReader reader;
        private string[] strings;

        private BVCDParser(BinaryReader reader, string[] strings)
        {
            this.reader = reader;
            this.strings = strings;
        }

        public static ChoreoScene Parse(Stream stream, string[] strings)
        {
            using BinaryReader reader = new BinaryReader(stream);
            var parser = new BVCDParser(reader, strings);
            return parser.Read();
        }

        protected virtual int ReadStringIndex()
        {
            return reader.ReadInt32();
        }

        protected string ReadString()
        {
            var index = ReadStringIndex();
            return strings[index];
        }

        protected virtual ChoreoScene Read()
        {
            var magic = reader.ReadUInt32();
            if (magic != MAGIC)
            {
                throw new UnexpectedMagicException("The content of the given stream is not bvcd data", magic, "bvcd");
            }
            version = reader.ReadByte();
            var crc = reader.ReadInt32();

            var eventsCount = reader.ReadByte();
            var events = new ChoreoEvent[eventsCount];
            for (var i = 0; i < eventsCount; i++)
            {
                var choreoEvent = ReadEvent();
                events[i] = choreoEvent;
            }

            var actorsCount = reader.ReadByte();
            var actors = new ChoreoActor[actorsCount];
            for (var i = 0; i < actorsCount; i++)
            {
                var actor = ReadActor();
                actors[i] = actor;
            }

            var ramp = ReadCurveData();
            var ignorePhonemes = reader.ReadBoolean();

            var data = new ChoreoScene(events, actors, ramp, ignorePhonemes);
            return data;
        }

        protected virtual ChoreoActor ReadActor()
        {
            var name = ReadString();

            var channelsCount = reader.ReadByte();
            var channels = new ChoreoChannel[channelsCount];
            for (var i = 0; i < channelsCount; i++)
            {
                var channel = ReadChannel();
                channels[i] = channel;
            }

            var isActive = reader.ReadBoolean();

            return new ChoreoActor(name, channels, isActive);
        }

        protected virtual ChoreoChannel ReadChannel()
        {
            var name = ReadString();

            var eventsCount = reader.ReadByte();
            var events = new ChoreoEvent[eventsCount];
            for (var i = 0; i < eventsCount; i++)
            {
                var choreoEvent = ReadEvent();
                events[i] = choreoEvent;
            }

            var isActive = reader.ReadBoolean();

            return new ChoreoChannel(name, events, isActive);
        }

        protected virtual ChoreoEventRelativeTag ReadRelativeTag()
        {
            var name = ReadString();
            var value = reader.ReadByte() / 255f;
            return new ChoreoEventRelativeTag(name, value);
        }

        protected virtual ChoreoFlexTimingTag ReadFlexTimingTag()
        {
            var name = ReadString();
            var value = reader.ReadByte() / 255f;
            return new ChoreoFlexTimingTag(name, value);
        }

        protected virtual ChoreoEventAbsoluteTag ReadAbsoluteTag()
        {
            var name = ReadString();
            var value = reader.ReadInt16() / 4096f;
            return new ChoreoEventAbsoluteTag(name, value);
        }

        protected virtual ChoreoEvent ReadEvent()
        {
            var eventType = (ChoreoEventType)reader.ReadByte();
            var name = ReadString();

            var eventStart = reader.ReadSingle();
            var eventEnd = reader.ReadSingle();

            var param1 = ReadString();
            var param2 = ReadString();
            var param3 = ReadString();

            ChoreoCurveData ramp;
            ramp = ReadCurveData();

            if (version >= 16)
            {
                var unk02 = reader.ReadByte();
                var unk03 = reader.ReadByte();
            }
            var flags = (ChoreoFlags)reader.ReadByte();

            var distanceToTarget = reader.ReadSingle();

            //relative tags
            var count = reader.ReadByte();
            var relativeTags = new ChoreoEventRelativeTag[count];
            for (var i = 0; i < count; i++)
            {
                relativeTags[i] = ReadRelativeTag();
            }

            //flex timing tags
            count = reader.ReadByte();
            var flexTimingTags = new ChoreoFlexTimingTag[count];
            for (var i = 0; i < count; i++)
            {
                flexTimingTags[i] = ReadFlexTimingTag();
            }


            //absolute tags
            //play tags
            count = reader.ReadByte();
            var playTags = new ChoreoEventAbsoluteTag[count];
            for (var i = 0; i < count; i++)
            {
                playTags[i] = ReadAbsoluteTag();
            }

            //shift tags
            count = reader.ReadByte();
            var shiftTags = new ChoreoEventAbsoluteTag[count];
            for (var i = 0; i < count; i++)
            {
                shiftTags[i] = ReadAbsoluteTag();
            }

            var sequenceDuration = -1f;
            if (eventType == ChoreoEventType.Gesture)
            {
                sequenceDuration = reader.ReadSingle();
            }

            var usingRelativeTag = reader.ReadBoolean();
            ChoreoEventRelativeTag relativeTag = null;
            if (usingRelativeTag)
            {
                relativeTag = ReadRelativeTag();
            }

            var flex = ReadFlex();

            byte loopCount = 0;
            ChoreoClosedCaptions closedCaptions = null;
            if (eventType == ChoreoEventType.Loop)
            {
                loopCount = reader.ReadByte();
            }
            else if (eventType == ChoreoEventType.Speak)
            {
                if (version < 16)
                {
                    closedCaptions = ReadClosedCaptions();
                }
                var soundStartDelay = reader.ReadSingle();
                if (version >= 16)
                {
                    var unk03 = reader.ReadByte();
                    Debug.Assert(unk03 == 0);
                }
            }

            //eventId or unk01 is sometimes missing?
            var constrainedEventId = reader.ReadInt32();
            var eventId = reader.ReadInt32();

            var absoluteTags = playTags.Concat(shiftTags).ToArray();

            return new ChoreoEvent
            {
                Type = eventType,
                Name = name,
                StartTime = eventStart,
                EndTime = eventEnd,
                Param1 = param1,
                Param2 = param2,
                Param3 = param3,
                Ramp = ramp,
                Flags = flags,
                DistanceToTarget = distanceToTarget,
                RelativeTags = relativeTags,
                FlexTimingTags = flexTimingTags,
                AbsoluteTags = absoluteTags,
                SequenceDuration = sequenceDuration,
                UsingRelativeTag = usingRelativeTag,
                RelativeTag = relativeTag,
                EventFlex = flex,
                LoopCount = loopCount,
                ClosedCaptions = closedCaptions,
                ConstrainedEventId = constrainedEventId,
                Id = eventId,
            };
        }

        protected virtual ChoreoClosedCaptions ReadClosedCaptions()
        {
            var type = (ChoreoClosedCaptionsType)reader.ReadByte();
            var token = ReadString();
            var flags = (ChoreoClosedCaptionsFlags)reader.ReadByte();
            return new ChoreoClosedCaptions(type, token, flags);
        }

        protected virtual ChoreoCurveData ReadCurveData()
        {
            var sampleCount = reader.ReadUInt16();
            var samples = new ChoreoSample[sampleCount];

            var samplesRead = 0;
            ChoreoSample lastSample = null;
            var type = 0;
            while (type != 0 || samplesRead < sampleCount)
            {
                if (type == 0) //Sample
                {
                    lastSample = ReadSample();

                    var index = samplesRead++;
                    samples[index] = lastSample;
                }
                else if (type == 1) //Curve type of last sample
                {
                    var inType = reader.ReadByte();
                    var outType = reader.ReadByte();
                    var unk01 = reader.ReadByte(); //what's this
                    Debug.Assert(unk01 == 0); //Does this have to be 0?

                    lastSample.SetCurveType(inType, outType);
                }
                else
                {
                    throw new UnexpectedMagicException($"Unexpected choreo sample data type", type, nameof(type));
                }

                type = reader.ReadByte();
            }

            return new ChoreoCurveData(samples);
        }

        protected virtual ChoreoSample ReadSample()
        {
            var time = reader.ReadSingle();
            var value = reader.ReadByte() / 255f;
            var hasBezier = reader.ReadBoolean();

            var sample = new ChoreoSample(time, value);
            if (hasBezier)
            {
                var type = reader.ReadByte();
                Debug.Assert(type == 3); //This seems to be always 3 for bezier data
                var inDeg = reader.ReadSingle();
                var inWeight = reader.ReadSingle();
                var outDeg = reader.ReadSingle();
                var outWeight = reader.ReadSingle();

                sample.SetBezierData(inDeg, inWeight, outDeg, outWeight);
            }

            return sample;
        }

        protected virtual ChoreoEventFlex ReadFlex()
        {
            var tracksCount = reader.ReadByte();
            var tracks = new ChoreoFlexAnimationTrack[tracksCount];
            for (var i = 0; i < tracksCount; i++)
            {
                tracks[i] = ReadFlexTrack();
            }

            return new ChoreoEventFlex(tracks);
        }

        protected virtual ChoreoFlexAnimationTrack ReadFlexTrack()
        {
            var name = ReadString();
            var flags = (ChoreoTrackFlags)reader.ReadByte();
            var minRange = reader.ReadSingle();
            var maxRange = reader.ReadSingle();

            var samplesCurve = ReadCurveData();
            ChoreoCurveData comboSamplesCurve = null;
            if (flags.HasFlag(ChoreoTrackFlags.Combo) || version >= 16)
            {
                comboSamplesCurve = ReadCurveData();
            }

            return new ChoreoFlexAnimationTrack(name, flags, minRange, maxRange, samplesCurve, comboSamplesCurve);
        }
    }
}
