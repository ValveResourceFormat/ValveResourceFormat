using NLayer;

namespace ValveResourceFormat.Renderer.Audio.Decoders;

/// <summary>
/// A reusable <see cref="IMpegFrame"/> that walks MP3 frames directly over a byte buffer, replacing
/// NLayer's stream-based <c>MpegFile</c>/<c>MpegStreamReader</c> front end. Those allocate a reader,
/// a frame decoder (with its large layer-III tables) and per-frame objects on every file; this walker
/// and the <see cref="NLayer.MpegFrameDecoder"/> it feeds are both kept per decode thread and rebound
/// per file, so MP3 decoding allocates nothing at steady state. Bit-reading semantics mirror NLayer's
/// own <c>MpegFrame</c> (cursor starts after the 4-byte header plus 2 CRC bytes when present; CRC is
/// not validated).
/// </summary>
internal sealed class MpegFrameWalker : IMpegFrame
{
    // Rows: version 1, version 2/2.5. Columns: layer I, II, III. Values in kbps, index 0 = free (rejected).
    private static readonly int[][][] BitRateTable =
    [
        [
            [0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448],
            [0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384],
            [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320],
        ],
        [
            [0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256],
            [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160],
            [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160],
        ],
    ];

    private byte[] data = [];
    private int dataLength;
    private int frameOffset = -1;

    // Bit reader state (see Reset/ReadBits)
    private int readOffset;
    private ulong bitBucket;
    private int bitsRead;

    /// <summary>Rebinds the walker to a new file's bytes. Call <see cref="TryNextFrame"/> to find the first frame.</summary>
    public void SetData(byte[] buffer, int length)
    {
        data = buffer;
        dataLength = length;
        frameOffset = -1;
    }

    /// <summary>Advances to the next valid frame, scanning past junk (ID3 tags, damaged data). False at end of data.</summary>
    public bool TryNextFrame()
    {
        var from = frameOffset < 0 ? SkipId3(0) : frameOffset + FrameLength;
        return TrySeekFrame(from);
    }

    private int SkipId3(int position)
    {
        if (dataLength >= position + 10
            && data[position] == 'I' && data[position + 1] == 'D' && data[position + 2] == '3')
        {
            // Syncsafe 28-bit size, excluding the 10-byte tag header
            var size = (data[position + 6] << 21) | (data[position + 7] << 14) | (data[position + 8] << 7) | data[position + 9];
            return position + 10 + size;
        }

        return position;
    }

    private bool TrySeekFrame(int from)
    {
        for (var position = Math.Max(from, 0); position + 4 <= dataLength; position++)
        {
            if (data[position] != 0xFF || (data[position + 1] & 0xE0) != 0xE0)
            {
                continue;
            }

            var candidate = frameOffset;
            frameOffset = position;

            if (HeaderValid() && position + FrameLength <= dataLength)
            {
                return true;
            }

            frameOffset = candidate;
        }

        return false;
    }

    private bool HeaderValid()
    {
        var versionBits = (data[frameOffset + 1] >> 3) & 3;
        var layerBits = (data[frameOffset + 1] >> 1) & 3;
        var bitRateIndex = (data[frameOffset + 2] >> 4) & 0xF;
        var sampleRateIndex = (data[frameOffset + 2] >> 2) & 3;

        // Free-format (index 0) frames have no computable length and are not used by game content
        return versionBits != 1 && layerBits != 0 && bitRateIndex is > 0 and < 15 && sampleRateIndex != 3;
    }

    /// <inheritdoc/>
    public MpegVersion Version => ((data[frameOffset + 1] >> 3) & 3) switch
    {
        0 => MpegVersion.Version25,
        2 => MpegVersion.Version2,
        3 => MpegVersion.Version1,
        _ => MpegVersion.Unknown,
    };

    /// <inheritdoc/>
    public MpegLayer Layer => ((data[frameOffset + 1] >> 1) & 3) switch
    {
        1 => MpegLayer.LayerIII,
        2 => MpegLayer.LayerII,
        3 => MpegLayer.LayerI,
        _ => MpegLayer.Unknown,
    };

    /// <inheritdoc/>
    public bool HasCrc => (data[frameOffset + 1] & 1) == 0;

    /// <inheritdoc/>
    public int BitRateIndex => (data[frameOffset + 2] >> 4) & 0xF;

    /// <inheritdoc/>
    public int SampleRateIndex => (data[frameOffset + 2] >> 2) & 3;

    /// <inheritdoc/>
    public int BitRate => BitRateTable[(int)Version / 10 - 1][(int)Layer - 1][BitRateIndex] * 1000;

    /// <inheritdoc/>
    public int SampleRate
    {
        get
        {
            var rate = SampleRateIndex switch { 0 => 44100, 1 => 48000, 2 => 32000, _ => 0 };
            return Version switch
            {
                MpegVersion.Version2 => rate / 2,
                MpegVersion.Version25 => rate / 4,
                _ => rate,
            };
        }
    }

    private int Padding => (data[frameOffset + 2] >> 1) & 1;

    /// <inheritdoc/>
    public MpegChannelMode ChannelMode => (MpegChannelMode)((data[frameOffset + 3] >> 6) & 3);

    /// <inheritdoc/>
    public int ChannelModeExtension => (data[frameOffset + 3] >> 4) & 3;

    /// <inheritdoc/>
    public bool IsCopyrighted => ((data[frameOffset + 3] >> 3) & 1) == 1;

    /// <inheritdoc/>
    public bool IsCorrupted => false;

    /// <inheritdoc/>
    public int SampleCount
    {
        get
        {
            if (Layer == MpegLayer.LayerI)
            {
                return 384;
            }

            if (Layer == MpegLayer.LayerIII && Version > MpegVersion.Version1)
            {
                return 576;
            }

            return 1152;
        }
    }

    /// <inheritdoc/>
    public int FrameLength => Layer == MpegLayer.LayerI
        ? (12 * BitRate / SampleRate + Padding) * 4
        : SampleCount / 8 * BitRate / SampleRate + Padding;

    /// <summary>Gets the number of output channels the decoder produces for this frame.</summary>
    public int Channels => ChannelMode == MpegChannelMode.Mono ? 1 : 2;

    /// <inheritdoc/>
    public void Reset()
    {
        readOffset = frameOffset + 4 + (HasCrc ? 2 : 0);
        bitBucket = 0;
        bitsRead = 0;
    }

    /// <inheritdoc/>
    public int ReadBits(int bitCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bitCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitCount, 32);

        var frameEnd = frameOffset + FrameLength;

        while (bitsRead < bitCount)
        {
            if (readOffset >= frameEnd || readOffset >= dataLength)
            {
                throw new System.IO.EndOfStreamException();
            }

            bitBucket = (bitBucket << 8) | data[readOffset++];
            bitsRead += 8;
        }

        var result = (int)((bitBucket >> (bitsRead - bitCount)) & ((1UL << bitCount) - 1));
        bitsRead -= bitCount;
        return result;
    }

    /// <summary>
    /// Parses a Xing/Info VBR header in the current frame, with its optional LAME gapless extension:
    /// the total audio frame count and how many leading/trailing samples per channel are encoder
    /// filler to trim. The tag frame itself carries no audio and must be skipped when this returns true.
    /// </summary>
    public bool TryParseXing(out int frameCount, out int encoderDelay, out int encoderPadding)
    {
        frameCount = 0;
        encoderDelay = 0;
        encoderPadding = 0;

        if (Layer != MpegLayer.LayerIII)
        {
            return false;
        }

        var sideInfoSize = Version == MpegVersion.Version1
            ? ChannelMode == MpegChannelMode.Mono ? 17 : 32
            : ChannelMode == MpegChannelMode.Mono ? 9 : 17;

        var position = frameOffset + 4 + (HasCrc ? 2 : 0) + sideInfoSize;
        var frameEnd = Math.Min(frameOffset + FrameLength, dataLength);

        if (position + 8 > frameEnd)
        {
            return false;
        }

        var isXing = data[position] == 'X' && data[position + 1] == 'i' && data[position + 2] == 'n' && data[position + 3] == 'g';
        var isInfo = data[position] == 'I' && data[position + 1] == 'n' && data[position + 2] == 'f' && data[position + 3] == 'o';

        if (!isXing && !isInfo)
        {
            return false;
        }

        position += 4;
        var flags = (data[position] << 24) | (data[position + 1] << 16) | (data[position + 2] << 8) | data[position + 3];
        position += 4;

        if ((flags & 1) != 0) // frame count
        {
            if (position + 4 > frameEnd)
            {
                return false;
            }

            frameCount = (data[position] << 24) | (data[position + 1] << 16) | (data[position + 2] << 8) | data[position + 3];
            position += 4;
        }

        if ((flags & 2) != 0) // byte count
        {
            position += 4;
        }

        if ((flags & 4) != 0) // seek table
        {
            position += 100;
        }

        if ((flags & 8) != 0) // quality
        {
            position += 4;
        }

        // LAME extension: 9-byte encoder string, then 12 bytes of fields, then the delay/padding triplet
        var delayPosition = position + 9 + 12;
        if (delayPosition + 3 <= frameEnd)
        {
            encoderDelay = (data[delayPosition] << 4) | (data[delayPosition + 1] >> 4);
            encoderPadding = ((data[delayPosition + 1] & 0xF) << 8) | data[delayPosition + 2];
        }

        return frameCount > 0;
    }
}
