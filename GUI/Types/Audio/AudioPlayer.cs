using System;
using System.Drawing;
using System.Windows.Forms;
using NAudio.Wave;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Audio
{
    internal class AudioPlayer
    {
        private readonly WaveOutEvent waveOut;
        private readonly Button playButton;

        public AudioPlayer(Resource resource, TabPage tab)
        {
            var soundData = (Sound)resource.Blocks[BlockType.DATA];

            var stream = soundData.GetSoundStream();
            waveOut = new WaveOutEvent();

            if (soundData.Type == Sound.AudioFileType.WAV)
            {
                var rawSource = new WaveFileReader(stream);
                waveOut.Init(rawSource);
            }
            else if (soundData.Type == Sound.AudioFileType.MP3)
            {
                var rawSource = new Mp3FileReader(stream);
                waveOut.Init(rawSource);
            }
            else if (soundData.Type == Sound.AudioFileType.AAC)
            {
                var rawSource = new StreamMediaFoundationReader(stream);
                waveOut.Init(rawSource);
            }

            playButton = new Button();
            playButton.Text = "Play";
            playButton.TabIndex = 1;
            playButton.Size = new Size(100, 25);
            playButton.Click += PlayButton_Click;

            tab.Controls.Add(playButton);
        }

        private void PlayButton_Click(object sender, EventArgs e)
        {
            if (waveOut.PlaybackState == PlaybackState.Playing)
            {
                waveOut.Pause();
                playButton.Text = "Play";
            }
            else
            {
                waveOut.Play();
                playButton.Text = "Pause";
            }
        }
    }
}
