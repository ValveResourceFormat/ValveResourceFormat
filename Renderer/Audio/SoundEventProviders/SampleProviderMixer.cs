using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.Renderer.Audio.SoundEventProviders
{
    internal class SampleProviderMixer : SampleProviderMulti
    {
        public SampleProviderMixer(IEnumerable<SampleProvider> sampleProviders, WaveFormat waveFormat) : base(sampleProviders, waveFormat)
        {
        }

        public SampleProviderMixer(WaveFormat waveFormat) : base(Array.Empty<SampleProvider>(), waveFormat)
        {
        }

        public override int Read(float[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            if (read < count)
            {
                for (var i = read; i < count; i++)
                {
                    buffer[offset + i] = 0f;
                }
            }
            return count;
        }
    }
}
