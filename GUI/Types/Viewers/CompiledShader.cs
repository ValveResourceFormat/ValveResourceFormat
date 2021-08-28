using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.CompiledShader;

namespace GUI.Types.Viewers
{
    public static class CompiledShader
    {
        public static bool IsAccepted(uint magic)
        {
            return magic == ShaderFile.MAGIC;
        }
        public static TabPage Create(VrfGuiContext vrfGuiContext, byte[] input, TabPage parentTab)
        {
            var tab = new TabPage();
            var control = new ShaderRichTextBox(vrfGuiContext, input, parentTab);
            tab.Controls.Add(control);
            return tab;
        }
    }

    public class ShaderRichTextBox : RichTextBox
    {
        private readonly TabPage parentTab;
        private ShaderFile shaderFile;

        public ShaderRichTextBox(VrfGuiContext vrfGuiContext, byte[] input, TabPage parentTab) : base()
        {
            this.parentTab = parentTab;
            shaderFile = new ShaderFile();
            var buffer = new StringWriter(CultureInfo.InvariantCulture);
            if (input != null)
            {
                shaderFile.Read(vrfGuiContext.FileName, new MemoryStream(input));
            }
            else
            {
                shaderFile.Read(vrfGuiContext.FileName);
            }
            shaderFile.PrintSummary(buffer.Write);
            Font = new Font(FontFamily.GenericMonospace, Font.Size);
            DetectUrls = true;
            Dock = DockStyle.Fill;
            Multiline = true;
            ReadOnly = true;
            WordWrap = false;
            Text = Utils.Utils.NormalizeLineEndings(buffer.ToString());
            ScrollBars = RichTextBoxScrollBars.Both;
            LinkClicked += new LinkClickedEventHandler(Link_Clicked);
        }

        private void Link_Clicked(object sender, LinkClickedEventArgs e)
        {
            var buffer = new StringWriter(CultureInfo.InvariantCulture);
            var zframeId = (long)Convert.ToInt64(e.LinkText[2..], 16);
            var zframeFile = shaderFile.GetZFrameFile(zframeId, OutputWriter: buffer.Write);
            zframeFile.PrintByteAnalysis();
            parentTab.Text = $"Z[{zframeId:X08}]";
            Text = Utils.Utils.NormalizeLineEndings(buffer.ToString());
            Console.WriteLine($"Opening {Path.GetFileName(shaderFile.filenamepath)[..^4]} ZFRAME[{zframeId:X08}]");
        }
    }
}
