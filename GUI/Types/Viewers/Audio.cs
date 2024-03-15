using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Audio;
using GUI.Utils;
using NAudio.Wave;
using NLayer.NAudioSupport;
using ValveResourceFormat;

namespace GUI.Types.Viewers
{
    class Audio : IViewer
    {
        public static bool IsAccepted(uint magic, string fileName)
        {
            return (magic == 0x46464952 /* RIFF */ && fileName.EndsWith(".wav", StringComparison.InvariantCultureIgnoreCase)) ||
                    fileName.EndsWith(".mp3", StringComparison.InvariantCultureIgnoreCase);
        }

        public TabPage Create(VrfGuiContext vrfGuiContext, Stream stream)
        {
            WaveStream waveStream;

            if (stream == null)
            {
                waveStream = new AudioFileReader(vrfGuiContext.FileName);
            }
            else if (vrfGuiContext.FileName.EndsWith(".mp3", StringComparison.InvariantCultureIgnoreCase))
            {
                waveStream = new Mp3FileReaderBase(stream, wf => new Mp3FrameDecompressor(wf));
            }
            else
            {
                waveStream = new WaveFileReader(stream);
            }

            var resource = new ValveResourceFormat.Resource
            {
                FileName = vrfGuiContext.FileName,
            };
            resource.Read(waveStream);
            var tab = new TabPage();
            var audio = new AudioPlayer(resource);
            var audioPanel = new AudioPlaybackPanel(audio);
            tab.Controls.Add(audioPanel);
            return tab;
        }
    }
}
