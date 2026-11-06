using System.IO;
using System.Text;
using ValveResourceFormat.ResourceTypes.Choreo;
using ValveResourceFormat.ResourceTypes.Choreo.Enums;
using ValveResourceFormat.Source1.Model;

namespace ValveResourceFormat.Source1.Choreo;

/// <summary>
/// Reads the binary scenes (<c>bvcd</c>) that Source 1 compiles into <c>scenes.image</c>.
/// </summary>
internal sealed class Source1ChoreoReader(BinaryReader reader, Func<short, string> getString)
{
    private const int Tag = 0x64637662;
    private const byte SupportedVersion = 4;

    public static ChoreoScene Read(ReadOnlySpan<byte> data, Func<short, string> getString)
    {
        using var stream = new MemoryStream(data.ToArray());
#pragma warning disable CA2000 // Disposed by the using declaration, which the analyzer misses as the reader is handed on
        using var reader = new BinaryReader(stream, Encoding.ASCII);
#pragma warning restore CA2000

        return new Source1ChoreoReader(reader, getString).ReadScene();
    }

    private ChoreoScene ReadScene()
    {
        var tag = reader.ReadInt32();
        if (tag != Tag)
        {
            throw new UnexpectedMagicException("The data is not a binary scene", tag, nameof(tag));
        }

        var version = reader.ReadByte();
        if (version != SupportedVersion)
        {
            throw new UnexpectedMagicException("Unsupported binary scene version", version, nameof(version));
        }

        // Checksum of the text scene
        reader.ReadInt32();

        var eventCount = reader.ReadByte();
        var events = new ChoreoEvent[eventCount];
        for (var i = 0; i < eventCount; i++)
        {
            events[i] = ReadEvent();
        }

        var actorCount = reader.ReadByte();
        var actors = new ChoreoActor[actorCount];
        for (var i = 0; i < actorCount; i++)
        {
            actors[i] = ReadActor();
        }

        var ramp = ReadRamp();
        var ignorePhonemes = reader.ReadByte() != 0;

        return new ChoreoScene(version, events, actors, ramp, ignorePhonemes);
    }

    private string ReadString() => getString(reader.ReadInt16());

    private ChoreoActor ReadActor()
    {
        var name = ReadString();

        var channelCount = reader.ReadByte();
        var channels = new ChoreoChannel[channelCount];
        for (var i = 0; i < channelCount; i++)
        {
            channels[i] = ReadChannel();
        }

        var isActive = reader.ReadByte() == 1;

        return new ChoreoActor(name, channels, isActive);
    }

    private ChoreoChannel ReadChannel()
    {
        var name = ReadString();

        var eventCount = reader.ReadByte();
        var events = new ChoreoEvent[eventCount];
        for (var i = 0; i < eventCount; i++)
        {
            events[i] = ReadEvent();
        }

        var isActive = reader.ReadByte() == 1;

        return new ChoreoChannel(name, events, isActive);
    }

    private ChoreoEvent ReadEvent()
    {
        var type = (ChoreoEventType)reader.ReadByte();
        var name = ReadString();
        var startTime = reader.ReadSingle();
        var endTime = reader.ReadSingle();
        var param1 = ReadString();
        var param2 = ReadString();
        var param3 = ReadString();
        var ramp = ReadRamp();
        var flags = (ChoreoFlags)reader.ReadByte();
        var distanceToTarget = reader.ReadSingle();

        var relativeTags = ReadTags(absolute: false);
        var flexTimingTags = ReadTags(absolute: false);
        var playbackTimeTags = ReadTags(absolute: true);
        var shiftedTimeTags = ReadTags(absolute: true);

        var sequenceDuration = -1f;
        if (type == ChoreoEventType.Gesture)
        {
            sequenceDuration = reader.ReadSingle();
        }

        ChoreoEventRelativeTag? relativeTag = null;
        if (reader.ReadByte() == 1)
        {
            var tagName = ReadString();
            var soundName = ReadString();
            relativeTag = new ChoreoEventRelativeTag(tagName, soundName);
        }

        var flex = ReadFlex();

        byte loopCount = 0;
        var closedCaptionsType = ChoreoClosedCaptionsType.None;
        string? closedCaptionsToken = null;
        var speakFlags = ChoreoSpeakFlags.None;

        if (type == ChoreoEventType.Loop)
        {
            loopCount = reader.ReadByte();
        }
        else if (type == ChoreoEventType.Speak)
        {
            closedCaptionsType = (ChoreoClosedCaptionsType)reader.ReadByte();
            closedCaptionsToken = ReadString();
            speakFlags = (ChoreoSpeakFlags)reader.ReadByte();
        }

        // These name a sequence of the model, which a converted model names with the characters the model compiler accepts
        if (type is ChoreoEventType.Gesture or ChoreoEventType.Sequence)
        {
            name = ModelNames.Sanitize(name);
            param1 = ModelNames.Sanitize(param1);
        }

        return new ChoreoEvent
        {
            Type = type,
            Name = name,
            StartTime = startTime,
            EndTime = endTime,
            Param1 = param1,
            Param2 = param2,
            Param3 = param3,
            Ramp = ramp,
            Flags = flags,
            DistanceToTarget = distanceToTarget,
            RelativeTags = relativeTags,
            FlexTimingTags = flexTimingTags,
            PlaybackTimeTags = playbackTimeTags,
            ShiftedTimeTags = shiftedTimeTags,
            SequenceDuration = sequenceDuration,
            RelativeTag = relativeTag,
            EventFlex = flex,
            LoopCount = loopCount,
            ClosedCaptionsType = closedCaptionsType,
            ClosedCaptionsToken = closedCaptionsToken,
            SpeakFlags = speakFlags,
        };
    }

    // Relative and flex timing tags hold a fraction in 8 bits, absolute tags in 4.12 fixed point
    private ChoreoTag[] ReadTags(bool absolute)
    {
        var count = reader.ReadByte();
        var tags = new ChoreoTag[count];
        for (var i = 0; i < count; i++)
        {
            var name = ReadString();
            var fraction = absolute ? reader.ReadUInt16() / 4096f : reader.ReadByte() / 255f;
            tags[i] = new ChoreoTag(name, fraction);
        }

        return tags;
    }

    private ChoreoCurveData ReadRamp()
    {
        var count = reader.ReadByte();
        var samples = new ChoreoSample[count];
        for (var i = 0; i < count; i++)
        {
            var time = reader.ReadSingle();
            var value = reader.ReadByte() / 255f;
            samples[i] = new ChoreoSample(time, value);
        }

        return new ChoreoCurveData(samples, null, null);
    }

    private ChoreoEventFlex ReadFlex()
    {
        var trackCount = reader.ReadByte();
        var tracks = new ChoreoFlexAnimationTrack[trackCount];
        for (var i = 0; i < trackCount; i++)
        {
            tracks[i] = ReadFlexTrack();
        }

        return new ChoreoEventFlex(tracks);
    }

    private ChoreoFlexAnimationTrack ReadFlexTrack()
    {
        var name = ReadString();
        var flags = reader.ReadByte();
        var minRange = reader.ReadSingle();
        var maxRange = reader.ReadSingle();

        var trackFlags = default(ChoreoTrackFlags);
        if ((flags & 1) != 0)
        {
            trackFlags |= ChoreoTrackFlags.Enabled;
        }

        var isCombo = (flags & 2) != 0;
        if (isCombo)
        {
            trackFlags |= ChoreoTrackFlags.Combo;
        }

        var samples = ReadFlexSamples(reader.ReadInt16());
        ChoreoCurveData? comboSamples = null;
        if (isCombo)
        {
            comboSamples = ReadFlexSamples(reader.ReadUInt16());
        }

        return new ChoreoFlexAnimationTrack(name, trackFlags, minRange, maxRange, samples, comboSamples);
    }

    private ChoreoCurveData ReadFlexSamples(int count)
    {
        var samples = new ChoreoSample[count];
        for (var i = 0; i < count; i++)
        {
            var time = reader.ReadSingle();
            var value = reader.ReadByte() / 255f;
            var curveType = reader.ReadUInt16();

            var sample = new ChoreoSample(time, value);
            sample.SetCurveType((byte)(curveType >> 8), (byte)(curveType & 0xFF));
            samples[i] = sample;
        }

        return new ChoreoCurveData(samples, null, null);
    }
}
