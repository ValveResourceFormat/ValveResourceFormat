using System.Drawing;
using System.Windows.Forms;

namespace GUI.Utils
{
    internal class MonospaceTextBox : TextBox
    {
        public MonospaceTextBox() : base()
        {
            const int FontSize = 9;

            try
            {
                using var font = new FontFamily("Cascadia Mono");
                Font = new Font(font, FontSize);

            }
            catch
            {
                Font = new Font(FontFamily.GenericMonospace, FontSize);
            }

            Dock = DockStyle.Fill;
            ScrollBars = ScrollBars.Both;
            BorderStyle = BorderStyle.None;
            ReadOnly = true;
            Multiline = true;
            WordWrap = false;
        }
    }
}
