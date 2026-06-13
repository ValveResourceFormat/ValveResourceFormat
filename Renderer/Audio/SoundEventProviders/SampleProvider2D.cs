using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.Renderer.Audio.SoundEventProviders
{
    internal class SampleProvider2D : SampleProvider
    {
        protected ISampleProvider Provider { get; init; }
        public SampleProvider2D(ISampleProvider provider)
        {
            Provider = provider;
        }

        public override WaveFormat WaveFormat => Provider.WaveFormat;

        public override int Read(float[] buffer, int offset, int count)
        {
            var read = Provider.Read(buffer, offset, count);

            if (Volume != 1f)
            {
                for (var i = 0; i < read; i++)
                {
                    buffer[i] *= VolumeMultiplier;
                }
            }

            if (read < count)
            {
                Over();
            }

            return read;
        }
    }
}
