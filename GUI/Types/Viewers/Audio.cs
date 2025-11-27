using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using NAudio.Wave;
using NLayer.NAudioSupport;

namespace GUI.Types.Viewers
{
    class Audio(VrfGuiContext vrfGuiContext, bool isPreview) : IViewer, IDisposable
    {
        private WaveStream? waveStream;

        public static bool IsAccepted(uint magic, string fileName)
        {
            return (magic == 0x46464952 /* RIFF */ && fileName.EndsWith(".wav", StringComparison.InvariantCultureIgnoreCase)) ||
                    fileName.EndsWith(".mp3", StringComparison.InvariantCultureIgnoreCase);
        }

        public async Task LoadAsync(Stream stream)
        {
            if (stream == null)
            {
                waveStream = new AudioFileReader(vrfGuiContext.FileName);
            }
            else if (vrfGuiContext.FileName!.EndsWith(".mp3", StringComparison.InvariantCultureIgnoreCase))
            {
                waveStream = new Mp3FileReaderBase(stream, wf => new Mp3FrameDecompressor(wf));
            }
            else
            {
                waveStream = new WaveFileReader(stream);
            }
        }

        public void Create(TabPage tab)
        {
            Debug.Assert(waveStream is not null);

            var tab = new TabPage();
            var audio = new AudioPlaybackPanel(waveStream);
            tab.Controls.Add(audio);

            var autoPlay = ((Settings.QuickPreviewFlags)Settings.Config.QuickFilePreview & Settings.QuickPreviewFlags.AutoPlaySounds) != 0;
            if (isPreview && autoPlay)
            {
                audio.HandleCreated += OnHandleCreated;
            }

            waveStream = null;

            return;
        }

        private void OnHandleCreated(object? sender, EventArgs e)
        {
            if (sender is AudioPlaybackPanel audio)
            {
                audio.HandleCreated -= OnHandleCreated;
                audio.Invoke(audio.Play);
            }
        }

        public void Dispose()
        {
            if (waveStream != null)
            {
                waveStream.Dispose();
                waveStream = null;
            }
        }
    }
}
