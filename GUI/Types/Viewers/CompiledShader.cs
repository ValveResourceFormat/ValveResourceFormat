using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat.CompiledShader;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;
using VrfPackage = SteamDatabase.ValvePak.Package;

namespace GUI.Types.Viewers
{
    public class CompiledShader : IDisposable, IViewer
    {

        public static bool IsAccepted(uint magic)
        {
            return magic == ShaderFile.MAGIC;
        }
        public class ShaderTabControl : TabControl
        {
            public ShaderTabControl() : base() { }
            protected override void OnKeyDown(KeyEventArgs ke)
            {
                base.OnKeyDown(ke);
                if (ke.KeyData == Keys.Escape)
                {
                    int tabIndex = SelectedIndex;
                    if (tabIndex > 0)
                    {
                        TabPages.RemoveAt(tabIndex);
                        SelectedIndex = tabIndex - 1;
                    }
                }
            }
        }

        private ShaderTabControl tabControl;
        private ShaderFile shaderFile;
        private SortedDictionary<(VcsProgramType, string), ShaderFile> shaderCollection;

        public TabPage Create(VrfGuiContext vrfGuiContext, byte[] input)
        {

            shaderCollection = GetShaderCollection(vrfGuiContext.FileName, vrfGuiContext.CurrentPackage);
            string filename = Path.GetFileName(vrfGuiContext.FileName);
            shaderFile = shaderCollection[(ComputeVcsProgramType(filename), filename)];
            var tab = new TabPage();
            tabControl = new ShaderTabControl
            {
                Dock = DockStyle.Fill,
            };

            tabControl.MouseClick += new MouseEventHandler(OnTabClick);
            var mainFileTab = new TabPage(Path.GetFileName(vrfGuiContext.FileName));
            var shaderRichTextBox = new ShaderRichTextBox(shaderFile, tabControl, shaderCollection);
            mainFileTab.Controls.Add(shaderRichTextBox);
            tabControl.Controls.Add(mainFileTab);
            tab.Controls.Add(tabControl);
            shaderRichTextBox.MouseEnter += new EventHandler(MouseEnterHandler);
            string helpText = "[ctrl+click to open and focus links, ESC or right-click on tabs to close]\n\n";
            shaderRichTextBox.Text = $"{helpText}{shaderRichTextBox.Text}";
            return tab;
        }

        private static SortedDictionary<(VcsProgramType, string), ShaderFile> GetShaderCollection(string targetFilename, VrfPackage vrfPackage)
        {
            SortedDictionary<(VcsProgramType, string), ShaderFile> shaderCollection = new();
            if (vrfPackage != null)
            {
                // search the package
                string vcsCollectionName = targetFilename.Substring(0, targetFilename.LastIndexOf('_')); // in the form water_dota_pcgl_40
                List<PackageEntry> vcsEntries = vrfPackage.Entries["vcs"];
                // vcsEntry.FileName is in the form bloom_dota_pcgl_30_ps (without vcs extension)
                foreach (var vcsEntry in vcsEntries)
                {
                    if (vcsEntry.FileName.StartsWith(vcsCollectionName))
                    {
                        VcsProgramType programType = ComputeVcsProgramType($"{vcsEntry.FileName}.vcs");
                        vrfPackage.ReadEntry(vcsEntry, out var shaderDatabytes);
                        ShaderFile relatedShaderFile = new();
                        relatedShaderFile.Read($"{vcsEntry.FileName}.vcs", new MemoryStream(shaderDatabytes));
                        shaderCollection.Add((programType, $"{vcsEntry.FileName}.vcs"), relatedShaderFile);
                    }
                }
            }
            else
            {
                // search file-system
                string filename = Path.GetFileName(targetFilename);
                string vcsCollectionName = filename.Substring(0, filename.LastIndexOf('_'));
                foreach (var vcsFile in Directory.GetFiles(Path.GetDirectoryName(targetFilename)))
                {
                    if (Path.GetFileName(vcsFile).StartsWith(vcsCollectionName))
                    {
                        VcsProgramType programType = ComputeVcsProgramType(vcsFile);
                        ShaderFile relatedShaderFile = new();
                        relatedShaderFile.Read(vcsFile);
                        shaderCollection.Add((programType, Path.GetFileName(vcsFile)), relatedShaderFile);
                    }
                }
            }
            return shaderCollection;
        }

        private static void MouseEnterHandler(object sender, EventArgs e)
        {
            RichTextBox shaderRTB = sender as RichTextBox;
            shaderRTB.Focus();
        }

        // Find the tab being clicked
        private void OnTabClick(object sender, MouseEventArgs e)
        {
            var tabControl = sender as TabControl;
            var tabs = tabControl.TabPages;
            TabPage thisTab = tabs.Cast<TabPage>().Where((t, i) => tabControl.GetTabRect(i).Contains(e.Location)).First();
            if (e.Button == MouseButtons.Right)
            {
                var tabIndex = GetTabIndex(thisTab);
                // don't close the main tab
                if (tabIndex == 0)
                {
                    return;
                }
                if (tabIndex == tabControl.SelectedIndex && tabIndex > 0)
                {
                    tabControl.SelectedIndex = tabIndex - 1;
                }
                tabControl.TabPages.Remove(thisTab);
                thisTab.Dispose();
            }
        }

        private int GetTabIndex(TabPage tab)
        {
            for (int i = 0; i < tabControl.TabPages.Count; i++)
            {
                if (tabControl.TabPages[i] == tab)
                {
                    return i;
                }
            }
            return -1;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && tabControl != null)
            {
                tabControl.Dispose();
                tabControl = null;
            }
            if (disposing && shaderFile != null)
            {
                shaderFile.Dispose();
                tabControl = null;
            }
        }

        private class ShaderRichTextBox : RichTextBox
        {
            private ShaderFile shaderFile;
            SortedDictionary<(VcsProgramType, string), ShaderFile> shaderCollection;
            private ShaderTabControl tabControl;
            private List<string> relatedFiles = new();
            public ShaderRichTextBox(ShaderFile shaderFile, ShaderTabControl tabControl,
                SortedDictionary<(VcsProgramType, string), ShaderFile> shaderCollection = null, bool byteVersion = false) : base()
            {
                this.shaderFile = shaderFile;
                this.tabControl = tabControl;
                if (shaderCollection != null)
                {
                    this.shaderCollection = shaderCollection;
                    foreach (var vcsFilenames in shaderCollection.Keys)
                    {
                        relatedFiles.Add(vcsFilenames.Item2);
                    }
                }
                var buffer = new StringWriter(CultureInfo.InvariantCulture);
                if (!byteVersion)
                {
                    shaderFile.PrintSummary(buffer.Write, showRichTextBoxLinks: true, relatedfiles: relatedFiles);
                }
                else
                {
                    shaderFile.PrintByteAnalysis(OutputWriter: buffer.Write);
                }
                Font = new Font(FontFamily.GenericMonospace, Font.Size);
                DetectUrls = true;
                Dock = DockStyle.Fill;
                Multiline = true;
                ReadOnly = true;
                WordWrap = false;
                Text = Utils.Utils.NormalizeLineEndings(buffer.ToString());
                ScrollBars = RichTextBoxScrollBars.Both;
                LinkClicked += new LinkClickedEventHandler(ShaderRichTextBoxLinkClicked);
            }

            private void ShaderRichTextBoxLinkClicked(object sender, LinkClickedEventArgs evt)
            {
                string linkText = evt.LinkText[2..]; // remove two starting backslahses
                string[] linkTokens = linkText.Split("\\");
                VcsProgramType programType = ComputeVcsProgramType(linkTokens[0]);
                if (programType != VcsProgramType.Undetermined)
                {
                    ShaderFile shaderFile = shaderCollection[(programType, linkTokens[0])];
                    TabPage newShaderTab = null;
                    if (linkTokens.Length > 1 && linkTokens[1].Equals("bytes", StringComparison.Ordinal))
                    {
                        newShaderTab = new TabPage($"{programType} bytes");
                        var shaderRichTextBox = new ShaderRichTextBox(shaderFile, tabControl, byteVersion: true);
                        shaderRichTextBox.MouseEnter += new EventHandler(MouseEnterHandler);
                        newShaderTab.Controls.Add(shaderRichTextBox);
                        tabControl.Controls.Add(newShaderTab);
                    }
                    else
                    {
                        newShaderTab = new TabPage($"{programType}");
                        var shaderRichTextBox = new ShaderRichTextBox(shaderFile, tabControl, shaderCollection);
                        shaderRichTextBox.MouseEnter += new EventHandler(MouseEnterHandler);
                        newShaderTab.Controls.Add(shaderRichTextBox);
                        tabControl.Controls.Add(newShaderTab);
                    }
                    if ((ModifierKeys & Keys.Control) == Keys.Control)
                    {
                        tabControl.SelectedTab = newShaderTab;
                    }
                    return;
                }
                long zframeId = Convert.ToInt64(linkText, 16);
                var zframeTab = new TabPage($"{shaderFile.filenamepath.Split('_')[^1][..^4]}[{zframeId:x}]");
                var zframeRichTextBox = new ZFrameRichTextBox(tabControl, shaderFile, zframeId);
                zframeRichTextBox.MouseEnter += new EventHandler(MouseEnterHandler);
                zframeTab.Controls.Add(zframeRichTextBox);
                tabControl.Controls.Add(zframeTab);
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    tabControl.SelectedTab = zframeTab;
                }
            }
        }


        private class ZFrameRichTextBox : RichTextBox
        {
            private TabControl tabControl;
            private ShaderFile shaderFile;
            private ZFrameFile zframeFile;

            public ZFrameRichTextBox(TabControl tabControl, ShaderFile shaderFile, long zframeId, bool byteVersion = false) : base()
            {
                this.tabControl = tabControl;
                this.shaderFile = shaderFile;
                var buffer = new StringWriter(CultureInfo.InvariantCulture);
                zframeFile = shaderFile.GetZFrameFile(zframeId, OutputWriter: buffer.Write);
                if (byteVersion)
                {
                    zframeFile.PrintByteAnalysis();
                }
                else
                {
                    PrintZFrameSummary zframeSummary = new PrintZFrameSummary(shaderFile, zframeFile, buffer.Write, true);
                }
                Font = new Font(FontFamily.GenericMonospace, Font.Size);
                DetectUrls = true;
                Dock = DockStyle.Fill;
                Multiline = true;
                ReadOnly = true;
                WordWrap = false;
                Text = Utils.Utils.NormalizeLineEndings(buffer.ToString());
                ScrollBars = RichTextBoxScrollBars.Both;
                LinkClicked += new LinkClickedEventHandler(ZFrameRichTextBoxLinkClicked);
            }

            private void ZFrameRichTextBoxLinkClicked(object sender, LinkClickedEventArgs evt)
            {
                string[] linkTokens = evt.LinkText[2..].Split("\\");
                if (linkTokens.Length == 1)
                {
                    long zframeId = Convert.ToInt64(linkTokens[0].Split('-')[^2][6..], 16);
                    var zframeTab = new TabPage($"{shaderFile.filenamepath.Split('_')[^1][..^4]}[{zframeId:x}] bytes");
                    var zframeRichTextBox = new ZFrameRichTextBox(tabControl, shaderFile, zframeId, byteVersion: true);
                    zframeRichTextBox.MouseEnter += new EventHandler(MouseEnterHandler);
                    zframeTab.Controls.Add(zframeRichTextBox);
                    tabControl.Controls.Add(zframeTab);
                    if ((ModifierKeys & Keys.Control) == Keys.Control)
                    {
                        tabControl.SelectedTab = zframeTab;
                    }
                    return;
                }
                int glslSourceId = Convert.ToInt32(linkTokens[1]);
                var buffer = new StringWriter(CultureInfo.InvariantCulture);
                zframeFile.PrintGlslSource(glslSourceId, buffer.Write);
                var glslTab = new TabPage($"{shaderFile.filenamepath.Split('_')[^1][..^4]}[{zframeFile.zframeId:x}]-glsl[{glslSourceId}]");
                var glslRichTextBox = new RichTextBox();
                glslRichTextBox.Font = new Font(FontFamily.GenericMonospace, Font.Size);
                glslRichTextBox.DetectUrls = true;
                glslRichTextBox.Dock = DockStyle.Fill;
                glslRichTextBox.Multiline = true;
                glslRichTextBox.ReadOnly = true;
                glslRichTextBox.WordWrap = false;
                glslRichTextBox.Text = Utils.Utils.NormalizeLineEndings(buffer.ToString());
                glslRichTextBox.ScrollBars = RichTextBoxScrollBars.Both;
                glslTab.Controls.Add(glslRichTextBox);
                tabControl.Controls.Add(glslTab);
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    tabControl.SelectedTab = glslTab;
                }
            }
        }
    }
}
