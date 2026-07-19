using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.Wave;
using ValveResourceFormat.Renderer.Audio;

namespace GUI.Types.Audio
{
    /// <summary>
    /// NAudio-backed <see cref="IAudioDevice"/>. Buffers submitted samples and plays them through the default output device.
    /// <see cref="SubmitSamples"/> blocks while the buffer is full, which paces the renderer's mixing thread.
    /// </summary>
    internal sealed class NAudioDevice : IAudioDevice
    {
        public int SampleRate => 44100;
        public int Channels => 2;

        private readonly WaveOutEvent output;
        private readonly BufferedWaveProvider buffer;
        private volatile bool disposed;

        public NAudioDevice()
        {
            var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
            buffer = new BufferedWaveProvider(format)
            {
                BufferDuration = TimeSpan.FromMilliseconds(500),
                ReadFully = true,
            };

            output = new WaveOutEvent
            {
                DesiredLatency = 120,
            };
            output.Init(buffer);
            output.Play();
        }

        public void SubmitSamples(ReadOnlySpan<float> samples)
        {
            var byteCount = samples.Length * sizeof(float);
            var bytes = ArrayPool<byte>.Shared.Rent(byteCount);

            try
            {
                MemoryMarshal.AsBytes(samples).CopyTo(bytes);

                while (!disposed && buffer.BufferedBytes + byteCount > buffer.BufferLength)
                {
                    Thread.Sleep(10);
                }

                if (!disposed)
                {
                    buffer.AddSamples(bytes, 0, byteCount);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }
        }

        public void Dispose()
        {
            disposed = true;
            output.Dispose();
        }
    }
}
