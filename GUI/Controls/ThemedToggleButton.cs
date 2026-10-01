using GUI.Utils;

namespace GUI.Controls
{
    /// <summary>A button that stays pressed while <see cref="Checked"/>, drawn in the accent colour.</summary>
    public class ThemedToggleButton : ThemedButton
    {
        /// <summary>Gets or sets whether the button is pressed. Setting it does not raise <see cref="Toggled"/>.</summary>
        public bool Checked
        {
            get;
            set
            {
                if (field == value)
                {
                    return;
                }

                field = value;
                UpdateColors();
            }
        }

        /// <summary>Raised when the user clicks the button, after <see cref="Checked"/> has flipped.</summary>
        public event Action<bool>? Toggled;

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
            Toggled?.Invoke(Checked);
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            UpdateColors();
        }

        private void UpdateColors()
        {
            if (!Style)
            {
                return;
            }

            BackColor = Checked ? Themer.CurrentThemeColors.Accent : Themer.CurrentThemeColors.Border;
            Invalidate();
        }
    }
}
