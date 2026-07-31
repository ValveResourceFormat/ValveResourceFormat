using NLayer;

namespace ValveResourceFormat.Renderer.Audio.Decoders;

/// <summary>
/// MP3 decoder built on NLayer's frame decoder (fully managed, no platform audio dependencies), fed
/// by a <see cref="MpegFrameWalker"/> straight over the file bytes - no streams, no per-file reader or
/// decoder objects. The frame decoder (whose cost is its per-instance synthesis and reservoir buffers;
/// the layer-III lookup tables are static and shared either way) and the walker are kept
/// per decode thread, so at steady state an MP3 decode allocates nothing. Honors Xing/LAME gapless
/// info: leading encoder delay and trailing padding are trimmed like NLayer's own <c>MpegFile</c> does.
/// </summary>
internal static class Mp3Decoder
{
    [ThreadStatic]
    private static MpegFrameWalker? cachedWalker;

    [ThreadStatic]
    private static MpegFrameDecoder? cachedDecoder;

    public static void Prewarm()
    {
        cachedWalker ??= new MpegFrameWalker();
        cachedDecoder ??= new MpegFrameDecoder();
    }

    /// <summary>Decodes raw MP3 data, or returns false when no audio could be parsed.</summary>
    /// <param name="data">Buffer holding the MP3 file.</param>
    /// <param name="dataLength">Length of the MP3 file within <paramref name="data"/>.</param>
    /// <param name="sink">Receives the decoded samples.</param>
    /// <param name="truncated">Set when the decode hit damaged frames and may be missing samples.</param>
    public static bool Decode(byte[] data, int dataLength, IPcm16Sink sink, out bool truncated)
    {
        truncated = false;

        var walker = cachedWalker ??= new MpegFrameWalker();
        var decoder = cachedDecoder ??= new MpegFrameDecoder();
        decoder.Reset();
        walker.SetData(data, dataLength);

        if (!walker.TryNextFrame())
        {
            return false;
        }

        var sampleRate = walker.SampleRate;
        var channels = walker.Channels;

        // Gapless info: the Xing/Info tag frame carries the audio frame count and the encoder's
        // leading/trailing filler sample counts; the tag frame itself is not audio.
        long totalSamples = -1;
        long delaySamples = 0;

        if (walker.TryParseXing(out var frameCount, out var encoderDelay, out var encoderPadding))
        {
            totalSamples = (long)frameCount * walker.SampleCount * channels;
            delaySamples = (long)encoderDelay * channels;
            totalSamples -= (long)encoderPadding * channels;

            if (!walker.TryNextFrame())
            {
                return false;
            }
        }

        sink.SetFormat(channels, sampleRate, totalSamples > 0 ? totalSamples - delaySamples : -1);

        // 2304 floats covers the largest frame (1152 samples x 2 channels)
        var scratch = DecodeScratch.RentFloats(2304);
        var produced = 0L;
        var written = 0L;
        var failures = 0;

        while (true)
        {
            int decoded;

            try
            {
                decoded = decoder.DecodeFrame(walker, scratch, 0);
            }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or System.IO.InvalidDataException or System.IO.EndOfStreamException)
            {
                // Skip the damaged frame so one bad spot doesn't lose the rest of the file, capped
                // for data damaged badly enough that skipping never gets back in sync
                if (++failures >= 16)
                {
                    break;
                }

                decoder.Reset();

                if (!walker.TryNextFrame())
                {
                    break;
                }

                continue;
            }

            if (decoded > 0)
            {
                // Emit the slice of this frame inside the [delay, total) gapless window
                var start = Math.Max(produced, delaySamples);
                var end = totalSamples > 0 ? Math.Min(produced + decoded, totalSamples) : produced + decoded;

                if (end > start)
                {
                    sink.Write(scratch.AsSpan((int)(start - produced), (int)(end - start)));
                    written += end - start;
                }

                produced += decoded;

                if (totalSamples > 0 && produced >= totalSamples)
                {
                    break;
                }
            }

            if (!walker.TryNextFrame())
            {
                break;
            }
        }

        truncated = failures > 0;
        return written > 0;
    }
}
