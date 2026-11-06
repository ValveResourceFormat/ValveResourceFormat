using System.Buffers.Binary;
using System.IO;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Source1.Audio;

/// <summary>
/// Converts the wave and MP3 files of Source 1 and GoldSrc games to sound resources, keeping the audio as stored.
/// </summary>
internal static class SoundConverter
{
    private const ushort PcmTag = 1;
    private const ushort AdpcmTag = 2;
    private const ushort ExtensibleTag = 0xFFFE;

    private const int RiffHeaderSize = 12;
    private const int ChunkHeaderSize = 8;
    private const int CuePointSize = 24;
    private const int AdpcmSamplesPerBlockOffset = 18;
    private const int MinimumAdpcmFormatSize = 22;

    // Kilobits per second of MPEG layer III, by the bitrate index of a frame header. Index 0 is free format.
    private static readonly int[] Mpeg1Layer3Bitrates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
    private static readonly int[] Mpeg2Layer3Bitrates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
    private static readonly int[] Mpeg1SampleRates = [44100, 48000, 32000];

    /// <summary>
    /// Converts a RIFF wave file of 8 or 16-bit PCM or Microsoft ADPCM. The first cue point makes the sound loop.
    /// </summary>
    /// <param name="file">The contents of the file, which the sound reads its audio from.</param>
    /// <returns>The sound resource.</returns>
    /// <exception cref="InvalidDataException">The file is not a wave file.</exception>
    /// <exception cref="NotSupportedException">The wave file has an encoding other than the supported ones.</exception>
    public static Resource ConvertWave(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Length < RiffHeaderSize || !file.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !file.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Not a RIFF wave file.");
        }

        var format = ReadOnlyMemory<byte>.Empty;
        var data = ReadOnlyMemory<byte>.Empty;
        var cue = ReadOnlyMemory<byte>.Empty;
        var position = RiffHeaderSize;

        while (position + ChunkHeaderSize <= file.Length)
        {
            var id = file.AsSpan(position, 4);
            var size = (long)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(position + 4));
            var bodyStart = position + ChunkHeaderSize;
            var bodyLength = (int)Math.Min(size, file.Length - bodyStart);
            var body = file.AsMemory(bodyStart, bodyLength);

            if (id.SequenceEqual("fmt "u8))
            {
                format = body;
            }
            else if (id.SequenceEqual("data"u8))
            {
                data = body;
            }
            else if (id.SequenceEqual("cue "u8))
            {
                cue = body;
            }

            // Chunks are padded to an even size
            position = (int)Math.Min(bodyStart + size + (size & 1), file.Length);
        }

        if (format.Length < 16)
        {
            throw new InvalidDataException("The wave file has no format chunk.");
        }

        var fmt = format.Span;
        var tag = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
        var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]);
        var blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(fmt[12..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);

        // The encoding of an extensible format is the first two bytes of its sub format
        if (tag == ExtensibleTag && fmt.Length >= 26)
        {
            tag = BinaryPrimitives.ReadUInt16LittleEndian(fmt[24..]);
        }

        if (channels is < 1 or > 2 || sampleRate == 0)
        {
            throw new InvalidDataException("The wave file has an invalid format chunk.");
        }

        Sound.AudioFormatV4 audioFormat;
        var header = ReadOnlyMemory<byte>.Empty;
        int blockSize;
        uint sampleCount;

        if (tag == PcmTag && bits is 8 or 16)
        {
            // Some files store a block alignment that does not match their format
            audioFormat = bits == 8 ? Sound.AudioFormatV4.PCM8 : Sound.AudioFormatV4.PCM16;
            blockSize = channels * bits / 8;
            sampleCount = (uint)(data.Length / blockSize);
        }
        else if (tag == AdpcmTag && format.Length >= MinimumAdpcmFormatSize && blockAlign > 0)
        {
            var samplesPerBlock = BinaryPrimitives.ReadUInt16LittleEndian(fmt[AdpcmSamplesPerBlockOffset..]);

            audioFormat = Sound.AudioFormatV4.ADPCM;
            header = format;
            blockSize = blockAlign;
            sampleCount = (uint)(data.Length / blockSize * samplesPerBlock);
        }
        else
        {
            throw new NotSupportedException($"Wave encoding {tag} with {bits} bits is not supported.");
        }

        var storedData = data[..(data.Length / blockSize * blockSize)];

        var loopStart = -1;
        var loopEnd = 0;

        if (cue.Length >= 4 + CuePointSize && BinaryPrimitives.ReadUInt32LittleEndian(cue.Span) > 0)
        {
            var cueSampleOffset = BinaryPrimitives.ReadUInt32LittleEndian(cue.Span[(4 + 20)..]);

            if (cueSampleOffset < sampleCount)
            {
                loopStart = (int)cueSampleOffset;
                loopEnd = (int)sampleCount;
            }
        }

        return Sound.Create(audioFormat, sampleRate, (byte)channels, sampleCount, storedData, header, loopStart, loopEnd);
    }

    /// <summary>
    /// Converts an MPEG layer III file. The sound plays once, as MP3 files carry no loop points.
    /// </summary>
    /// <param name="file">The contents of the file, which the sound reads its audio from.</param>
    /// <returns>The sound resource.</returns>
    /// <exception cref="InvalidDataException">The file has no MPEG layer III frame.</exception>
    public static Resource ConvertMp3(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var position = SkipId3Tag(file);
        var sampleRate = 0;
        var channels = 0;
        long sampleCount = 0;

        while (TryReadFrame(file, position, out var frame))
        {
            sampleRate = frame.SampleRate;
            channels = frame.Channels;
            sampleCount += frame.SampleCount;
            position += frame.Length;
        }

        if (sampleCount == 0)
        {
            throw new InvalidDataException("The file has no MPEG layer III frame.");
        }

        return Sound.Create(Sound.AudioFormatV4.MP3, (uint)sampleRate, (byte)channels, (uint)sampleCount, file);
    }

    private static int SkipId3Tag(byte[] file)
    {
        if (file.Length < 10 || !file.AsSpan(0, 3).SequenceEqual("ID3"u8))
        {
            return 0;
        }

        // The size is four bytes of seven bits and does not count the ten byte header
        var size = file[6] << 21 | file[7] << 14 | file[8] << 7 | file[9];

        return 10 + size;
    }

    private readonly record struct Mp3Frame(int SampleRate, int Channels, int SampleCount, int Length);

    private static bool TryReadFrame(byte[] file, int position, out Mp3Frame frame)
    {
        frame = default;

        if (position + 4 > file.Length)
        {
            return false;
        }

        var header = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(position));
        var syncBits = header >> 21;
        var versionBits = (int)(header >> 19 & 3);
        var layerBits = (int)(header >> 17 & 3);
        var bitrateIndex = (int)(header >> 12 & 15);
        var sampleRateIndex = (int)(header >> 10 & 3);
        var padding = (int)(header >> 9 & 1);
        var channelMode = (int)(header >> 6 & 3);

        const int Mpeg25 = 0;
        const int Reserved = 1;
        const int Mpeg1 = 3;
        const int Layer3 = 1;

        if (syncBits != 0x7FF || versionBits == Reserved || layerBits != Layer3 || bitrateIndex is 0 or 15 || sampleRateIndex == 3)
        {
            return false;
        }

        var isMpeg1 = versionBits == Mpeg1;
        var bitrate = (isMpeg1 ? Mpeg1Layer3Bitrates : Mpeg2Layer3Bitrates)[bitrateIndex] * 1000;
        var sampleRate = Mpeg1SampleRates[sampleRateIndex];

        if (!isMpeg1)
        {
            sampleRate /= 2;
        }

        if (versionBits == Mpeg25)
        {
            sampleRate /= 2;
        }

        var samplesPerFrame = isMpeg1 ? 1152 : 576;
        var length = samplesPerFrame / 8 * bitrate / sampleRate + padding;

        if (position + length > file.Length)
        {
            return false;
        }

        var channels = channelMode == 3 ? 1 : 2;
        frame = new Mp3Frame(sampleRate, channels, samplesPerFrame, length);

        return true;
    }
}
