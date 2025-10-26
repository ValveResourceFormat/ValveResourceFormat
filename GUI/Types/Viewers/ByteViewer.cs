using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;

namespace GUI.Types.Viewers
{
    class ByteViewer : IViewer
    {
        public static bool IsAccepted() => true;

        public TabPage Create(VrfGuiContext vrfGuiContext, Stream stream)
        {
            var tab = new TabPage();
            var resTabs = new ThemedTabControl
            {
                Dock = DockStyle.Fill,
            };
            tab.Controls.Add(resTabs);

            var bvTab = new TabPage("Hex");
            var bv = new System.ComponentModel.Design.ByteViewer
            {
                Dock = DockStyle.Fill,
            };
            bvTab.Controls.Add(bv);
            resTabs.TabPages.Add(bvTab);

            byte[] input;

            if (stream == null)
            {
                input = File.ReadAllBytes(vrfGuiContext.FileName!);
            }
            else
            {
                input = new byte[stream.Length];
                stream.ReadExactly(input);
            }

            var text = GetTextFromBytes(input.AsSpan());

            if (!string.IsNullOrEmpty(text))
            {
                var textTab = new TabPage("Text");
                var textBox = CodeTextBox.Create(text);
                textTab.Controls.Add(textBox);
                resTabs.TabPages.Add(textTab);
                resTabs.SelectedTab = textTab;
            }

            Program.MainForm.Invoke((MethodInvoker)(() =>
            {
                bv.SetBytes(input);
            }));

            return tab;
        }

        public static string? GetTextFromBytes(ReadOnlySpan<byte> span)
        {
            if (span.Length >= 4 && span[0] == 0xFF && span[1] == 0xFE && span[2] == 0x00 && span[3] == 0x00)  // UTF-32 LE BOM
            {
                var enc = new System.Text.UTF32Encoding(bigEndian: false, byteOrderMark: true);
                return enc.GetString(span[4..]);
            }

            if (span.Length >= 4 && span[0] == 0x00 && span[1] == 0x00 && span[2] == 0xFE && span[3] == 0xFF) // UTF-32 BE BOM
            {
                var enc = new System.Text.UTF32Encoding(bigEndian: true, byteOrderMark: true);
                return enc.GetString(span[4..]);
            }

            if (span.Length >= 2 && span[0] == 0xFF && span[1] == 0xFE) // UTF-16 LE BOM
            {
                return System.Text.Encoding.Unicode.GetString(span[2..]);
            }

            if (span.Length >= 2 && span[0] == 0xFE && span[1] == 0xFF) // UTF-16 BE BOM
            {
                var enc = new System.Text.UnicodeEncoding(bigEndian: true, byteOrderMark: true);
                return enc.GetString(span[2..]);
            }

            if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF) // UTF-8 BOM
            {
                return System.Text.Encoding.UTF8.GetString(span[3..]);
            }

            var firstNullByte = span.IndexOf((byte)0);
            if (firstNullByte < 0)
            {
                return System.Text.Encoding.UTF8.GetString(span); // No null bytes found
            }

            if (firstNullByte == 0)
            {
                return null; // Starts with null byte
            }

            // Check if everything after first null byte is also null
            var remainingBytes = span[(firstNullByte + 1)..];
            foreach (var b in remainingBytes)
            {
                if (b != 0)
                {
                    return null; // Has embedded null bytes
                }
            }

            // Only trailing nulls, trim them and decode as UTF-8
            return System.Text.Encoding.UTF8.GetString(span[..firstNullByte]);
        }
    }
}
