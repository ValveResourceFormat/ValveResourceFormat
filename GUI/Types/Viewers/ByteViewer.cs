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

            var span = input.AsSpan();
            var firstNullByte = span.IndexOf((byte)0);
            var hasNullBytes = firstNullByte >= 0;

            if (hasNullBytes && firstNullByte > 0)
            {
                var isTrailingNulls = true;

                for (var i = span.Length - 1; i > firstNullByte; i--)
                {
                    if (span[i] != 0x00)
                    {
                        isTrailingNulls = false;
                        break;
                    }
                }

                if (isTrailingNulls)
                {
                    span = span[..firstNullByte];
                    hasNullBytes = false;
                }
            }

            if (!hasNullBytes)
            {
                var textTab = new TabPage("Text");
                var text = CodeTextBox.Create(System.Text.Encoding.UTF8.GetString(span));
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
    }
}
