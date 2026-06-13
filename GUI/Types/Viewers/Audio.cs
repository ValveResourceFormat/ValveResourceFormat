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

        public async Task LoadAsync(Stream? stream)
        {
            if (stream == null)
            {
                if (vrfGuiContext.FileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    waveStream = new WaveFileReader(vrfGuiContext.FileName);
                    if (waveStream.WaveFormat.Encoding != WaveFormatEncoding.Pcm && waveStream.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
                    {
                        waveStream = WaveFormatConversionStream.CreatePcmStream(waveStream);
                        waveStream = new BlockAlignReductionStream(waveStream);
                    }
                }
                else if (vrfGuiContext.FileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                {
                    //waveStream = new MediaFoundationReader(vrfGuiContext.FileName);
                    waveStream = new Mp3FileReaderBase(vrfGuiContext.FileName, wf => new Mp3FrameDecompressor(wf));
                }
                else
                {
                    throw new NotImplementedException($"Unknown audio file extension: {Path.GetExtension(vrfGuiContext.FileName)}");
                }
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

            var autoPlay = ((Settings.QuickPreviewFlags)Settings.Config.QuickFilePreview & Settings.QuickPreviewFlags.AutoPlaySounds) != 0;
            var audio = new AudioPlaybackPanel(waveStream, isPreview && autoPlay, (0, 0));
            tab.Controls.Add(audio);

            waveStream = null;

            return;
        }

        public void Dispose()
        {
            waveStream?.Dispose();
            waveStream = null;
        }
    }
}
