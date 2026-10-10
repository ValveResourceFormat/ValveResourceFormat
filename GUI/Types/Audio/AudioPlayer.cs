using NAudio.Wave;
using NLayer.NAudioSupport;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Audio
{
    internal static class AudioPlayer
    {
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
                    Sound.AudioFileType.WAV => new WaveFileReader(stream),
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
