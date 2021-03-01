using System;
using System.Windows.Forms;
using GUI.Controls;
using NAudio.Wave;
using NLayer.NAudioSupport;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Audio
{
    internal class AudioPlayer
    {
        public AudioPlayer(Resource resource, TabPage tab)
        {
            var soundData = (Sound)resource.DataBlock;
            var stream = soundData.GetSoundStream();

            try
            {
                WaveStream waveStream;

                switch (soundData.SoundType)
                {
                    case Sound.AudioFileType.WAV: waveStream = new WaveFileReader(stream); break;
                    case Sound.AudioFileType.MP3: waveStream = new Mp3FileReaderBase(stream, wf => new Mp3FrameDecompressor(wf)); break;
                    case Sound.AudioFileType.AAC: waveStream = new StreamMediaFoundationReader(stream); break;
                    default: throw new Exception($"Dont know how to play {soundData.SoundType}");
                }

                var audio = new AudioPlaybackPanel(waveStream);

                tab.Controls.Add(audio);
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
    }
}
