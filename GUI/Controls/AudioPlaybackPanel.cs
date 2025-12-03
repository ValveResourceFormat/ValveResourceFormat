using System.Globalization;
using System.Windows.Forms;
using GUI.Types.Audio;
using GUI.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GUI.Controls
{
    internal partial class AudioPlaybackPanel : UserControl
    {
        private readonly WaveOutEvent waveOut = new();
        private readonly WaveStream waveStream;
        private readonly SampleChannel sampleChannel;
        private readonly bool AutoPlay;

        public AudioPlaybackPanel(WaveStream inputStream, bool autoPlay)
        {
            AutoPlay = autoPlay;
            Dock = DockStyle.Fill;

            InitializeComponent();

            waveStream = inputStream;
            labelTotalTime.Text = waveStream.TotalTime.ToString("mm\\:ss\\.ff", CultureInfo.InvariantCulture);
            volumeSlider1.Volume = Settings.Config.Volume;

            WaveStream? stream = null;

            try
            {
                if (waveStream.WaveFormat.Encoding == WaveFormatEncoding.Adpcm)
                {
                    stream = WaveFormatConversionStream.CreatePcmStream(waveStream);
                    sampleChannel = new SampleChannel(stream, true);
                }
                else
                {
                    sampleChannel = new SampleChannel(waveStream, true);
                }

                waveOut.PlaybackStopped += OnPlaybackStopped;
                waveOut.Init(sampleChannel);

                stream = null;
            }
            catch (Exception driverCreateException)
            {
                Program.ShowError(driverCreateException);
            }
            finally
            {
                stream?.Dispose();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            var waveFormRenderer = new WaveFormRenderer()
            {
                Width = waveFormPictureBox.Width,
                TopHeight = waveFormPictureBox.Height / 2,
                BottomHeight = waveFormPictureBox.Height / 2,
            };
            var image = waveFormRenderer.Render(waveStream);

            waveStream.Position = 0;

            waveFormPictureBox.Image = image;

            if (AutoPlay)
            {
                Play();
            }
        }

        private void OnPlayPauseButtonClick(object sender, EventArgs e)
        {
            if (waveOut.PlaybackState == PlaybackState.Playing)
            {
                waveOut.Pause();
                playbackTimer.Enabled = false;
                playPauseButton.Text = "Play";
            }
            else
            {
                Play();
            }
        }

        public void Play()
        {
            if (waveOut.PlaybackState == PlaybackState.Playing)
            {
                return;
            }

            sampleChannel.Volume = volumeSlider1.Volume;

            waveOut.Play();
            playbackTimer.Enabled = true;
            playPauseButton.Text = "Pause";
            UpdateTime();
        }

        void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Program.ShowError(e.Exception);
            }

            waveStream?.Position = 0;

            if (playbackTimer != null)
            {
                playbackTimer.Enabled = false;
                UpdateTime();
            }
        }

        private void CloseWaveOut()
        {
            if (playbackTimer != null)
            {
                playbackTimer.Enabled = false;
                playbackTimer.Dispose();
                playbackTimer = null;
            }

            if (waveOut != null)
            {
                waveOut.Stop();
                waveOut.Dispose();
            }

            waveStream?.Dispose();
        }

        private void OnVolumeSliderChanged(object sender, EventArgs e)
        {
            sampleChannel?.Volume = volumeSlider1.Volume;

            Settings.Config.Volume = volumeSlider1.Volume;
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            UpdateTime();
        }

        private void OnTrackBarPositionScroll(object sender, EventArgs e)
        {
            waveStream.CurrentTime = TimeSpan.FromSeconds(waveStream.TotalTime.TotalSeconds * trackBarPosition.Value / 100.0);
            UpdateTime();
        }

        private void UpdateTime()
        {
            var currentTime = waveStream.CurrentTime;
            trackBarPosition.Value = Math.Min(trackBarPosition.Maximum, (int)(100 * currentTime.TotalSeconds / waveStream.TotalTime.TotalSeconds));
            labelCurrentTime.Text = currentTime.ToString("mm\\:ss\\.ff", CultureInfo.InvariantCulture);
        }
    }
}
