using System.Drawing;
using System.Windows.Forms;
using GUI.Forms;

namespace GUI.Utils
{
    internal class MonospaceTextBox : TextBox
    {
        private string PreviousSearchText;

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
            KeyDown += OnKeyDown;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            const Keys ctrlF = Keys.Control | Keys.F;

            if ((e.KeyData & ctrlF) == ctrlF)
            {
                if ((e.KeyData & Keys.Shift) == 0)
                {
                    var searchForm = new SearchForm();
                    searchForm.HideSearchType();
                    searchForm.SearchText = PreviousSearchText ?? string.Empty;
                    var result = searchForm.ShowDialog();
                    if (result != DialogResult.OK)
                    {
                        return;
                    }

                    var searchText = searchForm.SearchText;
                    PreviousSearchText = searchText;
                }

                if (string.IsNullOrEmpty(PreviousSearchText))
                {
                    return;
                }

                // Search from selection end
                var index = Text.IndexOf(PreviousSearchText, SelectionStart + SelectionLength, System.StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    // If no match, search from beginning until selection
                    index = Text.IndexOf(PreviousSearchText, 0, SelectionStart, System.StringComparison.OrdinalIgnoreCase);

                    if (index < 0)
                    {
                        return;
                    }
                }

                SelectionStart = index;
                SelectionLength = PreviousSearchText.Length;
                ScrollToCaret();
            }
        }
    }
}
