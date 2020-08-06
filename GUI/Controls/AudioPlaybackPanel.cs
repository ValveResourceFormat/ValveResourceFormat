using System;
using System.Windows.Forms;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GUI.Controls
{
    internal partial class AudioPlaybackPanel : UserControl
    {
        private IWavePlayer waveOut;
        public WaveStream WaveStream { get; set; }
        private Action<float> setVolumeDelegate;

        public AudioPlaybackPanel()
        {
            InitializeComponent();

            try
            {
                waveOut = new WaveOutEvent();
                waveOut.PlaybackStopped += OnPlaybackStopped;
            }
            catch (Exception driverCreateException)
            {
                MessageBox.Show($"{driverCreateException.Message}");
                return;
            }
        }

        private void OnButtonPlayClick(object sender, EventArgs e)
        {
            if (waveOut != null)
            {
                if (waveOut.PlaybackState == PlaybackState.Playing)
                {
                    return;
                }
                else if (waveOut.PlaybackState == PlaybackState.Paused)
                {
                    waveOut.Play();
                    return;
                }
            }

            ISampleProvider sampleProvider;
            try
            {
                sampleProvider = CreateInputStream();
            }
            catch (Exception createException)
            {
                MessageBox.Show($"{createException.Message}", "Error Loading File");
                return;
            }

            labelTotalTime.Text = $"{(int)WaveStream.TotalTime.TotalMinutes:00}:{WaveStream.TotalTime.Seconds:00}";

            try
            {
                waveOut.Init(sampleProvider);
            }
            catch (Exception initException)
            {
                MessageBox.Show($"{initException.Message}", "Error Initializing Output");
                return;
            }

            setVolumeDelegate(volumeSlider1.Volume);
            waveOut.Play();
        }

        private ISampleProvider CreateInputStream()
        {
            var sampleChannel = new SampleChannel(WaveStream, true);
            sampleChannel.PreVolumeMeter += OnPreVolumeMeter;
            setVolumeDelegate = vol => sampleChannel.Volume = vol;
            var postVolumeMeter = new MeteringSampleProvider(sampleChannel);
            postVolumeMeter.StreamVolume += OnPostVolumeMeter;

            return postVolumeMeter;
        }

        void OnPreVolumeMeter(object sender, StreamVolumeEventArgs e)
        {
            // we know it is stereo
            waveformPainter1.AddMax(e.MaxSampleValues[0]);
            waveformPainter2.AddMax(e.MaxSampleValues[1]);
        }

        void OnPostVolumeMeter(object sender, StreamVolumeEventArgs e)
        {
            // we know it is stereo
            volumeMeter1.Amplitude = e.MaxSampleValues[0];
            volumeMeter2.Amplitude = e.MaxSampleValues[1];
        }

        void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                MessageBox.Show(e.Exception.Message, "Playback Device Error");
            }

            if (WaveStream != null)
            {
                WaveStream.Position = 0;
            }
        }

        private void CloseWaveOut()
        {
            waveOut?.Stop();

            if (WaveStream != null)
            {
                WaveStream.Dispose();
                setVolumeDelegate = null;
                WaveStream = null;
            }

            if (waveOut != null)
            {
                waveOut.Dispose();
                waveOut = null;
            }
        }

        private void OnButtonPauseClick(object sender, EventArgs e)
        {
            if (waveOut?.PlaybackState == PlaybackState.Playing)
            {
                waveOut.Pause();
            }
        }

        private void OnVolumeSliderChanged(object sender, EventArgs e)
        {
            setVolumeDelegate?.Invoke(volumeSlider1.Volume);
        }

        private void OnButtonStopClick(object sender, EventArgs e) => waveOut?.Stop();

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (waveOut != null && WaveStream != null)
            {
                var currentTime = waveOut.PlaybackState == PlaybackState.Stopped ? TimeSpan.Zero : WaveStream.CurrentTime;
                trackBarPosition.Value = Math.Min(trackBarPosition.Maximum, (int)(100 * currentTime.TotalSeconds / WaveStream.TotalTime.TotalSeconds));
                labelCurrentTime.Text = $"{(int)currentTime.TotalMinutes:00}:{currentTime.Seconds:00}";
            }
            else
            {
                trackBarPosition.Value = 0;
            }
        }

        private void trackBarPosition_Scroll(object sender, EventArgs e)
        {
            if (waveOut != null)
            {
                WaveStream.CurrentTime = TimeSpan.FromSeconds(WaveStream.TotalTime.TotalSeconds * trackBarPosition.Value / 100.0);
            }
        }
    }
}
