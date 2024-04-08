using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
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

        private TabPage tabPage;
        private Resource soundResource;

        public AudioPlayer(Resource resource, TabPage tab = null)
        {
            tabPage = tab;
            soundResource = resource;
            try
            {
                CreateWaveStream();
                if (tabPage != null)
                {
                    var audio = new AudioPlaybackPanel(this);
                    tabPage.Controls.Add(audio);
                }
            }
            catch (Exception e)
            {
                Log.Error(nameof(AudioPlayer), e.ToString());

                using var msg = new Label
                {
                    Text = $"NAudio Exception: {e}",
                    Dock = DockStyle.Fill,
                };
                tabPage?.Controls.Add(msg);
            }
        }

        private MeteringSampleProvider CreateInputStream()
        {
            if (WaveStream == null)
            {
                CreateWaveStream();
            }
            var sampleChannel = new SampleChannel(WaveStream, true);
            sampleChannel.PreVolumeMeter += PreVolumeMeter;
            SetVolumeDelegate = vol => sampleChannel.Volume = vol;
            var postVolumeMeter = new MeteringSampleProvider(sampleChannel);
            postVolumeMeter.StreamVolume += PostVolumeMeter;

            return postVolumeMeter;
        }

        private void CreateWaveStream()
        {
            var soundData = (Sound)soundResource.DataBlock;
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

            }
            catch
            {

            }
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

            if (tabPage == null)
            {
                SetVolume(Settings.Config.Volume);
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
            if (WaveStream != null)
            {
                WaveStream.Position = 0;
            }
        }

        public void TogglePlay()
        {
            if (WaveOut?.PlaybackState == PlaybackState.Playing)
            {
                Stop();
            }
            else
            {
                Play();
            }
        }
    }
}
