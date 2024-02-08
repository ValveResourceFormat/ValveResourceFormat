using System.Diagnostics;
using System.IO;
using System.Linq;
using ValveResourceFormat.ResourceTypes.Choreo.Enums;
using ValveResourceFormat.ResourceTypes.Choreo.Curves;
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

            ChoreoEdge leftEdge = null;
            ChoreoEdge rightEdge = null;
            if (version >= 16)
            {
                leftEdge = ReadEdge();
                rightEdge = ReadEdge();
            }

            var ignorePhonemes = reader.ReadBoolean();

            var data = new ChoreoScene(version, events, actors, ramp, leftEdge, rightEdge, ignorePhonemes);
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

        protected virtual ChoreoTag ReadTag()
        {
            var name = ReadString();
            var value = reader.ReadByte() / 255f;
            return new ChoreoTag(name, value);
        }

        protected virtual ChoreoEventRelativeTag ReadRelativeTag()
        {
            var name = ReadString();
            var soundName = ReadString();
            return new ChoreoEventRelativeTag(name, soundName);
        }

        protected virtual ChoreoTag ReadAbsoluteTag()
        {
            var name = ReadString();
            var value = reader.ReadInt16() / 4096f;
            return new ChoreoTag(name, value);
        }

        protected virtual ChoreoEdge ReadEdge()
        {
            var hasEdge = reader.ReadBoolean();
            if (!hasEdge)
            {
                return null;
            }
            var toCurve = reader.ReadByte();
            var fromCurve = reader.ReadByte();
            var curve = new CurveType
            {
                InType = fromCurve,
                OutType = toCurve
            };

            //TODO: There's two more bytes here, but only curve type and zero value can be set from faceposer. Is there something else here for newer versions?
            reader.ReadBytes(2);

            var zeroValue = reader.ReadSingle();
            return new ChoreoEdge(curve, zeroValue);
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

            //TODO: Should these be a part of ChoreoCurveData?
            ChoreoEdge leftEdge = null;
            ChoreoEdge rightEdge = null;
            if (version >= 16)
            {
                leftEdge = ReadEdge();
                rightEdge = ReadEdge();
            }
            var flags = (ChoreoFlags)reader.ReadByte();

            var distanceToTarget = reader.ReadSingle();

            //relative tags
            var count = reader.ReadByte();
            var relativeTags = new ChoreoTag[count];
            for (var i = 0; i < count; i++)
            {
                relativeTags[i] = ReadTag();
            }

            //flex timing tags
            count = reader.ReadByte();
            var flexTimingTags = new ChoreoTag[count];
            for (var i = 0; i < count; i++)
            {
                flexTimingTags[i] = ReadTag();
            }


            //absolute tags
            //play tags
            count = reader.ReadByte();
            var playTags = new ChoreoTag[count];
            for (var i = 0; i < count; i++)
            {
                playTags[i] = ReadAbsoluteTag();
            }

            //shift tags
            count = reader.ReadByte();
            var shiftTags = new ChoreoTag[count];
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
            var soundStartDelay = 0f;
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
                soundStartDelay = reader.ReadSingle();
                if (version >= 16)
                {
                    var unk03 = reader.ReadByte();
                    Debug.Assert(unk03 == 0);
                }
            }

            //eventId or unk01 is sometimes missing?
            var constrainedEventId = reader.ReadInt32();
            var eventId = reader.ReadInt32();

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
                LeftEdge = leftEdge,
                RightEdge = rightEdge,
                Flags = flags,
                DistanceToTarget = distanceToTarget,
                RelativeTags = relativeTags,
                FlexTimingTags = flexTimingTags,
                PlaybackTimeTags = playTags,
                ShiftedTimeTags = shiftTags,
                SequenceDuration = sequenceDuration,
                UsingRelativeTag = usingRelativeTag,
                RelativeTag = relativeTag,
                EventFlex = flex,
                LoopCount = loopCount,
                ClosedCaptions = closedCaptions,
                SoundStartDelay = soundStartDelay,
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
                    var outType = reader.ReadByte();
                    var inType = reader.ReadByte();
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
