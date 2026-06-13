using NAudio.Wave;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.Renderer.Audio.SoundEventProviders
{
    class SampleProviderMulti : SampleProvider
    {
        private LinkedList<SampleProvider> providers;
        private WaveFormat waveFormat;
        public override WaveFormat WaveFormat => waveFormat;

        public SampleProviderMulti(IEnumerable<SampleProvider> sampleProviders)
        {
            providers = new(sampleProviders);
            if (providers.Count > 0)
            {
                waveFormat = providers.First().WaveFormat;
            }
        }
        public SampleProviderMulti(IEnumerable<SampleProvider> sampleProviders, WaveFormat waveFormat)
        {
            providers = new(sampleProviders);
            this.waveFormat = waveFormat;
        }

        public void AddProvider(SampleProvider provider)
        {
            lock (providers)
            {
                providers.AddLast(provider);
            }
            if (waveFormat == null)
            {
                waveFormat = provider.WaveFormat;
            }
        }

        public void RemoveProvider(SampleProvider provider)
        {
            lock (provider)
            {
                providers.Remove(provider);
            }
        }

        public void ClearProviders()
        {
            lock (providers)
            {
                providers.Clear();
            }
        }

        public override int Read(float[] buffer, int offset, int count)
        {
            var maxRead = 0;
            lock (providers)
            {
                if (providers.Count == 0)
                {
                    return 0;
                }

                var readBuffer = ArrayPool<float>.Shared.Rent(count);

                try
                {
                    var node = providers.First;
                    while (node != null)
                    {
                        var next = node.Next;

                        var read = node.Value.Read(readBuffer, offset, count);
                        var index = offset;
                        for (var i = 0; i < read; i++)
                        {
                            if (i >= maxRead)
                            {
                                buffer[index++] = readBuffer[i];
                            }
                            else
                            {
                                buffer[index++] += readBuffer[i];
                            }
                        }

                        if (read < count && node.List == providers)
                        {
                            providers.Remove(node);
                        }

                        maxRead = Math.Max(maxRead, read);

                        node = next;
                    }
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(readBuffer);
                }
            }

            if (maxRead < count)
            {
                Over();
            }

            return maxRead;
        }
    }
}
