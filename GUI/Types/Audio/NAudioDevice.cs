using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using ValveResourceFormat.Renderer.Audio;

namespace GUI.Types.Audio
{
    /// <summary>
    /// NAudio-backed <see cref="IAudioDevice"/> using event-driven shared mode WASAPI.
    /// <see cref="SubmitSamples"/> blocks while more than <see cref="MixAhead"/> of audio is queued,
    /// which paces the renderer's mixing thread and keeps latency low (the equivalent of snd_mixahead).
    /// </summary>
    internal sealed class NAudioDevice : IAudioDevice
    {
        private const int WasapiLatencyMs = 20;

        public int SampleRate { get; }
        public int Channels => 2;

        /// <summary>
        /// Maximum amount of mixed audio queued ahead of the device. Lower values reduce latency,
        /// higher values are safer against underruns.
        /// </summary>
        public TimeSpan MixAhead { get; set; } = TimeSpan.FromMilliseconds(25);

        private readonly WasapiOut output;
        private readonly BufferedWaveProvider buffer;
        private volatile bool disposed;

        public NAudioDevice()
        {
            // Use the device mix format's sample rate so WASAPI does not need to insert a resampler
            var sampleRate = 48000;

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                sampleRate = device.AudioClient.MixFormat.SampleRate;
            }
            catch (COMException)
            {
                // No default endpoint to probe (no audio hardware, headless session): fall back to a common rate, WASAPI will resample
            }

            SampleRate = sampleRate;

            var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
            buffer = new BufferedWaveProvider(format)
            {
                BufferDuration = TimeSpan.FromMilliseconds(500),
                ReadFully = true, // produce silence when empty instead of stopping playback
            };

            output = new WasapiOut(AudioClientShareMode.Shared, useEventSync: true, latency: WasapiLatencyMs);
            output.Init(buffer);
            output.Play();
            _ = Windows.Win32.PInvoke.timeBeginPeriod(1); // need this - SubmitSamples paces the mixing thread with Thread.Sleep(1)
        }

        public void SubmitSamples(ReadOnlySpan<float> samples)
        {
            var byteCount = samples.Length * sizeof(float);
            var bytes = ArrayPool<byte>.Shared.Rent(byteCount);

            try
            {
                MemoryMarshal.AsBytes(samples).CopyTo(bytes);

                // Wait until the queued audio drops below the mixahead window, keeping the mixer just-in-time
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
