using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using NAudio.Wave;

namespace GUI.Types.Viewers
{
    public class Audio : IViewer
    {
        public static bool IsAccepted(uint magic)
        {
            return magic == 123; // TODO
        }

        public TabPage Create(VrfGuiContext vrfGuiContext, byte[] input)
        {
            var tab = new TabPage();
            var audio = new AudioPlaybackPanel(new AudioFileReader(vrfGuiContext.FileName)); // TODO: Support input
            tab.Controls.Add(audio);
            return tab;
        }
    }
}
