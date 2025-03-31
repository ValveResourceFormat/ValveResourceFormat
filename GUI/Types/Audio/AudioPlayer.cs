using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using NAudio.Wave;
using NLayer.NAudioSupport;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Audio
{
    internal class AudioPlayer
    {
        public AudioPlayer(Resource resource, TabPage tab, bool autoPlay)
        {
            var soundData = (Sound)resource.DataBlock;

            if (soundData == null || soundData.StreamingDataSize == 0)
            {
                return;
            }

            var stream = soundData.GetSoundStream();

            try
            {
                WaveStream waveStream = soundData.SoundType switch
                {
                    Sound.AudioFileType.WAV => new WaveFileReader(stream),
                    Sound.AudioFileType.MP3 => new Mp3FileReaderBase(stream, wf => new Mp3FrameDecompressor(wf)),
                    Sound.AudioFileType.AAC => new StreamMediaFoundationReader(stream),
                    _ => throw new UnexpectedMagicException("Dont know how to play", (int)soundData.SoundType, nameof(soundData.SoundType)),
                };
                var audio = new AudioPlaybackPanel(waveStream);

                tab.Controls.Add(audio);

                if (autoPlay)
                {
                    audio.HandleCreated += OnHandleCreated;
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

                tab.Controls.Add(msg);
            }
        }

        private void OnHandleCreated(object sender, EventArgs e)
        {
            var audio = (AudioPlaybackPanel)sender;
            audio.HandleCreated -= OnHandleCreated;
            audio.Invoke(audio.Play);
        }
    }
}
