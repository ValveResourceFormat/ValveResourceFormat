using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
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
                    var tabIndex = SelectedIndex;
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
            var filename = Path.GetFileName(vrfGuiContext.FileName);
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
            var helpText = "[ctrl+click to open and focus links, ESC or right-click on tabs to close]\n\n";
            shaderRichTextBox.Text = $"{helpText}{shaderRichTextBox.Text}";

            {
                var control = new MonospaceTextBox
                {
                    Text = Utils.Utils.NormalizeLineEndings(new ShaderExtract(shaderCollection).ToVFX()),
                };

                var vfx = new TabPage("Reconstructed vfx");
                vfx.Controls.Add(control);
                tabControl.TabPages.Add(vfx);
            }
            return tab;
        }

        private static SortedDictionary<(VcsProgramType, string), ShaderFile> GetShaderCollection(string targetFilename, VrfPackage vrfPackage)
        {
            SortedDictionary<(VcsProgramType, string), ShaderFile> shaderCollection = new();
            if (vrfPackage != null)
            {
                // search the package
                var filename = Path.GetFileName(targetFilename);
                var vcsCollectionName = filename[..filename.LastIndexOf('_')]; // in the form water_dota_pcgl_40
                var vcsEntries = vrfPackage.Entries["vcs"];
                // vcsEntry.FileName is in the form bloom_dota_pcgl_30_ps (without vcs extension)
                foreach (var vcsEntry in vcsEntries)
                {
                    if (vcsEntry.FileName.StartsWith(vcsCollectionName, StringComparison.InvariantCulture))
                    {
                        var programType = ComputeVcsProgramType($"{vcsEntry.FileName}.vcs");
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
                var filename = Path.GetFileName(targetFilename);
                var vcsCollectionName = filename[..filename.LastIndexOf('_')];
                foreach (var vcsFile in Directory.GetFiles(Path.GetDirectoryName(targetFilename)))
                {
                    if (Path.GetFileName(vcsFile).StartsWith(vcsCollectionName, StringComparison.InvariantCulture))
                    {
                        var programType = ComputeVcsProgramType(vcsFile);
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
            var shaderRTB = sender as RichTextBox;
            shaderRTB.Focus();
        }

        // Find the tab being clicked
        private void OnTabClick(object sender, MouseEventArgs e)
        {
            var tabControl = sender as TabControl;
            var tabs = tabControl.TabPages;
            var thisTab = tabs.Cast<TabPage>().Where((t, i) => tabControl.GetTabRect(i).Contains(e.Location)).First();
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
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
            for (var i = 0; i < tabControl.TabPages.Count; i++)
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
            if (disposing)
            {
                if (tabControl != null)
                {
                    tabControl.Dispose();
                    tabControl = null;
                }

                foreach (var shader in shaderCollection.Values)
                {
                    shader.Dispose();
                }
            }
        }

        private class ShaderRichTextBox : RichTextBox
        {
            private readonly ShaderFile shaderFile;
            readonly SortedDictionary<(VcsProgramType, string), ShaderFile> shaderCollection;
            private readonly ShaderTabControl tabControl;
            private readonly List<string> relatedFiles = new();
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
                    shaderFile.PrintByteDetail(outputWriter: buffer.Write);
                }
                Font = new Font(FontFamily.GenericMonospace, Font.Size);
                DetectUrls = true;
                Dock = DockStyle.Fill;
                Multiline = true;
                ReadOnly = true;
                WordWrap = false;
                Text = buffer.ToString().ReplaceLineEndings();
                ScrollBars = RichTextBoxScrollBars.Both;
                LinkClicked += new LinkClickedEventHandler(ShaderRichTextBoxLinkClicked);
            }

            private void ShaderRichTextBoxLinkClicked(object sender, LinkClickedEventArgs evt)
            {
                var linkText = evt.LinkText[2..]; // remove two starting backslahses
                var linkTokens = linkText.Split("\\");
                // linkTokens[0] is sometimes a zframe id, in those cases programType equals 'undetermined'
                // where linkTokens[0] is a filename VcsProgramType should be defined
                var programType = ComputeVcsProgramType(linkTokens[0]);
                if (programType != VcsProgramType.Undetermined)
                {
                    var shaderFile = shaderCollection[(programType, linkTokens[0])];
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
                var zframeId = Convert.ToInt64(linkText, 16);
                var zframeTab = new TabPage($"{shaderFile.FilenamePath.Split('_')[^1][..^4]}[{zframeId:x}]");
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


        private class ZFrameRichTextBox : RichTextBox, IDisposable
        {
            private readonly TabControl tabControl;
            private readonly ShaderFile shaderFile;
            private ZFrameFile zframeFile;

            public ZFrameRichTextBox(TabControl tabControl, ShaderFile shaderFile, long zframeId, bool byteVersion = false) : base()
            {
                this.tabControl = tabControl;
                this.shaderFile = shaderFile;
                var buffer = new StringWriter(CultureInfo.InvariantCulture);
                zframeFile = shaderFile.GetZFrameFile(zframeId, outputWriter: buffer.Write);
                if (byteVersion)
                {
                    zframeFile.PrintByteDetail();
                }
                else
                {
                    PrintZFrameSummary zframeSummary = new(shaderFile, zframeFile, buffer.Write, true);
                }
                Font = new Font(FontFamily.GenericMonospace, Font.Size);
                DetectUrls = true;
                Dock = DockStyle.Fill;
                Multiline = true;
                ReadOnly = true;
                WordWrap = false;
                Text = buffer.ToString().ReplaceLineEndings();
                ScrollBars = RichTextBoxScrollBars.Both;
                LinkClicked += new LinkClickedEventHandler(ZFrameRichTextBoxLinkClicked);
            }

            public new void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected new virtual void Dispose(bool disposing)
            {
                if (disposing && zframeFile != null)
                {
                    zframeFile.Dispose();
                    zframeFile = null;
                }

                base.Dispose(disposing);
            }

            private void ZFrameRichTextBoxLinkClicked(object sender, LinkClickedEventArgs evt)
            {
                var linkTokens = evt.LinkText[2..].Split("\\");
                // if the link contains only one token it is the name of the zframe in the form
                // blur_pcgl_40_vs.vcs-ZFRAME00000000-databytes
                if (linkTokens.Length == 1)
                {
                    // the target id is extracted from the text link, parsing here strictly depends on the chosen format
                    // linkTokens[0].Split('-')[^2] evaluates as ZFRAME00000000, number is read as base 16
                    var zframeId = Convert.ToInt64(linkTokens[0].Split('-')[^2][6..], 16);
                    var zframeTab = new TabPage($"{shaderFile.FilenamePath.Split('_')[^1][..^4]}[{zframeId:x}] bytes");
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
                // if (linkTokens.Length != 1) the link text will always be in the form '\\source\0'
                // the sourceId is given in decimals, extracted here from linkTokens[1]
                // (the sourceId is not the same as the zframeId - a single zframe may contain more than 1 source,
                // they are enumerated in each zframe file starting from 0)
                var gpuSourceId = Convert.ToInt32(linkTokens[1], CultureInfo.InvariantCulture);
                var gpuSourceTabTitle = $"{shaderFile.FilenamePath.Split('_')[^1][..^4]}[{zframeFile.ZframeId:x}]({gpuSourceId})";

                TabPage gpuSourceTab = null;
                var buffer = new StringWriter(CultureInfo.InvariantCulture);
                zframeFile.PrintGpuSource(gpuSourceId, buffer.Write);
                switch (zframeFile.GpuSources[gpuSourceId])
                {
                    case GlslSource:
                        gpuSourceTab = new TabPage(gpuSourceTabTitle);
                        var gpuSourceRichTextBox = new RichTextBox
                        {
                            Font = new Font(FontFamily.GenericMonospace, Font.Size),
                            DetectUrls = true,
                            Dock = DockStyle.Fill,
                            Multiline = true,
                            ReadOnly = true,
                            WordWrap = false,
                            Text = buffer.ToString().ReplaceLineEndings(),
                            ScrollBars = RichTextBoxScrollBars.Both
                        };
                        gpuSourceTab.Controls.Add(gpuSourceRichTextBox);
                        break;

                    case DxbcSource:
                    case DxilSource:
                    case VulkanSource:
                        var input = zframeFile.GpuSources[gpuSourceId].Sourcebytes;
                        gpuSourceTab = CreateByteViewerTab(input, buffer.ToString());
                        gpuSourceTab.Text = gpuSourceTabTitle;
                        break;

                    default:
                        throw new InvalidDataException($"Unimplemented GPU source type {zframeFile.GpuSources[gpuSourceId].GetType()}");
                }

                tabControl.Controls.Add(gpuSourceTab);
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    tabControl.SelectedTab = gpuSourceTab;
                }
            }

            private static TabPage CreateByteViewerTab(byte[] databytes, string dataFormatted)
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
                var textTab = new TabPage("Bytes");
                var textBox = new System.Windows.Forms.RichTextBox
                {
                    Dock = DockStyle.Fill,
                    ScrollBars = RichTextBoxScrollBars.Both,
                    Multiline = true,
                    ReadOnly = true,
                    WordWrap = false,
                    Text = dataFormatted,
                };
                textBox.Font = new Font(FontFamily.GenericMonospace, textBox.Font.Size);
                textTab.Controls.Add(textBox);
                resTabs.TabPages.Add(textTab);
                resTabs.SelectedTab = textTab;
                Program.MainForm.Invoke((MethodInvoker)(
                    () => bv.SetBytes(databytes)
                ));
                return tab;
            }
        }
    }
}
