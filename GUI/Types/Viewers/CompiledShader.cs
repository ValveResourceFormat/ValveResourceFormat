using System.Diagnostics;
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
using Vortice.SPIRV;
using Vortice.SpirvCross;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;
using VrfPackage = SteamDatabase.ValvePak.Package;

#nullable disable

namespace GUI.Types.Viewers
{
    class CompiledShader : IDisposable, IViewer
    {
        public static bool IsAccepted(uint magic)
        {
            return magic == VfxProgramData.MAGIC;
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

        public static string SpvToHlsl(VfxShaderFileVulkan v, ShaderCollection c, VcsProgramType s, long z, long d)
            => AttemptSpirvReflection(v, c, s, z, (int)d, Backend.HLSL);

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

                IViewer.AddContentTab<Func<string>>(tabControl, extract.GetVfxFileName(), extract.ToVFX, preSelect: true, CodeTextBox.HighlightLanguage.Shaders);
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

                        var relatedShaderFile = new VfxProgramData();

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
                        var relatedShaderFile = new VfxProgramData();

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
            private readonly VfxProgramData shaderFile;
#pragma warning restore CA2213
            private readonly ShaderCollection shaderCollection;
            private readonly ShaderTabControl tabControl;
            private readonly List<string> relatedFiles = [];
            public ShaderRichTextBox(VcsProgramType leadProgramType, ShaderTabControl tabControl, ShaderCollection shaderCollection) : base()
            {
                this.shaderCollection = shaderCollection;
                this.tabControl = tabControl;
                shaderFile = shaderCollection.Get(leadProgramType);
                foreach (var shader in shaderCollection)
                {
                    relatedFiles.Add(Path.GetFileName(shader.FilenamePath));
                }
                using var buffer = new StringWriter(CultureInfo.InvariantCulture);
                shaderFile.PrintSummary(buffer.Write, showRichTextBoxLinks: true, relatedfiles: relatedFiles);
                Font = CodeTextBox.GetMonospaceFont();
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

                    if (!tabControl.TryAddUniqueTab(shaderCollection, programType, out var newShaderTab))
                    {
                        tabControl.SelectedTab = newShaderTab;
                        return;
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
            private readonly VfxProgramData shaderFile;
            private VfxStaticComboData zframeFile;

            public ZFrameRichTextBox(TabControl tabControl, VfxProgramData shaderFile, ShaderCollection shaderCollection, long zframeId) : base()
            {
                this.tabControl = tabControl;
                this.shaderFile = shaderFile;
                this.shaderCollection = shaderCollection;
                using var buffer = new StringWriter(CultureInfo.InvariantCulture);
                zframeFile = shaderFile.GetZFrameFile(zframeId);
                var zframeSummary = new PrintZFrameSummary(shaderFile, zframeFile, buffer.Write, true);
                Font = CodeTextBox.GetMonospaceFont();
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
                Debug.Assert(linkTokens.Length > 1);
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

        public static TabPage CreateDecompiledTabPage(ShaderCollection shaderCollection, VfxProgramData shaderFile, VfxStaticComboData zframeFile, int gpuSourceId, string gpuSourceTabTitle)
        {
            TabPage gpuSourceTab = null;
            var gpuSource = zframeFile.GpuSources[gpuSourceId];

            switch (gpuSource)
            {
                case VfxShaderFileGL:
                    {
                        gpuSourceTab = new TabPage(gpuSourceTabTitle);
                        var gpuSourceGlslText = new CodeTextBox(Encoding.UTF8.GetString(gpuSource.Bytecode), CodeTextBox.HighlightLanguage.Shaders);
                        gpuSourceTab.Controls.Add(gpuSourceGlslText);
                        break;
                    }

                case VfxShaderFileDXBC:
                case VfxShaderFileDXIL:
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
                            sourceBv.SetBytes(gpuSource.Bytecode);
                        }));

                        break;
                    }

                case VfxShaderFileVulkan vulkanSource:
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
                        var textBox = new CodeTextBox(reflectedSource, CodeTextBox.HighlightLanguage.Shaders);
                        textTab.Controls.Add(textBox);
                        resTabs.TabPages.Add(textTab);
                        resTabs.SelectedTab = textTab;

                        if (!vulkanSource.IsEmpty())
                        {
                            Program.MainForm.Invoke((MethodInvoker)(() =>
                            {
                                sourceBv.SetBytes(vulkanSource.Bytecode);
                                //metadataBv.SetBytes(vulkanSource.Metadata.ToArray());
                            }));
                        }

                        break;
                    }

                default:
                    throw new InvalidDataException($"Unimplemented GPU source type {gpuSource.GetType()}");
            }

            return gpuSourceTab;
        }

        public static string AttemptSpirvReflection(VfxShaderFileVulkan vulkanSource, ShaderCollection vcsFiles, VcsProgramType stage,
            long zFrameId, int dynamicId, Backend backend, bool lastRetry = false)
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

                // name variables based on reflection data from VCS
                {
                    SpirvCrossApi.spvc_compiler_create_shader_resources(compiler, out var resources).CheckResult();

                    Rename(compiler, resources, ResourceType.SeparateImage, vcsFiles, stage, zFrameId, dynamicId);
                    Rename(compiler, resources, ResourceType.SeparateSamplers, vcsFiles, stage, zFrameId, dynamicId);

                    Rename(compiler, resources, ResourceType.StorageBuffer, vcsFiles, stage, zFrameId, dynamicId);
                    Rename(compiler, resources, ResourceType.UniformBuffer, vcsFiles, stage, zFrameId, dynamicId);

                    Rename(compiler, resources, ResourceType.StageInput, vcsFiles, stage, zFrameId, dynamicId);
                }

                SpirvCrossApi.spvc_compiler_compile(compiler, out var code).CheckResult();

                buffer.WriteLine($"// SPIR-V source ({vulkanSource.BytecodeSize}), {backend} reflection with SPIRV-Cross by KhronosGroup");
                buffer.WriteLine($"// {StringToken.VRF_GENERATOR}");
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

                if (!lastRetry)
                {
                    var retryBackend = backend == Backend.GLSL ? Backend.HLSL : Backend.GLSL;
                    buffer.WriteLine();
                    buffer.WriteLine($"Re-attempting reflection with the {retryBackend} backend.");
                    buffer.WriteLine("*/");
                    buffer.WriteLine();

                    buffer.Write(AttemptSpirvReflection(vulkanSource, vcsFiles, stage, zFrameId, dynamicId, retryBackend, lastRetry: true));
                }
                else
                {
                    buffer.WriteLine("*/");
                }
            }
            finally
            {
                SpirvCrossApi.spvc_context_release_allocations(context);
                SpirvCrossApi.spvc_context_destroy(context);
            }

            return buffer.ToString();
        }

        private static unsafe void Rename(spvc_compiler compiler, spvc_resources resources, ResourceType resourceType,
            ShaderCollection vcsFiles, VcsProgramType stage, long zFrameId, int dynamicId)
        {
            var shader = vcsFiles.Get(stage);
            Span<spvc_buffer_range> bufferRanges = stackalloc spvc_buffer_range[256];
            // var leadingWriteSequence = shader.ZFrameCache.Get(zFrameId).DataBlocks[dynamicId];

            var staticComboData = shader.ZFrameCache.Get(zFrameId);

            var dynamicBlockIndex = staticComboData.RenderStateInfos[dynamicId].BlockIdRef;
            var writeSequence = staticComboData.DataBlocks[(int)dynamicBlockIndex];

            SpirvCrossApi.spvc_resources_get_resource_list_for_type(resources, resourceType, out var outResources, out var outResourceCount).CheckResult();
            for (nuint i = 0; i < outResourceCount; i++)
            {
                var resource = outResources[i];

                var location = (int)SpirvCrossApi.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.Location);
                var index = SpirvCrossApi.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.Index);
                var binding = SpirvCrossApi.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.Binding);
                var set = SpirvCrossApi.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.DescriptorSet);

                var vfxType = resource.base_type_id switch
                {
                    406 => Vfx.Type.Sampler2D,
                    407 => Vfx.Type.Sampler3D,
                    408 => Vfx.Type.SamplerCube,
                    472 => Vfx.Type.SamplerCubeArray,
                    _ => Vfx.Type.Void,
                };

                var isVertexShader = stage is VcsProgramType.VertexShader;
                var uniformBufferBindingOffset = isVertexShader ? 14u : 0;
                var uniformBufferBinding = binding - uniformBufferBindingOffset;

                var isGlobalsBuffer = uniformBufferBinding == 0 && set == 0;

                var name = resourceType switch
                {
                    ResourceType.SeparateImage => GetNameForTexture(shader, writeSequence, binding, vfxType),
                    ResourceType.SeparateSamplers => GetNameForSampler(shader, writeSequence, binding),
                    ResourceType.StorageBuffer or ResourceType.StorageImage => GetNameForStorageBuffer(shader, writeSequence, binding),
                    ResourceType.UniformBuffer => isGlobalsBuffer ? "_Globals_" : GetNameForUniformBuffer(shader, writeSequence, uniformBufferBinding, set),
                    ResourceType.StageInput => isVertexShader
                        ? GetVsAttributeName(shader, shader.VSInputSignatures[staticComboData.VShaderInputs[dynamicBlockIndex]], location)
                        : string.Empty,
                    _ => string.Empty,
                };

                if (string.IsNullOrEmpty(name))
                {
                    Console.WriteLine($"Unhandled resource type {resourceType}");
                    continue;
                }

                if (resourceType is ResourceType.SeparateImage && vfxType is Vfx.Type.Void)
                {
                    name = $"{name}_unexpectedTypeId{resource.base_type_id}_{resource.type_id}";
                }

                SpirvCrossApi.spvc_compiler_set_name(compiler, resource.id, name);

                if (resourceType is ResourceType.UniformBuffer)
                {
                    // get buffer members
                    nuint bufferRangeCount = 0;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

                    SpirvCrossApi.spvc_compiler_get_active_buffer_ranges(compiler, resource.id, (spvc_buffer_range**)&bufferRanges, &bufferRangeCount).CheckResult();
#pragma warning restore CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type


                    for (var j = 0; j < (int)bufferRangeCount; j++)
                    {
                        var bufferRange = bufferRanges[j];

                        var memberName = isGlobalsBuffer
                            ? GetGlobalBufferMemberName(shader, writeSequence, offset: (int)bufferRange.offset / 4)
                            : GetBufferMemberName(shader, name, offset: (int)bufferRange.offset / 4);

                        if (string.IsNullOrEmpty(memberName))
                        {
                            continue;
                        }

                        fixed (byte* memberNameBytes = memberName.GetUtf8Span())
                        {
                            SpirvCrossApi.spvc_compiler_set_member_name(compiler, resource.base_type_id, bufferRange.index, memberNameBytes);
                        }
                    }
                }
            }
        }

        const int TextureStartingPoint = 90;
        const int TextureIndexStartingPoint = 30;

        private static string GetNameForTexture(VfxProgramData shader, VfxVariableIndexArray writeSequence, uint image_binding, Vfx.Type vfxType)
        {
            var semgent1Params = writeSequence.Segment1
                .Select<VfxVariableIndexData, (VfxVariableIndexData Field, VfxVariableDescription Param)>(f => (f, shader.VariableDescriptions[f.VariableIndex]));

            foreach (var field in writeSequence.Segment1)
            {
                var param = shader.VariableDescriptions[field.VariableIndex];

                if (param.ParamType is ParameterType.SamplerState)
                {
                    // Arg4 = 16
                    continue;
                }

                if (param.ParamType is not ParameterType.Texture)
                {
                    continue;
                }

                var isBindlessTextureArray = param.Arg4 == 152;
                Debug.Assert(param.Arg4 is 152 or 24);

                var startingPoint = isBindlessTextureArray ? TextureIndexStartingPoint : TextureStartingPoint;

                if (param.VfxType is Vfx.Type.Sampler1D
                    or Vfx.Type.Sampler2D
                    or Vfx.Type.Sampler3D
                    or Vfx.Type.SamplerCube
                    or Vfx.Type.SamplerCubeArray
                    or Vfx.Type.Sampler2DArray
                    or Vfx.Type.Sampler1DArray
                    or Vfx.Type.Sampler3DArray)
                {

                    if (isBindlessTextureArray && vfxType != param.VfxType)
                    {
                        continue;
                    }

                    if (field.Dest == image_binding - startingPoint)
                    {
                        return param.Name;
                    }

                    continue;
                }
            }

            return "undetermined";
        }

        const int SamplerStartingPoint = 42;
        public static string GetNameForSampler(VfxProgramData shader, VfxVariableIndexArray writeSequence, uint sampler_binding)
        {
            var semgent1Params = writeSequence.Segment1
                .Select<VfxVariableIndexData, (VfxVariableIndexData Field, VfxVariableDescription Param)>(f => (f, shader.VariableDescriptions[f.VariableIndex]));

            var samplerSettings = string.Empty;

            foreach (var field in writeSequence.Segment1)
            {
                var param = shader.VariableDescriptions[field.VariableIndex];

                if (param.ParamType is not ParameterType.SamplerState)
                {
                    continue;
                }

                if (field.Dest == sampler_binding - SamplerStartingPoint)
                {
                    var value = param.HasDynamicExpression ? /*param.DynExp*/ "dynamic" : param.IntDefs[0].ToString(CultureInfo.InvariantCulture);
                    samplerSettings += $"{param.Name}_{value}__";
                }
            }

            return samplerSettings.Length > 0 ? samplerSettings[..^2] : "undetermined";
        }

        const int StorageBufferStartingPoint = 30;
        public static string GetNameForStorageBuffer(VfxProgramData shader, VfxVariableIndexArray writeSequence, uint buffer_binding)
        {
            var semgent1Params = writeSequence.Segment1
                .Select<VfxVariableIndexData, (VfxVariableIndexData Field, VfxVariableDescription Param)>(f => (f, shader.VariableDescriptions[f.VariableIndex]));

            foreach (var field in writeSequence.Segment1)
            {
                var param = shader.VariableDescriptions[field.VariableIndex];

                if (param.VfxType is < Vfx.Type.StructuredBuffer or > Vfx.Type.RWStructuredBufferWithCounter)
                {
                    continue;
                }

                if (field.Dest == buffer_binding - StorageBufferStartingPoint)
                {
                    return param.Name;
                }
            }

            return "undetermined";
        }

        private static string GetNameForUniformBuffer(VfxProgramData shader, VfxVariableIndexArray writeSequence, uint binding, uint set)
        {
            return writeSequence.Segment1
                .Select<VfxVariableIndexData, (VfxVariableIndexData Field, VfxVariableDescription Param)>(f => (f, shader.VariableDescriptions[f.VariableIndex]))
                .Where(fp => fp.Param.VfxType is Vfx.Type.Cbuffer)
                .FirstOrDefault(fp => fp.Field.Dest == binding).Param?.Name ?? "undetermined";
        }

        private static string GetGlobalBufferMemberName(VfxProgramData shader, VfxVariableIndexArray writeSequence, int offset)
        {
            var globalBufferParameters = writeSequence.Globals
                .Select<VfxVariableIndexData, (VfxVariableIndexData Field, VfxVariableDescription Param)>(f => (f, shader.VariableDescriptions[f.VariableIndex]))
                .ToList();

            return globalBufferParameters.FirstOrDefault(fp => fp.Field.Dest == offset).Param?.Name ?? string.Empty;
        }

        // by offset
        // https://github.com/KhronosGroup/SPIRV-Cross/blob/f349c91274b91c1a7c173f2df70ec53080076191/spirv_hlsl.cpp#L2616
        private static string GetBufferMemberName(VfxProgramData shader, string bufferName, int index = -1, int offset = -1)
        {
            var bufferParams = shader.ExtConstantBufferDescriptions.FirstOrDefault(buffer => buffer.Name == bufferName)?.BufferParams;

            if (bufferParams is null)
            {
                return string.Empty;
            }

            if (index != -1)
            {
                return bufferParams[index].Name;
            }
            else if (offset != -1)
            {
                return bufferParams.First(p => p.Offset == offset).Name;
            }
            else
            {
                return "undetermined";
            }
        }

        public static string GetVsAttributeName(VfxProgramData shader, VsInputSignatureElement inputSignatureElement, int attributeLocation)
        {
            return inputSignatureElement.SymbolsDefinition[attributeLocation].Name;
        }
    }
}
