using System.Drawing;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls
{
    /// <summary>
    /// A panel that displays multiple keybinding controls in a horizontal flow layout.
    /// </summary>
    public class KeybindingsPanel : FlowLayoutPanel
    {
        public KeybindingsPanel()
        {
            FlowDirection = FlowDirection.LeftToRight;
            WrapContents = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Color.Transparent;
            Padding = new Padding(this.AdjustForDPI(4), this.AdjustForDPI(4), this.AdjustForDPI(4), this.AdjustForDPI(4));
        }

        /// <summary>
        /// Updates the panel to display the specified keybindings.
        /// </summary>
        /// <param name="keybindings">List of keybindings to display</param>
        public void SetKeybindings(List<KeybindingInfo> keybindings)
        {
            SuspendLayout();

            Controls.Clear();

            foreach (var binding in keybindings)
            {
                var keycap = new KeycapControl
                {
                    KeyText = binding.KeyCombination,
                    Description = binding.Description,
                    Margin = new Padding(0, 0, this.AdjustForDPI(4), 0)
                };
                Controls.Add(keycap);
            }

            ResumeLayout();
        }
    }
}
