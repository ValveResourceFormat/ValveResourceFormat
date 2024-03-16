using System.Globalization;
using System.Windows.Forms;
using GUI.Types.Audio;
using GUI.Utils;
using NAudio.Gui;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GUI.Controls
{
    internal partial class AudioPlaybackPanel : UserControl
    {
        private AudioPlayer audioPlayer;
        public AudioPlaybackPanel(AudioPlayer ap)
        {
            Dock = DockStyle.Fill;
            InitializeComponent();

            audioPlayer = ap;
            audioPlayer.PlaybackStopped += OnPlaybackStopped;
            audioPlayer.PreVolumeMeter += OnPreVolumeMeter;
            audioPlayer.PostVolumeMeter += OnPostVolumeMeter;
            labelTotalTime.Text = audioPlayer.WaveStream.TotalTime.ToString("mm\\:ss\\.ff", CultureInfo.InvariantCulture);
            volumeSlider1.Volume = Settings.Config.Volume;
            Program.MainForm.StopAudioPlayerSearch();
            Program.MainForm.AudioPlayerCurrent?.Stop();
        }

        private void OnButtonPlayClick(object sender, EventArgs e)
        {
            if (audioPlayer.WaveOut?.PlaybackState == PlaybackState.Playing) return;

            if (Program.MainForm.AudioPlayerCurrent != audioPlayer)
            {
                Program.MainForm.AudioPlayerCurrent?.Stop();
                Program.MainForm.AudioPlayerCurrent = audioPlayer;
            }

            Program.MainForm.StopAudioPlayerSearch();
            audioPlayer.Play();
            audioPlayer.SetVolume(volumeSlider1.Volume);
            playbackTimer.Enabled = true;
            UpdateTime();
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

            if (audioPlayer.WaveStream != null)
            {
                audioPlayer.WaveStream.Position = 0;
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

            audioPlayer.Close();
        }

        private void OnButtonPauseClick(object sender, EventArgs e)
        {
            audioPlayer.Pause();

            playbackTimer.Enabled = false;
        }

        private void OnVolumeSliderChanged(object sender, EventArgs e)
        {
            audioPlayer.SetVolume(volumeSlider1.Volume);

            Settings.Config.Volume = volumeSlider1.Volume;
        }

        private void OnButtonStopClick(object sender, EventArgs e)
        {
            audioPlayer.Stop();
            playbackTimer.Enabled = false;
            UpdateTime();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            UpdateTime();
        }

        private void OnTrackBarPositionScroll(object sender, EventArgs e)
        {
            audioPlayer.WaveStream.CurrentTime = TimeSpan.FromSeconds(audioPlayer.WaveStream.TotalTime.TotalSeconds * trackBarPosition.Value / 100.0);
            UpdateTime();
        }

        private void UpdateTime()
        {
            var currentTime = audioPlayer.WaveStream.CurrentTime;
            trackBarPosition.Value = Math.Min(trackBarPosition.Maximum, (int)(100 * currentTime.TotalSeconds / audioPlayer.WaveStream.TotalTime.TotalSeconds));
            labelCurrentTime.Text = currentTime.ToString("mm\\:ss\\.ff", CultureInfo.InvariantCulture);
        }
    }
}
