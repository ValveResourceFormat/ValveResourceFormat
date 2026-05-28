using GUI.Utils;
using NAudio.Wave;
using NLayer.NAudioSupport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace GUI.Types.Audio
{
    class SoundCache
    {
        struct SoundCacheData
        {
            public byte[] Data;
            public WaveFormat WaveFormat;
            public int LoopStart;
            public int LoopEnd;
            public uint Samples;
        }
        VrfGuiContext context;
        Dictionary<string, SoundCacheData> sounds = [];
        public SoundCache(VrfGuiContext context)
        {
            this.context = context;
        }

        public WaveStream GetSoundStream(string fileName)
        {
            if (!sounds.TryGetValue(fileName, out var soundData))
            {
                soundData.Data = LoadSound(fileName, out soundData.WaveFormat, out var sound);
                if (sound != null)
                {
                    soundData.LoopStart = sound.LoopStart;
                    soundData.LoopEnd = sound.LoopEnd;
                    soundData.Samples = sound.SampleCount;
                }
                sounds.Add(fileName, soundData);
            }
            if (soundData.Data == null)
            {
                return null;
            }

            WaveStream stream = new RawSourceWaveStream(new MemoryStream(soundData.Data), soundData.WaveFormat);
            if (soundData.LoopStart != -1)
            {
                float loopMultiplier = stream.Length / (float)soundData.Samples;
                int loopStart = (int)(soundData.LoopStart * loopMultiplier);
                int loopEnd = soundData.LoopEnd == 0 ? (int)stream.Length - 1 : (int)(soundData.LoopEnd * loopMultiplier);
                return new LoopWaveStream(stream, loopStart, loopEnd);
            }

            return stream;
        }

        private byte[] LoadSound(string fileName, out WaveFormat format, out Sound soundData)
        {
            format = null;
            Resource resource = context.LoadFileCompiled(fileName);

            if (resource == null)
            {
                soundData = null;
                return null;
            }

            soundData = (Sound)resource.DataBlock;

            if (soundData == null)
            {
                return null;
            }

            var stream = soundData.GetSoundStream();

            try
            {
                using WaveStream waveStream = soundData.SoundType switch
                {
                    Sound.AudioFileType.WAV => new WaveFileReader(stream),
                    Sound.AudioFileType.MP3 => new Mp3FileReaderBase(stream, wf => new Mp3FrameDecompressor(wf)),
                    Sound.AudioFileType.AAC => new StreamMediaFoundationReader(stream),
                    _ => throw new UnexpectedMagicException("Dont know how to play", (int)soundData.SoundType, nameof(soundData.SoundType)),
                };
                format = waveStream.WaveFormat;
                using var memoryStream = new MemoryStream();
                waveStream.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
            catch (Exception e)
            {
                Log.Error(nameof(SoundCache), e.ToString());
                return null;
            }
        }
    }
}
