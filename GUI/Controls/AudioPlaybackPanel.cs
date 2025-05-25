using System.Globalization;
using System.Windows.Forms;
using GUI.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

#nullable disable

namespace GUI.Controls
{
    internal partial class AudioPlaybackPanel : UserControl
    {
        private WaveOutEvent waveOut;
        private WaveStream waveStream;
        private Action<float> setVolumeDelegate;

        public AudioPlaybackPanel(WaveStream inputStream)
        {
            Dock = DockStyle.Fill;

            InitializeComponent();

            waveStream = inputStream;
            labelTotalTime.Text = waveStream.TotalTime.ToString("mm\\:ss\\.ff", CultureInfo.InvariantCulture);
            volumeSlider1.Volume = Settings.Config.Volume;
        }

        private void OnButtonPlayClick(object sender, EventArgs e) => Play();

        public void Play()
        {
            if (waveOut == null)
            {
                try
                {
                    waveOut = new WaveOutEvent();
                    waveOut.PlaybackStopped += OnPlaybackStopped;
                    waveOut.Init(CreateInputStream());
                }
                catch (Exception driverCreateException)
                {
                    MessageBox.Show(driverCreateException.Message, "Failed to play audio");
                    return;
                }
            }

            if (waveOut.PlaybackState == PlaybackState.Playing)
            {
                return;
            }

            setVolumeDelegate(volumeSlider1.Volume);
            waveOut.Play();
            playbackTimer.Enabled = true;
            UpdateTime();
        }

        private MeteringSampleProvider CreateInputStream()
        {
            WaveStream stream = null;

            try
            {
                SampleChannel sampleChannel;

                if (waveStream.WaveFormat.Encoding == WaveFormatEncoding.Adpcm)
                {
                    stream = WaveFormatConversionStream.CreatePcmStream(waveStream);
                    sampleChannel = new SampleChannel(stream, true);
                }
                else
                {
                    sampleChannel = new SampleChannel(waveStream, true);
                }

                stream = null;
                sampleChannel.PreVolumeMeter += OnPreVolumeMeter;
                setVolumeDelegate = vol => sampleChannel.Volume = vol;
                var postVolumeMeter = new MeteringSampleProvider(sampleChannel);
                postVolumeMeter.StreamVolume += OnPostVolumeMeter;
                return postVolumeMeter;
            }
            finally
            {
                stream?.Dispose();
            }
        }

        void OnPreVolumeMeter(object sender, StreamVolumeEventArgs e)
        {
            waveformPainter1.AddMax(e.MaxSampleValues[0]);
            waveformPainter2.AddMax(e.MaxSampleValues[1]);
        }

        void OnPostVolumeMeter(object sender, StreamVolumeEventArgs e)
        {
            volumeMeter1.Amplitude = e.MaxSampleValues[0];
            volumeMeter2.Amplitude = e.MaxSampleValues[1];
        }

        void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                MessageBox.Show(e.Exception.Message, "Playback Device Error");
            }

            if (waveStream != null)
            {
                waveStream.Position = 0;
            }

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
                waveOut = null;
            }

            if (waveStream != null)
            {
                waveStream.Dispose();
                setVolumeDelegate = null;
                waveStream = null;
            }
        }

        private void OnButtonPauseClick(object sender, EventArgs e)
        {
            if (waveOut?.PlaybackState == PlaybackState.Playing)
            {
                waveOut.Pause();
            }

            playbackTimer.Enabled = false;
        }

        private void OnVolumeSliderChanged(object sender, EventArgs e)
        {
            setVolumeDelegate?.Invoke(volumeSlider1.Volume);

            Settings.Config.Volume = volumeSlider1.Volume;
        }

        private void OnButtonStopClick(object sender, EventArgs e)
        {
            waveOut?.Stop();
            waveStream.Position = 0;
            playbackTimer.Enabled = false;
            UpdateTime();
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
