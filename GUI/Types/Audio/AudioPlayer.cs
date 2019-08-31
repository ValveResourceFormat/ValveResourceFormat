using System;
using System.Drawing;
using System.Windows.Forms;
using NAudio.Wave;
using NLayer.NAudioSupport;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Audio
{
    internal class AudioPlayer
    {
        private readonly Button playButton;
        private WaveOutEvent waveOut;
        private WaveStream waveStream;

        public AudioPlayer(Resource resource, TabPage tab)
        {
            var soundData = (Sound)resource.Blocks[BlockType.DATA];

            var stream = soundData.GetSoundStream();
            waveOut = new WaveOutEvent();
            waveOut.PlaybackStopped += WaveOut_PlaybackStopped;

            try
            {
                switch (soundData.Type)
                {
                    case Sound.AudioFileType.WAV: waveStream = new WaveFileReader(stream); break;
                    case Sound.AudioFileType.MP3: waveStream = new Mp3FileReader(stream, new Mp3FileReader.FrameDecompressorBuilder(wf => new Mp3FrameDecompressor(wf))); break;
                    case Sound.AudioFileType.AAC: waveStream = new StreamMediaFoundationReader(stream); break;
                    default: throw new Exception($"Dont know how to play {soundData.Type}");
                }

                waveOut.Init(waveStream);

                playButton = new Button();
                playButton.Text = "Play";
                playButton.TabIndex = 1;
                playButton.Size = new Size(100, 25);
                playButton.Click += PlayButton_Click;
                playButton.Disposed += PlayButton_Disposed;

                tab.Controls.Add(playButton);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);

                var msg = new Label
                {
                    Text = $"NAudio Exception: {e.Message}",
                    Dock = DockStyle.Fill,
                };

                tab.Controls.Add(msg);
            }
        }

        private void WaveOut_PlaybackStopped(object sender, StoppedEventArgs e)
        {
            playButton.Text = "Play";
        }

        private void PlayButton_Disposed(object sender, EventArgs e)
        {
            if (waveOut != null)
            {
                Console.WriteLine("Disposed sound");
                waveOut.Dispose();
                waveOut = null;
            }

            if (waveStream != null)
            {
                waveStream.Dispose();
                waveStream = null;
            }
        }

        private void PlayButton_Click(object sender, EventArgs e)
        {
            if (waveOut.PlaybackState == PlaybackState.Stopped)
            {
                waveStream.Position = 0;
            }

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
