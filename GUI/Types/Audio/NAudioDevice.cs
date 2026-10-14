using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.Wave;
using ValveResourceFormat.Renderer.Audio;

namespace GUI.Types.Audio
{
    /// <summary>
    /// NAudio-backed <see cref="IAudioDevice"/> using event-driven shared mode WASAPI.
    /// </summary>
    internal sealed class NAudioDevice : IAudioDevice
    {
        private const int WasapiLatencyMs = 20;

        public int SampleRate { get; }
        public int Channels => 2;

        /// <summary>
        /// Maximum amount of mixed audio queued ahead of the device.
        /// </summary>
        public TimeSpan MixAhead { get; set; } = TimeSpan.FromMilliseconds(25);

        private readonly WasapiPlayer output;
        private readonly BufferedWaveProvider buffer;
        private volatile bool disposed;

        public NAudioDevice()
        {
            output = new WasapiPlayerBuilder()
                .WithSharedMode()
                .WithEventSync()
                .WithLatency(WasapiLatencyMs)
                .Build();

            // Use the device mix format's sample rate so WASAPI does not need to insert a resampler
            SampleRate = output.DeviceMixFormat.SampleRate;

            var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
            buffer = new BufferedWaveProvider(format, TimeSpan.FromMilliseconds(500))
            {
                ReadFully = true, // produce silence when empty instead of stopping playback
            };

            output.Init(buffer);
            output.Play();
            _ = Windows.Win32.PInvoke.timeBeginPeriod(1); // SubmitSamples paces the mixing thread with Thread.Sleep(1)
        }

        public void SubmitSamples(ReadOnlySpan<float> samples)
        {
            var byteCount = samples.Length * sizeof(float);
            var bytes = ArrayPool<byte>.Shared.Rent(byteCount);

            try
            {
                MemoryMarshal.AsBytes(samples).CopyTo(bytes);

                while (!disposed && buffer.BufferedDuration > MixAhead)
                {
                    Thread.Sleep(1);
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
            _ = Windows.Win32.PInvoke.timeEndPeriod(1);
        }
    }
}
