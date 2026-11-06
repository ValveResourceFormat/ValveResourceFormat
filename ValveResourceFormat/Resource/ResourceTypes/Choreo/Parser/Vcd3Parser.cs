using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.Choreo.Curves;
using ValveResourceFormat.ResourceTypes.Choreo.Enums;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo.Parser
{
    /// <summary>
    /// Reads the KV3 form of a scene, stored in compiled <c>.vcd</c> resources.
    /// </summary>
    public static class Vcd3Parser
    {
        /// <summary>Reads a scene.</summary>
        /// <param name="root">The root of the KV3 data.</param>
        /// <returns>The scene, without a name.</returns>
        public static ChoreoScene Parse(KVObject root)
        {
            ArgumentNullException.ThrowIfNull(root);

            return new ChoreoScene(0, ReadEvents(root), ReadActors(root), ReadRamp(root, "scene_ramp"), root.GetBooleanProperty("ignorePhonemes"))
            {
                Duration = SecondsToMilliseconds(root.GetFloatProperty("_scene_stop_time_")),
                SoundDuration = SecondsToMilliseconds(root.GetFloatProperty("_last_speak_time_")),
                HasSounds = root.GetBooleanProperty("_has_speak_events_"),
            };
        }

        private static int SecondsToMilliseconds(float seconds) => (int)MathF.Round(seconds * 1000f);

        private static IReadOnlyList<KVObject> GetOptionalArray(KVObject parent, string name) => parent.GetArray(name) ?? [];

        private static ChoreoActor[] ReadActors(KVObject root)
        {
            var actors = GetOptionalArray(root, "actors");
            var result = new ChoreoActor[actors.Count];

            for (var i = 0; i < result.Length; i++)
            {
                var actor = actors[i];
                result[i] = new ChoreoActor(actor.GetStringProperty("name"), ReadChannels(actor), actor.GetBooleanProperty("active", true));
            }

            return result;
        }

        private static ChoreoChannel[] ReadChannels(KVObject actor)
        {
            var channels = GetOptionalArray(actor, "channels");
            var result = new ChoreoChannel[channels.Count];

            for (var i = 0; i < result.Length; i++)
            {
                var channel = channels[i];
                result[i] = new ChoreoChannel(channel.GetStringProperty("name"), ReadEvents(channel), channel.GetBooleanProperty("active", true));
            }

            return result;
        }

        private static ChoreoEvent[] ReadEvents(KVObject parent)
        {
            var events = GetOptionalArray(parent, "events");
            var result = new ChoreoEvent[events.Count];

            for (var i = 0; i < result.Length; i++)
            {
                result[i] = ReadEvent(events[i]);
            }

            return result;
        }

        private static ChoreoEvent ReadEvent(KVObject kv)
        {
            var type = ReadEventType(kv.GetStringProperty("type"));
            var startTime = kv.GetFloatProperty("start_time");
            var endTime = kv.GetFloatProperty("end_time", -1f);
            var param = kv.GetStringProperty("param", string.Empty);

            // A loop spans the time it jumps back to and the time it jumps at, unless a parameter already
            // holds where to jump back to. Scene lists use that form, which is what scenes are read into.
            if (type == ChoreoEventType.Loop && param.Length == 0)
            {
                param = startTime.ToString("F6", CultureInfo.InvariantCulture);
                startTime = endTime;
                endTime = -1f;
            }

            var loopCount = kv.GetInt32Property("loopcount");

            return new ChoreoEvent
            {
                Type = type,
                Name = kv.GetStringProperty("name", string.Empty),
                StartTime = startTime,
                EndTime = endTime,
                Param1 = param,
                Param2 = kv.GetStringProperty("param2", string.Empty),
                Param3 = kv.GetStringProperty("param3", string.Empty),
                Ramp = ReadRamp(kv, "event_ramp"),
                Flags = ReadFlags(kv),
                RelativeTags = [],
                FlexTimingTags = [],
                PlaybackTimeTags = ReadTags(kv, "playback_time"),
                ShiftedTimeTags = ReadTags(kv, "shifted_time"),
                SequenceDuration = kv.GetFloatProperty("sequenceduration"),
                EventFlex = new ChoreoEventFlex(ReadFlexTracks(kv)),
                LoopCount = loopCount < 0 ? byte.MaxValue : (byte)loopCount,
                SpeakFlags = ReadSpeakFlags(kv),
                SoundStartDelay = kv.GetFloatProperty("startdelay"),
                Id = kv.GetInt32Property("eventID"),
                ConstrainedEventId = kv.GetInt32Property("constrainedEventID"),
            };
        }

        // Scenes also hold types that only exist in this form and that nothing here plays
        private static ChoreoEventType ReadEventType(string type)
            => Enum.TryParse<ChoreoEventType>(type, ignoreCase: true, out var eventType) ? eventType : ChoreoEventType.Unspecified;

        private static ChoreoFlags ReadFlags(KVObject kv)
        {
            var flags = default(ChoreoFlags);

            if (kv.GetBooleanProperty("active", true))
            {
                flags |= ChoreoFlags.IsActive;
            }

            if (kv.GetBooleanProperty("fixedlength"))
            {
                flags |= ChoreoFlags.FixedLength;
            }

            if (kv.GetBooleanProperty("playoverscript"))
            {
                flags |= ChoreoFlags.PlayOverScript;
            }

            return flags;
        }

        private static ChoreoSpeakFlags ReadSpeakFlags(KVObject kv)
        {
            var flags = ChoreoSpeakFlags.None;

            if (kv.GetBooleanProperty("cc_noattenuate"))
            {
                flags |= ChoreoSpeakFlags.SuppressingCaptionAttenuation;
            }

            if (kv.GetBooleanProperty("hardstopspeakevent"))
            {
                flags |= ChoreoSpeakFlags.HardStopSpeakEvent;
            }

            if (kv.GetBooleanProperty("volumematcheseventramp"))
            {
                flags |= ChoreoSpeakFlags.VolumeMatchesEventRamp;
            }

            return flags;
        }

        private static ChoreoTag[] ReadTags(KVObject kv, string name)
        {
            var tags = GetOptionalArray(kv, name);
            var result = new ChoreoTag[tags.Count];

            for (var i = 0; i < result.Length; i++)
            {
                result[i] = new ChoreoTag(tags[i].GetStringProperty("name", string.Empty), tags[i].GetFloatProperty("fraction"));
            }

            return result;
        }

        private static ChoreoFlexAnimationTrack[] ReadFlexTracks(KVObject kv)
        {
            var result = new List<ChoreoFlexAnimationTrack>();

            foreach (var track in GetOptionalArray(kv, "flexanimations"))
            {
                var flags = default(ChoreoTrackFlags);

                if (!track.GetBooleanProperty("disabled"))
                {
                    flags |= ChoreoTrackFlags.Enabled;
                }

                var samples = ReadCurve(track, "samples");
                ChoreoCurveData? comboRamp = null;

                if (track.GetBooleanProperty("combo"))
                {
                    flags |= ChoreoTrackFlags.Combo;
                    comboRamp = ReadCurve(track, "stereo");
                }
                else if (samples.Samples.Length == 0)
                {
                    continue;
                }

                result.Add(new ChoreoFlexAnimationTrack(
                    track.GetStringProperty("name"),
                    flags,
                    track.GetFloatProperty("min"),
                    track.GetFloatProperty("max", 1f),
                    samples,
                    comboRamp));
            }

            return [.. result];
        }

        // Samples win over the curve that was fitted to them
        private static ChoreoCurveData ReadRamp(KVObject parent, string name)
        {
            if (!parent.ContainsKey(name) && parent.TryGetValue($"{name}_curve", out var curve))
            {
                return ReadPiecewiseCurve(curve);
            }

            return ReadCurve(parent, name);
        }

        private static ChoreoCurveData ReadCurve(KVObject parent, string samplesName)
        {
            var samples = GetOptionalArray(parent, samplesName);
            var result = new ChoreoSample[samples.Count];

            for (var i = 0; i < result.Length; i++)
            {
                var sample = samples[i];
                result[i] = new ChoreoSample(sample.GetFloatProperty("time"), sample.GetFloatProperty("value"));

                if (sample.TryGetValue("curvetype", out var curveType))
                {
                    var curve = CurveType.Parse((string)curveType);
                    result[i].SetCurveType(curve.InType, curve.OutType);
                }

                if (sample.TryGetValue("bezier", out var bezier))
                {
                    ReadBezier(result[i], bezier);
                }
            }

            return new ChoreoCurveData(result, ReadEdge(parent, "left_edge"), ReadEdge(parent, "right_edge"));
        }

        private static void ReadBezier(ChoreoSample sample, KVObject bezier)
        {
            var flags = BezierFlags.None;

            if (bezier.GetBooleanProperty("unified"))
            {
                flags |= BezierFlags.Unified;
            }

            if (bezier.GetBooleanProperty("unweighted"))
            {
                flags |= BezierFlags.Unweighted;
            }

            var handleIn = bezier.GetSubCollection("in");
            var handleOut = bezier.GetSubCollection("out");

            sample.SetBezierData(
                flags,
                handleIn.GetFloatProperty("deg"),
                handleIn.GetFloatProperty("weight"),
                handleOut.GetFloatProperty("deg"),
                handleOut.GetFloatProperty("weight"));
        }

        private static ChoreoEdge? ReadEdge(KVObject parent, string name)
        {
            if (!parent.TryGetValue(name, out var edge))
            {
                return null;
            }

            return new ChoreoEdge(CurveType.Parse(edge.GetStringProperty("type")), edge.GetFloatProperty("zero_value"));
        }

        // A cubic Hermite segment is the cubic Bezier with handles a third of the segment along the slopes.
        // The curve evaluates handles by the sine of their angle, so a right angle leaves the signed weight as the offset.
        private static ChoreoCurveData ReadPiecewiseCurve(KVObject curve)
        {
            const float HandleDegrees = 90f;

            var keys = curve.GetArray("m_spline");
            var result = new ChoreoSample[keys.Count];

            for (var i = 0; i < result.Length; i++)
            {
                var key = keys[i];
                var time = key.GetFloatProperty("x");
                var inWeight = 0f;
                var outWeight = 0f;

                if (i > 0)
                {
                    var length = time - keys[i - 1].GetFloatProperty("x");
                    inWeight = -key.GetFloatProperty("m_flSlopeIncoming") * length / 3f;
                }

                if (i + 1 < result.Length)
                {
                    var length = keys[i + 1].GetFloatProperty("x") - time;
                    outWeight = key.GetFloatProperty("m_flSlopeOutgoing") * length / 3f;
                }

                result[i] = new ChoreoSample(time, key.GetFloatProperty("y"));
                result[i].SetCurveType(CurveInterpolator.Bezier, CurveInterpolator.Bezier);
                result[i].SetBezierData(BezierFlags.None, HandleDegrees, inWeight, HandleDegrees, outWeight);
            }

            return new ChoreoCurveData(result, null, null);
        }
    }
}
