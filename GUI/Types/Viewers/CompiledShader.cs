using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
using Vortice.SpirvCross;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;
using VrfPackage = SteamDatabase.ValvePak.Package;

namespace GUI.Types.Viewers
{
    class CompiledShader : IDisposable, IViewer
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

            public TabPage CreateShaderFileTab(ShaderCollection collection, VcsProgramType shaderFileType,
                bool showHelpText = false, string tabName = null)
            {
                var tab = new TabPage(tabName ?? shaderFileType.ToString());
                var shaderRichTextBox = new ShaderRichTextBox(shaderFileType, this, collection);
                tab.Controls.Add(shaderRichTextBox);

                shaderRichTextBox.MouseEnter += new EventHandler(MouseEnterHandler);

                if (showHelpText)
                {
                    var helpText = "[ctrl+click to open links in a new tab, ESC or right-click on tabs to close]\n\n";
                    shaderRichTextBox.Text = $"{helpText}{shaderRichTextBox.Text}";
                }

                Controls.Add(tab);
                return tab;
            }

            public bool TryAddUniqueTab(ShaderCollection collection, VcsProgramType shaderFileType, out TabPage newShaderTab)
            {
                var tabName = shaderFileType.ToString();
                if (TabPages.Cast<TabPage>().FirstOrDefault(t => t.Text == tabName) is TabPage existing)
                {
                    newShaderTab = existing;
                    return false;
                }

                newShaderTab = CreateShaderFileTab(collection, shaderFileType);
                return true;
            }
        }

        private ShaderTabControl tabControl;
        private ShaderCollection shaderCollection;

        public static string SpvToHlsl(VulkanSource v, ShaderCollection c, VcsProgramType s, long z, long d)
            => AttemptSpirvReflection(v, c, s, z, d, Backend.HLSL);

        public TabPage Create(VrfGuiContext vrfGuiContext, Stream stream)
        {
            shaderCollection = GetShaderCollection(vrfGuiContext.FileName, vrfGuiContext.CurrentPackage);

            SetShaderTabControl();

            var tab = new TabPage();
            tab.Controls.Add(tabControl);

            var filename = Path.GetFileName(vrfGuiContext.FileName);
            var leadFileType = ComputeVCSFileName(filename).ProgramType;

            tabControl.CreateShaderFileTab(shaderCollection, leadFileType);

            if (shaderCollection.Features is null)
            {
                return tab; // ShaderExtract cannot continue without the features file present
            }

            try
            {
                var extract = new ShaderExtract(shaderCollection)
                {
                    SpirvCompiler = SpvToHlsl,
                };

                IViewer.AddContentTab<Func<string>>(tabControl, extract.GetVfxFileName(), extract.ToVFX, preSelect: true);
            }
            catch (Exception e)
            {
                IViewer.AddContentTab(tabControl, $"{nameof(ShaderExtract)} Error", e.ToString(), preSelect: false);
            }

            return tab;
        }

        public ShaderTabControl SetResourceBlockTabControl(TabPage blockTab, ShaderCollection shaders)
        {
            shaderCollection = shaders;
            SetShaderTabControl();
            blockTab.Controls.Add(tabControl);

            return tabControl;
        }

        void SetShaderTabControl()
        {
            tabControl = new ShaderTabControl
            {
                Dock = DockStyle.Fill,
            };

            tabControl.MouseClick += new MouseEventHandler(OnTabClick);
        }

        private static ShaderCollection GetShaderCollection(string targetFilename, VrfPackage vrfPackage)
        {
            ShaderCollection shaderCollection = [];
            if (vrfPackage != null)
            {
                // search the package
                var filename = Path.GetFileName(targetFilename);
                var vcsCollectionName = filename[..filename.LastIndexOf('_')]; // in the form water_dota_pcgl_40
                var vcsEntries = vrfPackage.Entries["vcs"];

                foreach (var vcsEntry in vcsEntries)
                {
                    // vcsEntry.FileName is in the form bloom_dota_pcgl_30_ps (without vcs extension)
                    if (vcsEntry.FileName.StartsWith(vcsCollectionName, StringComparison.InvariantCulture))
                    {
                        var programType = ComputeVCSFileName($"{vcsEntry.FileName}.vcs").ProgramType;

                        vrfPackage.ReadEntry(vcsEntry, out var shaderDatabytes);

                        var relatedShaderFile = new ShaderFile();

                        try
                        {
                            relatedShaderFile.Read($"{vcsEntry.FileName}.vcs", new MemoryStream(shaderDatabytes));
                            shaderCollection.Add(relatedShaderFile);
                            relatedShaderFile = null;
                        }
                        finally
                        {
                            relatedShaderFile?.Dispose();
                        }
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
                        var programType = ComputeVCSFileName(vcsFile).ProgramType;
                        var relatedShaderFile = new ShaderFile();

                        try
                        {
                            relatedShaderFile.Read(vcsFile);
                            shaderCollection.Add(relatedShaderFile);
                            relatedShaderFile = null;
                        }
                        finally
                        {
                            relatedShaderFile?.Dispose();
                        }
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

                if (shaderCollection != null)
                {
                    shaderCollection.Dispose();
                    shaderCollection = null;
                }
            }
        }
        public class ShaderRichTextBox : RichTextBox
        {
#pragma warning disable CA2213
            private readonly ShaderFile shaderFile;
#pragma warning restore CA2213
            private readonly ShaderCollection shaderCollection;
            private readonly ShaderTabControl tabControl;
            private readonly List<string> relatedFiles = [];
            public ShaderRichTextBox(VcsProgramType leadProgramType, ShaderTabControl tabControl,
                ShaderCollection shaderCollection, bool byteVersion = false) : base()
            {
                this.shaderCollection = shaderCollection;
                this.tabControl = tabControl;
                shaderFile = shaderCollection.Get(leadProgramType);
                foreach (var shader in shaderCollection)
                {
                    relatedFiles.Add(Path.GetFileName(shader.FilenamePath));
                }
                using var buffer = new StringWriter(CultureInfo.InvariantCulture);
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
                // linkTokens[0] is sometimes a zframe id, otherwise a VcsProgramType should be defined
                if (linkTokens[0].Split("_").Length >= 4)
                {
                    var programType = ComputeVCSFileName(linkTokens[0]).ProgramType;
                    var shaderFile = shaderCollection.Get(programType);
                    TabPage newShaderTab = null;
                    if (linkTokens.Length > 1 && linkTokens[1].Equals("bytes", StringComparison.Ordinal))
                    {
                        newShaderTab = new TabPage($"{programType} bytes");
                        var shaderRichTextBox = new ShaderRichTextBox(programType, tabControl, shaderCollection, byteVersion: true);
                        shaderRichTextBox.MouseEnter += new EventHandler(MouseEnterHandler);
                        newShaderTab.Controls.Add(shaderRichTextBox);
                        tabControl.Controls.Add(newShaderTab);
                    }
                    else
                    {
                        if (!tabControl.TryAddUniqueTab(shaderCollection, programType, out newShaderTab))
                        {
                            tabControl.SelectedTab = newShaderTab;
                            return;
                        }
                    }

                    if (!ModifierKeys.HasFlag(Keys.Control))
                    {
                        tabControl.SelectedTab = newShaderTab;
                    }
                    return;
                }
                var zframeId = Convert.ToInt64(linkText, 16);
                var zframeTab = new TabPage($"{shaderFile.FilenamePath.Split('_')[^1][..^4]}[{zframeId:x}]");
                var zframeRichTextBox = new ZFrameRichTextBox(tabControl, shaderFile, shaderCollection, zframeId);
                zframeRichTextBox.MouseEnter += new EventHandler(MouseEnterHandler);
                zframeTab.Controls.Add(zframeRichTextBox);
                tabControl.Controls.Add(zframeTab);

                if (!ModifierKeys.HasFlag(Keys.Control))
                {
                    tabControl.SelectedTab = zframeTab;
                }
            }
        }

        public class ZFrameRichTextBox : RichTextBox, IDisposable
        {
            private readonly TabControl tabControl;
            private readonly ShaderCollection shaderCollection;
            private readonly ShaderFile shaderFile;
            private ZFrameFile zframeFile;

            public ZFrameRichTextBox(TabControl tabControl, ShaderFile shaderFile, ShaderCollection shaderCollection,
                long zframeId, bool byteVersion = false) : base()
            {
                this.tabControl = tabControl;
                this.shaderFile = shaderFile;
                this.shaderCollection = shaderCollection;
                using var buffer = new StringWriter(CultureInfo.InvariantCulture);
                zframeFile = shaderFile.GetZFrameFile(zframeId, outputWriter: buffer.Write);
                if (byteVersion)
                {
                    try
                    {
                        zframeFile.PrintByteDetail();
                    }
                    catch (Exception e)
                    {
                        zframeFile.DataReader.OutputWrite(e.ToString());
                    }
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
                    var zframeRichTextBox = new ZFrameRichTextBox(tabControl, shaderFile, shaderCollection, zframeId, byteVersion: true);
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

                var gpuSourceTab = CreateDecompiledTabPage(shaderCollection, shaderFile, zframeFile, gpuSourceId, gpuSourceTabTitle);

                tabControl.Controls.Add(gpuSourceTab);
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    tabControl.SelectedTab = gpuSourceTab;
                }
            }
        }

        public static TabPage CreateDecompiledTabPage(ShaderCollection shaderCollection, ShaderFile shaderFile, ZFrameFile zframeFile, int gpuSourceId, string gpuSourceTabTitle)
        {
            TabPage gpuSourceTab = null;
            var gpuSource = zframeFile.GpuSources[gpuSourceId];

            switch (gpuSource)
            {
                case GlslSource:
                    {
                        gpuSourceTab = new TabPage(gpuSourceTabTitle);
                        var gpuSourceGlslText = new CodeTextBox(Encoding.UTF8.GetString(gpuSource.Sourcebytes));
                        gpuSourceTab.Controls.Add(gpuSourceGlslText);
                        break;
                    }

                case DxbcSource:
                case DxilSource:
                    {
                        gpuSourceTab = new TabPage
                        {
                            Text = gpuSourceTabTitle
                        };

                        var sourceBv = new System.ComponentModel.Design.ByteViewer
                        {
                            Dock = DockStyle.Fill,
                        };
                        gpuSourceTab.Controls.Add(sourceBv);

                        Program.MainForm.Invoke((MethodInvoker)(() =>
                        {
                            sourceBv.SetBytes(gpuSource.Sourcebytes);
                        }));

                        break;
                    }

                case VulkanSource vulkanSource:
                    {
                        gpuSourceTab = new TabPage
                        {
                            Text = gpuSourceTabTitle
                        };
                        var resTabs = new ThemedTabControl
                        {
                            Dock = DockStyle.Fill,
                        };
                        gpuSourceTab.Controls.Add(resTabs);

                        // source
                        var sourceBvTab = new TabPage("Source");
                        var sourceBv = new System.ComponentModel.Design.ByteViewer
                        {
                            Dock = DockStyle.Fill,
                        };
                        sourceBvTab.Controls.Add(sourceBv);
                        resTabs.TabPages.Add(sourceBvTab);

                        // metadata
                        var metadataBvTab = new TabPage("Metadata");
                        var metadataBv = new System.ComponentModel.Design.ByteViewer
                        {
                            Dock = DockStyle.Fill,
                        };
                        metadataBvTab.Controls.Add(metadataBv);
                        resTabs.TabPages.Add(metadataBvTab);

                        // text
                        var reflectedSource = AttemptSpirvReflection(vulkanSource, shaderCollection, shaderFile.VcsProgramType,
                            zframeFile.ZframeId, 0, Backend.GLSL);

                        var textTab = new TabPage("SPIR-V");
                        var textBox = new CodeTextBox(reflectedSource);
                        textTab.Controls.Add(textBox);
                        resTabs.TabPages.Add(textTab);
                        resTabs.SelectedTab = textTab;

                        if (!vulkanSource.IsEmpty())
                        {
                            Program.MainForm.Invoke((MethodInvoker)(() =>
                            {
                                sourceBv.SetBytes(vulkanSource.Bytecode.ToArray());
                                metadataBv.SetBytes(vulkanSource.Metadata.ToArray());
                            }));
                        }

                        break;
                    }

                default:
                    throw new InvalidDataException($"Unimplemented GPU source type {gpuSource.GetType()}");
            }

            return gpuSourceTab;
        }

#pragma warning disable IDE0060 // Remove unused parameter - TODO: these parameters are used in the `spirvcross` branch
        public static string AttemptSpirvReflection(VulkanSource vulkanSource, ShaderCollection vcsFiles, VcsProgramType stage,
            long zFrameId, long dynamicId, Backend backend)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            SpirvCrossApi.spvc_context_create(out var context).CheckResult();

            using var buffer = new StringWriter(CultureInfo.InvariantCulture);

            try
            {
                SpirvCrossApi.spvc_context_parse_spirv(context, vulkanSource.Bytecode, out var parsedIr).CheckResult();
                SpirvCrossApi.spvc_context_create_compiler(context, backend, parsedIr, CaptureMode.TakeOwnership, out var compiler).CheckResult();

                SpirvCrossApi.spvc_compiler_create_compiler_options(compiler, out var options).CheckResult();

                if (backend == Backend.GLSL)
                {
                    SpirvCrossApi.spvc_compiler_options_set_uint(options, CompilerOption.GLSLVersion, 460);
                    SpirvCrossApi.spvc_compiler_options_set_bool(options, CompilerOption.GLSLES, SpirvCrossApi.SPVC_FALSE);
                    SpirvCrossApi.spvc_compiler_options_set_bool(options, CompilerOption.GLSLVulkanSemantics, SpirvCrossApi.SPVC_TRUE);
                    SpirvCrossApi.spvc_compiler_options_set_bool(options, CompilerOption.GLSLEmitUniformBufferAsPlainUniforms, SpirvCrossApi.SPVC_TRUE);
                }
                else if (backend == Backend.HLSL)
                {
                    SpirvCrossApi.spvc_compiler_options_set_uint(options, CompilerOption.HLSLShaderModel, 61);
                }

                SpirvCrossApi.spvc_compiler_install_compiler_options(compiler, options);

                SpirvCrossApi.spvc_compiler_compile(compiler, out var code).CheckResult();

                buffer.WriteLine($"// SPIR-V source ({vulkanSource.MetaDataSize}), {backend} reflection with SPIRV-Cross by KhronosGroup");
                buffer.WriteLine($"// {ValveResourceFormat.Utils.StringToken.VRF_GENERATOR}");
                buffer.WriteLine();
                buffer.WriteLine(code.ReplaceLineEndings());
            }
            catch (Exception e)
            {
                buffer.WriteLine("/*");
                buffer.WriteLine($"SPIR-V reflection failed: {e.Message}");

                var lastError = SpirvCrossApi.spvc_context_get_last_error_string(context);

                if (!string.IsNullOrEmpty(lastError))
                {
                    buffer.WriteLine();
                    buffer.WriteLine(lastError);
                }

                buffer.WriteLine("*/");
            }
            finally
            {
                SpirvCrossApi.spvc_context_release_allocations(context);
                SpirvCrossApi.spvc_context_destroy(context);
            }

            return buffer.ToString();
        }
    }
}
