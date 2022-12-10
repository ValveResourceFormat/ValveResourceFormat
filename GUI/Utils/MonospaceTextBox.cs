using System.Drawing;
using System.Windows.Forms;

namespace GUI.Utils
{
    internal class MonospaceTextBox : TextBox
    {
        public MonospaceTextBox() : base()
        {
            Font = new Font(FontFamily.GenericMonospace, 9);
            Dock = DockStyle.Fill;
            ScrollBars = ScrollBars.Both;
            BorderStyle = BorderStyle.None;
            ReadOnly = true;
            Multiline = true;
            WordWrap = false;
        }
    }
}
