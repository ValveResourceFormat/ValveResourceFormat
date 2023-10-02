using System;
using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;

namespace GUI.Types.Viewers
{
    class ByteViewer : IViewer
    {
        public static bool IsAccepted() => true;

        public TabPage Create(VrfGuiContext vrfGuiContext, byte[] input)
        {
            var tab = new TabPage();
            var resTabs = new TabControl
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

            input ??= File.ReadAllBytes(vrfGuiContext.FileName);

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
                var text = new CodeTextBox
                {
                    Text = System.Text.Encoding.UTF8.GetString(span).ReplaceLineEndings(),
                };
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
