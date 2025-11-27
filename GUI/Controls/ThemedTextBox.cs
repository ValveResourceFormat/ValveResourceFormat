using System.ComponentModel;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls
{
    public class ThemedTextBox : TextBox
    {
        protected override bool DoubleBuffered { get; set; } = true;

        public ThemedTextBox()
        {
            Multiline = true;
            Margin = new Padding(0, 3, 0, 3);
        }
        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            BackColor = Themer.CurrentThemeColors.Border;
            ForeColor = Themer.CurrentThemeColors.Contrast;
        }

        public event EventHandler? CustomTextChanged;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        private bool FireCustomTextChanged = true;

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);

            if (FireCustomTextChanged && CustomTextChanged != null)
            {
                CustomTextChanged(this, e);
            }
        }

        public void SetTextWithoutCustomEvent(string text)
        {
            FireCustomTextChanged = false;
            Text = text;
            FireCustomTextChanged = true;
        }
    }
}
