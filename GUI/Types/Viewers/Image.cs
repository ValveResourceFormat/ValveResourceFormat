using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Types.Viewers
{
    public class Image : IViewer
    {
        public static bool IsAccepted(uint magic)
        {
            return magic == 0x474E5089 || /* png */
                   magic << 8 == 0xFFD8FF00 || /* jpg */
                   magic << 8 == 0x46494700; /* gif */
        }

        public TabPage Create(VrfGuiContext vrfGuiContext, byte[] input)
        {
            var img = input != null ? System.Drawing.Image.FromStream(new MemoryStream(input)) : System.Drawing.Image.FromFile(vrfGuiContext.FileName);

            var control = new Forms.Texture
            {
                BackColor = Color.Black,
            };
            control.SetImage(new Bitmap(img), Path.GetFileNameWithoutExtension(vrfGuiContext.FileName), img.Width, img.Height);

            var tab = new TabPage();
            tab.Controls.Add(control);
            return tab;
        }
    }
}
