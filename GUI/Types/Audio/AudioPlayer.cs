using System.IO;
using System.Runtime.InteropServices;
using NAudio.Wave;
using NLayer.NAudioSupport;
using ValveResourceFormat;
using ValveResourceFormat.Renderer.Audio.Decoders;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Audio
{
    internal static class AudioPlayer
    {
        /// <summary>
        /// Returns <paramref name="reader"/>, or for MS ADPCM, which NAudio cannot decode without ACM, a stream of
        /// its decoded float samples. In that case <paramref name="reader"/> is disposed.
        /// </summary>
        public static WaveStream CreateWavStream(WaveFileReader reader)
        {
            var format = reader.WaveFormat;

            if (format.Encoding != WaveFormatEncoding.Adpcm)
            {
                return reader;
            }

            var data = new byte[reader.Length];
            reader.ReadExactly(data);
            reader.Dispose();

            var sink = new FloatSampleSink();

            if (!PcmDecoder.DecodeAdpcm(data, format.Channels, format.BlockAlign, format.SampleRate, sink))
            {
                throw new NotSupportedException($"Unsupported ADPCM layout: {format.Channels} channels, block align {format.BlockAlign}.");
            }

            var floatFormat = WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels);
            return new RawSourceWaveStream(sink.ToStream(), floatFormat);
        }

        private sealed class FloatSampleSink : IPcm16Sink
        {
            private byte[] samples = [];
            private int length;

            public void SetFormat(int channels, int sampleRate, long estimatedSourceSamples)
            {
                samples = new byte[estimatedSourceSamples * sizeof(float)];
            }

            public void Write(ReadOnlySpan<float> source)
            {
                var bytes = MemoryMarshal.AsBytes(source);
                bytes.CopyTo(samples.AsSpan(length));
                length += bytes.Length;
            }

            public MemoryStream ToStream() => new(samples, 0, length, writable: false);
        }

        /// <summary>
        /// Creates a decoded <see cref="WaveStream"/> and loop markers for streaming sound data, or null
        /// if there is none. The caller owns the returned stream.
        /// </summary>
        public static (WaveStream Stream, (int Start, int End) LoopMarkers)? CreateWaveStream(Resource resource)
        {
            var soundData = (Sound?)resource.DataBlock;

            if (soundData == null || soundData.StreamingDataSize == 0)
            {
                return null;
            }

            var stream = soundData.GetSoundStream();

            try
            {
                WaveStream waveStream = soundData.SoundType switch
                {
                    Sound.AudioFileType.WAV => CreateWavStream(new WaveFileReader(stream)),
                    Sound.AudioFileType.MP3 => new Mp3FileReaderBase(stream, wf => new Mp3FrameDecompressor(wf)),
                    Sound.AudioFileType.AAC => new StreamMediaFoundationReader(stream),
                    _ => throw new UnexpectedMagicException("Don't know how to play", (int)soundData.SoundType, nameof(soundData.SoundType)),
                };

                return (waveStream, (soundData.LoopStart, soundData.LoopEnd));
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
    }
}
