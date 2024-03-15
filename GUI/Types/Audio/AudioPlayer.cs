using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using NAudio.Gui;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLayer.NAudioSupport;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace GUI.Types.Audio
{
    internal class AudioPlayer
    {
        public WaveOutEvent WaveOut { get; private set; }
        public WaveStream WaveStream { get; private set; }
        public Action<float> SetVolumeDelegate { get; set; }
        public EventHandler<StreamVolumeEventArgs> PreVolumeMeter { get; set; }
        public EventHandler<StreamVolumeEventArgs> PostVolumeMeter { get; set; }
        public EventHandler<StoppedEventArgs> PlaybackStopped { get; set; }

        public AudioPlayer(Resource resource, TabPage tab = null)
        {
            var soundData = (Sound)resource.DataBlock;

            if (soundData == null)
            {
                return;
            }

            var stream = soundData.GetSoundStream();

            try
            {
                WaveStream = soundData.SoundType switch
                {
                    Sound.AudioFileType.WAV => new WaveFileReader(stream),
                    Sound.AudioFileType.MP3 => new Mp3FileReaderBase(stream, wf => new Mp3FrameDecompressor(wf)),
                    Sound.AudioFileType.AAC => new StreamMediaFoundationReader(stream),
                    _ => throw new UnexpectedMagicException("Dont know how to play", (int)soundData.SoundType, nameof(soundData.SoundType)),
                };
                if (tab != null)
                {
                    var audio = new AudioPlaybackPanel(this);
                    tab.Controls.Add(audio);
                }
            }
            catch (Exception e)
            {
                Log.Error(nameof(AudioPlayer), e.ToString());

                var msg = new Label
                {
                    Text = $"NAudio Exception: {e}",
                    Dock = DockStyle.Fill,
                };
                if (tab != null)
                {
                    tab.Controls.Add(msg);
                }
            }
            if (tab == null)
            {
                SetVolume(Settings.Config.Volume);
            }
        }

        private MeteringSampleProvider CreateInputStream()
        {
            var sampleChannel = new SampleChannel(WaveStream, true);
            sampleChannel.PreVolumeMeter += PreVolumeMeter;
            SetVolumeDelegate = vol => sampleChannel.Volume = vol;
            var postVolumeMeter = new MeteringSampleProvider(sampleChannel);
            postVolumeMeter.StreamVolume += PostVolumeMeter;

            return postVolumeMeter;
        }
        public void Play()
        {
            if (WaveOut == null)
            {
                try
                {
                    WaveOut = new WaveOutEvent();
                    WaveOut.PlaybackStopped += PlaybackStopped;
                    WaveOut.Init(CreateInputStream());
                }
                catch (Exception driverCreateException)
                {
                    MessageBox.Show(driverCreateException.Message, "Failed to play audio");
                    return;
                }
            }

            if (WaveOut.PlaybackState == PlaybackState.Playing)
            {
                return;
            }
            WaveOut.Play();

        }
        public void SetVolume(float volume)
        {
            SetVolumeDelegate?.Invoke(volume);
        }

        public void Close()
        {
            if (WaveOut != null)
            {
                WaveOut.Stop();
                WaveOut.Dispose();
                WaveOut = null;
            }

            if (WaveStream != null)
            {
                WaveStream.Dispose();
                SetVolumeDelegate = null;
                WaveStream = null;
            }
        }

        public void Pause()
        {
            if (WaveOut?.PlaybackState == PlaybackState.Playing)
            {
                WaveOut.Pause();
            }
        }

        public void Stop()
        {
            WaveOut?.Stop();
            WaveStream.Position = 0;
        }
    }
}
