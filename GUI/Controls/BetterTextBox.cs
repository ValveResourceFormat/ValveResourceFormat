using System.ComponentModel;
using System.Windows.Forms;

namespace GUI.Controls
{
    public class BetterTextBox : TextBox
    {
        public BetterTextBox()
        {
            Multiline = true;
            Margin = new Padding(0, 3, 0, 3);
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
