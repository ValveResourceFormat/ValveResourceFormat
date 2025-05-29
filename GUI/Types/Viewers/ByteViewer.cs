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

            var textSpan = GetTextFromBytes(input.AsSpan());

            if (!textSpan.IsEmpty)
            {
                var textTab = new TabPage("Text");
                var text = CodeTextBox.Create(System.Text.Encoding.UTF8.GetString(textSpan));
                textTab.Controls.Add(text);
                resTabs.TabPages.Add(textTab);
                resTabs.SelectedTab = textTab;
            }

            Program.MainForm.Invoke((MethodInvoker)(() =>
            {
                bv.SetBytes(input);
            }));

            return tab;
        }

        public static ReadOnlySpan<byte> GetTextFromBytes(ReadOnlySpan<byte> span)
        {
            var firstNullByte = span.IndexOf((byte)0);
            if (firstNullByte < 0)
            {
                return span; // No null bytes found
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

            // Only trailing nulls, trim them
            return span[..firstNullByte];
        }
    }
}
