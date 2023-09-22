using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GUI.Forms;

namespace GUI.Utils
{
    internal class MonospaceTextBox : TextBox
    {
        private string PreviousSearchText;

        // set tab stops to a width of 4 https://stackoverflow.com/a/12953632
        private readonly static int[] tabWidth = new[] { 4 * 4 };

        private const int EM_SETTABSTOPS = 0x00CB;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr SendMessage(IntPtr h, int msg, int wParam, int[] lParam);

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
            HandleCreated += OnHandleCreated;
            KeyDown += OnKeyDown;
            Disposed += OnDisposed;
        }

        private void OnHandleCreated(object sender, EventArgs e)
        {
            SendMessage(Handle, EM_SETTABSTOPS, 1, tabWidth);
        }

        private void OnDisposed(object sender, System.EventArgs e)
        {
            HandleCreated -= OnHandleCreated;
            KeyDown -= OnKeyDown;
            Disposed -= OnDisposed;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            const Keys ctrlF = Keys.Control | Keys.F;

            if ((e.KeyData & ctrlF) == ctrlF)
            {
                if ((e.KeyData & Keys.Shift) == 0)
                {
                    using var searchForm = new SearchForm();
                    searchForm.HideSearchType();
                    searchForm.SearchText = PreviousSearchText ?? string.Empty;

                    var result = searchForm.ShowDialog();
                    if (result != DialogResult.OK)
                    {
                        return;
                    }

                    PreviousSearchText = searchForm.SearchText;
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
