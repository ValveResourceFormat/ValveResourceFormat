using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
using Vortice.SPIRV;
using Vortice.SpirvCross;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;
using SpirvResourceType = Vortice.SpirvCross.ResourceType;

#nullable disable

namespace GUI.Types.Viewers
{
    class CompiledShader : IDisposable, IViewer
    {
        private TextControl control;
        private TreeView fileListView;

        public static string SpvToHlsl(VfxShaderFileVulkan file) => AttemptSpirvReflection(file, Backend.HLSL);

        public static bool IsAccepted(uint magic)
        {
            return magic == VfxProgramData.MAGIC;
        }

        public CompiledShader()
        {
            fileListView = new TreeView
            {
                FullRowSelect = true,
                HideSelection = false,
                ShowRootLines = false,
                ShowNodeToolTips = true,
                Dock = DockStyle.Fill,
                ImageList = MainForm.ImageList,
            };
            fileListView.NodeMouseClick += OnNodeMouseClick;

            control = new TextControl(CodeTextBox.HighlightLanguage.Shaders);
            control.AddControl(fileListView);
        }

        public TabPage Create(VrfGuiContext vrfGuiContext, Stream stream)
        {
            stream?.Dispose(); // Creating shader collection doesn't actually use the provided stream which is kind of a waste

            var filename = Path.GetFileName(vrfGuiContext.FileName);
            var leadProgramType = ComputeVCSFileName(filename).ProgramType;
            var vcsCollectionName = filename.AsSpan(0, filename.LastIndexOf('_')); // in the form water_dota_pcgl_40

            var shaderCollection = ShaderCollection.GetShaderCollection(vrfGuiContext.FileName, vrfGuiContext.CurrentPackage);

            return Create(shaderCollection, vcsCollectionName, leadProgramType);
        }

        public TabPage Create(ShaderCollection shaderCollection, ReadOnlySpan<char> vcsCollectionName, VcsProgramType leadProgramType, IDictionary<string, byte> leadFeatureParams = null)
        {
            var tab = new TabPage();
            tab.Controls.Add(control);

            var vfxImage = MainForm.GetImageIndexForExtension("_folder");
            var programImage = MainForm.GetImageIndexForExtension("vcs");
            var comboImage = MainForm.GetImageIndexForExtension("rman");

            var materialCollectionIndex = 0;
            var collectionNode = new TreeNode($"{vcsCollectionName}.vfx")
            {
                ImageIndex = vfxImage,
                SelectedImageIndex = vfxImage,
            };
            fileListView.Nodes.Add(collectionNode);

            List<string> sfNamesAbbrev = [];
            List<string> sfNames = [];

            foreach (var program in shaderCollection.OrderBy(static x => x.VcsProgramType))
            {
                var truncatedProgramName = program.FilenamePath.AsSpan();

                if (truncatedProgramName.StartsWith(vcsCollectionName))
                {
                    truncatedProgramName = truncatedProgramName[(vcsCollectionName.Length + 1)..];
                }

                var programNode = new TreeNode(truncatedProgramName.ToString())
                {
                    Tag = program,
                    ImageIndex = programImage,
                    SelectedImageIndex = programImage,
                };
                collectionNode.Nodes.Add(programNode);

                if (program.StaticComboEntries.Count > 0)
                {
                    var configGen = new ConfigMappingParams(program);
                    var leadStaticComboId = -1L; // Shader file to be displayed for a particular material

                    if (leadFeatureParams != null)
                    {
                        leadStaticComboId = ShaderDataProvider.GetStaticConfiguration_ForFeatureState(shaderCollection.Features, program, leadFeatureParams).StaticComboId;
                    }

                    foreach (var staticComboEntry in program.StaticComboEntries)
                    {
                        var config = configGen.GetConfigState(staticComboEntry.Key);

                        sfNames.Clear();
                        sfNamesAbbrev.Clear();

                        for (var i = 0; i < program.StaticComboArray.Length; i++)
                        {
                            if (config[i] == 0)
                            {
                                continue;
                            }

                            var sfBlock = program.StaticComboArray[i];
                            var sfShortName = ShortenShaderParam(sfBlock.Name).ToLowerInvariant();

                            if (config[i] > 1)
                            {
                                sfNames.Add($"{sfBlock.Name}={config[i]}");
                                sfNamesAbbrev.Add($"{sfShortName}={config[i]}");
                            }
                            else
                            {
                                sfNames.Add(sfBlock.Name);
                                sfNamesAbbrev.Add(sfShortName);
                            }
                        }

                        var variantsAbbrev = sfNamesAbbrev.Count > 0 ? $" ({string.Join(", ", sfNamesAbbrev)})" : string.Empty;
                        var variantsTooltip = string.Join(Environment.NewLine, sfNames);

                        var comboNode = new TreeNode($"{staticComboEntry.Key:x08}{variantsAbbrev}")
                        {
                            ToolTipText = variantsTooltip,
                            Tag = staticComboEntry.Value,
                            ImageIndex = comboImage,
                            SelectedImageIndex = comboImage,
                        };
                        programNode.Nodes.Add(comboNode);

                        if (staticComboEntry.Key == leadStaticComboId)
                        {
                            // When viewing from a material, unserialize the correct static combo straight away
                            var combo = staticComboEntry.Value.Unserialize();
                            comboNode.Tag = combo;
                            CreateStaticComboNodes(combo, comboNode);

                            var shaderFile = combo.ShaderFiles[0];

                            if (shaderFile.Bytecode.Length > 0)
                            {
                                var matImage = MainForm.GetImageIndexForExtension("vmat");
                                var matNode = new TreeNode($"Material {program.VcsProgramType}{variantsAbbrev}")
                                {
                                    ToolTipText = variantsTooltip,
                                    Tag = combo.ShaderFiles[0],
                                    ImageIndex = matImage,
                                    SelectedImageIndex = matImage,
                                };
                                collectionNode.Nodes.Insert(materialCollectionIndex++, matNode);
                            }
                        }
                    }

                    if (program.VcsProgramType == leadProgramType)
                    {
                        programNode.Expand();
                    }
                }
            }

            collectionNode.Expand();

            // ShaderExtract cannot continue without the features file present
            if (shaderCollection.Features is not null)
            {
                var shaderExtract = new ShaderExtract(shaderCollection)
                {
                    SpirvCompiler = SpvToHlsl,
                };

                collectionNode.Tag = shaderExtract;
                DisplayExtractedVfx(shaderExtract);
            }

            return tab;
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
                if (fileListView != null)
                {
                    fileListView.Dispose();
                    fileListView = null;
                }

                if (control != null)
                {
                    control.Dispose();
                    control = null;
                }
            }
        }

        private void OnNodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node.Tag is ShaderExtract shaderExtract)
            {
                DisplayExtractedVfx(shaderExtract);
            }
            else if (e.Node.Tag is VfxProgramData program)
            {
                using var output = new IndentedTextWriter();
                program.PrintSummary(output);
                control.TextBox.Text = output.ToString();
            }
            else if (e.Node.Tag is VfxStaticComboVcsEntry comboEntry)
            {
                var combo = comboEntry.Unserialize();
                e.Node.Tag = combo; // Replace the entry with unserialized combo data

                DisplayStaticCombo(combo);

                fileListView.BeginUpdate();
                CreateStaticComboNodes(combo, e.Node);
                fileListView.EndUpdate();
            }
            else if (e.Node.Tag is VfxStaticComboData combo)
            {
                DisplayStaticCombo(combo);
            }
            else if (e.Node.Tag is VfxShaderFile shaderFile)
            {
                // TODO: Perhaps these tabs can also be removed, but currently required for the byte viewer
                var shaderSource = GetDecompiledFile(shaderFile);

                control.TextBox.Text = shaderSource.Source ?? string.Empty;

                /*
                TODO: We need to display the bytecode somehow

                tabs = new ThemedTabControl
                {
                    Dock = DockStyle.Fill,
                };

                // source
                var sourceBvTab = new TabPage("Source");
                var sourceBv = new System.ComponentModel.Design.ByteViewer
                {
                    Dock = DockStyle.Fill,
                };
                sourceBvTab.Controls.Add(sourceBv);
                resTabs.TabPages.Add(sourceBvTab);

                // text
                var reflectedSource = AttemptSpirvReflection(vulkanSource, Backend.GLSL);

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
                    }));
                }
                */
            }
        }

        private static void CreateStaticComboNodes(VfxStaticComboData combo, TreeNode treeNode)
        {
            var sourceFileImage = MainForm.GetImageIndexForExtension("ini");

            List<string> dfNamesAbbrev = [];
            List<string> dfNames = [];
            var sourceIdToRenderStateInfo = new Dictionary<int, VfxRenderStateInfo>(combo.ShaderFiles.Length);

            // We are only taking the first render state info currently
            foreach (var renderStateInfo in combo.DynamicCombos)
            {
                sourceIdToRenderStateInfo.TryAdd(renderStateInfo.ShaderFileId, renderStateInfo);
            }

            foreach (var source in combo.ShaderFiles)
            {
                if (source.Size == 0)
                {
                    continue;
                }

                var config = combo.ParentProgramData.GetDBlockConfig(sourceIdToRenderStateInfo[source.ShaderFileId].DynamicComboId);

                dfNames.Clear();
                dfNamesAbbrev.Clear();

                for (var i = 0; i < combo.ParentProgramData.DynamicComboArray.Length; i++)
                {
                    if (config[i] == 0)
                    {
                        continue;
                    }

                    var dfBlock = combo.ParentProgramData.DynamicComboArray[i];
                    var dfShortName = ShortenShaderParam(dfBlock.Name).ToLowerInvariant();

                    if (config[i] > 1)
                    {
                        dfNames.Add($"{dfBlock.Name}={config[i]}");
                        dfNamesAbbrev.Add($"{dfShortName}={config[i]}");
                    }
                    else
                    {
                        dfNames.Add(dfBlock.Name);
                        dfNamesAbbrev.Add(dfShortName);
                    }
                }

                var node = new TreeNode($"{source.ShaderFileId:X2}{(dfNamesAbbrev.Count > 0 ? $" ({string.Join(", ", dfNamesAbbrev)})" : string.Empty)}")
                {
                    ToolTipText = string.Join(Environment.NewLine, dfNames),
                    Tag = source,
                    ImageIndex = sourceFileImage,
                    SelectedImageIndex = sourceFileImage,
                };
                treeNode.Nodes.Add(node);
            }

            treeNode.Expand();
        }

        private void DisplayExtractedVfx(ShaderExtract shaderExtract)
        {
            try
            {
                control.TextBox.Text = shaderExtract.ToVFX();
            }
            catch (Exception ex)
            {
                control.TextBox.Text = ex.ToString();
            }
        }

        private void DisplayStaticCombo(VfxStaticComboData combo)
        {
            using var output = new IndentedTextWriter();
            var zframeSummary = new PrintZFrameSummary(combo, output);
            control.TextBox.Text = output.ToString();
        }

        private static (string Source, byte[] Bytecode) GetDecompiledFile(VfxShaderFile shaderFile)
        {
            switch (shaderFile)
            {
                case VfxShaderFileGL:
                    {
                        return (Encoding.UTF8.GetString(shaderFile.Bytecode), null);
                    }

                case VfxShaderFileDXBC:
                case VfxShaderFileDXIL:
                    {
                        return ("Decompiling DirectX shaders is not supported.", shaderFile.Bytecode);
                    }

                case VfxShaderFileVulkan vulkanSource:
                    {
                        var reflectedSource = AttemptSpirvReflection(vulkanSource, Backend.GLSL);

                        return (reflectedSource, shaderFile.Bytecode);
                    }

                default:
                    throw new InvalidDataException($"Unimplemented GPU source type {shaderFile.GetType()}");
            }
        }

        private static string AttemptSpirvReflection(VfxShaderFileVulkan vulkanSource, Backend backend, bool lastRetry = false)
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
                    SpirvCrossApi.spvc_compiler_options_set_uint(options, CompilerOption.HLSLUseEntryPointName, 1);
                }

                SpirvCrossApi.spvc_compiler_install_compiler_options(compiler, options);

                if (vulkanSource.ParentCombo.ParentProgramData.VcsProgramType is not VcsProgramType.RaytracingShader)
                {
                    SpirvCrossApi.spvc_compiler_create_shader_resources(compiler, out var resources).CheckResult();

                    Rename(compiler, resources, SpirvResourceType.SeparateImage, vulkanSource);
                    Rename(compiler, resources, SpirvResourceType.SeparateSamplers, vulkanSource);

                    Rename(compiler, resources, SpirvResourceType.StorageBuffer, vulkanSource);
                    Rename(compiler, resources, SpirvResourceType.UniformBuffer, vulkanSource);

                    Rename(compiler, resources, SpirvResourceType.StageInput, vulkanSource);
                    Rename(compiler, resources, SpirvResourceType.StageOutput, vulkanSource);
                }

                SpirvCrossApi.spvc_compiler_compile(compiler, out var code).CheckResult();

                if (backend == Backend.HLSL)
                {
                    if (vulkanSource.ParentCombo.ParentProgramData.VcsProgramType is VcsProgramType.VertexShader)
                    {
                        code = code.Replace("SPIRV_Cross_Input", "VS_INPUT", StringComparison.Ordinal);
                        code = code.Replace("SPIRV_Cross_Output", "PS_INPUT", StringComparison.Ordinal);
                    }
                    else if (vulkanSource.ParentCombo.ParentProgramData.VcsProgramType is VcsProgramType.PixelShader)
                    {
                        code = code.Replace("SPIRV_Cross_Input", "PS_INPUT", StringComparison.Ordinal);
                        code = code.Replace("SPIRV_Cross_Output", "PS_OUTPUT", StringComparison.Ordinal);
                    }
                }

                buffer.WriteLine($"// SPIR-V source ({vulkanSource.BytecodeSize} bytes), {backend} reflection with SPIRV-Cross by KhronosGroup");
                buffer.WriteLine($"// {StringToken.VRF_GENERATOR}");
                buffer.WriteLine();
                buffer.WriteLine(code);
            }
            catch (Exception e)
            {
                buffer.WriteLine($"// SPIR-V reflection failed: {e.Message}");

                var lastError = SpirvCrossApi.spvc_context_get_last_error_string(context);

                if (!string.IsNullOrEmpty(lastError))
                {
                    foreach (var errorLine in lastError.AsSpan().EnumerateLines())
                    {
                        buffer.Write("// ");
                        buffer.WriteLine(errorLine);
                    }
                }

                if (!lastRetry)
                {
                    var retryBackend = backend == Backend.GLSL ? Backend.HLSL : Backend.GLSL;
                    buffer.WriteLine("// ");
                    buffer.WriteLine($"// Re-attempting reflection with the {retryBackend} backend.");
                    buffer.WriteLine();

                    buffer.Write(AttemptSpirvReflection(vulkanSource, retryBackend, lastRetry: true));
                }
            }
            finally
            {
                SpirvCrossApi.spvc_context_release_allocations(context);
                SpirvCrossApi.spvc_context_destroy(context);
            }

            return buffer.ToString();
        }

        private static unsafe void Rename(spvc_compiler compiler, spvc_resources resources, SpirvResourceType resourceType, VfxShaderFile shaderFile)
        {
            var staticComboData = shaderFile.ParentCombo;
            var program = staticComboData.ParentProgramData;
            // var leadingWriteSequence = shader.ZFrameCache.Get(zFrameId).DataBlocks[dynamicId];

            var dynamicBlockIndex = Array.Find(staticComboData.DynamicCombos, r => r.ShaderFileId == shaderFile.ShaderFileId).DynamicComboId;
            var writeSequence = staticComboData.DynamicComboVariables[(int)dynamicBlockIndex];

            var reflectedResources = SpirvCrossApi.spvc_resources_get_resource_list_for_type(resources, resourceType);

            var (currentStageInputIndex, currentStageOutputIndex) = (0, 0);
            var isVertexShader = program.VcsProgramType is VcsProgramType.VertexShader;
            ValveResourceFormat.ResourceTypes.Material.InputSignatureElement[] vsInputElements = null;

            if (isVertexShader)
            {
                var inputSignature = program.VSInputSignatures[staticComboData.VShaderInputs[shaderFile.ShaderFileId]];

                var unorderedElements = inputSignature.SymbolsDefinition.ToList();
                vsInputElements = new ValveResourceFormat.ResourceTypes.Material.InputSignatureElement[unorderedElements.Count];
                var vsInputIndex = 0;

                Span<string> priority =
                [
                    "Pos",
                    "PosXyz",

                    "Color",

                    "TexCoord",
                    "LowPrecisionUv",
                    "LowPrecisionUv1", // there may be more
                    "LightmapUV",

                    "Normal",
                    "TangentU_SignV",
                    "OptionallyCompressedTangentFrame",
                    "CompressedTangentFrame",

                    "BlendIndices",
                    "BlendWeight",

                    "InstanceTransformUv",

                    "VertexPaintBlendParams", // there may be more
                    "VertexPaintTintColor",
                    "PerVertexLighting", // todo: confirm this
                ];

                var shouldPrioritizeVertexColors = program.ShaderName == "csgo_water_fancy";

                if (shouldPrioritizeVertexColors)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        var colorIndex = priority.Length - 3 + i;
                        var color = priority[colorIndex];

                        // make place for the new item
                        for (var j = colorIndex; j > i; j--)
                        {
                            priority[j] = priority[j - 1];
                        }

                        priority[i] = color;
                    }
                }

                foreach (var semantic in priority)
                {
                    var elementIndex = unorderedElements.FindIndex(el => el.Semantic == semantic);
                    if (elementIndex != -1)
                    {
                        var element = unorderedElements[elementIndex];
                        vsInputElements[vsInputIndex++] = element;
                        unorderedElements.Remove(element);
                    }
                }

                foreach (var element in unorderedElements)
                {
                    vsInputElements[vsInputIndex++] = element;
                    Log.Warn(nameof(CompiledShader), $"VS Input element semantic missing from the priority list ({element.Semantic})");
                }
            }

            foreach (var resource in reflectedResources)
            {
                var location = (int)SpirvCrossApi.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.Location);
                var index = SpirvCrossApi.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.Index);
                var binding = SpirvCrossApi.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.Binding);
                var set = SpirvCrossApi.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.DescriptorSet);

                var vfxType = resource.base_type_id switch
                {
                    406 => VfxVariableType.Sampler2D,
                    407 => VfxVariableType.Sampler3D,
                    408 => VfxVariableType.SamplerCube,
                    472 => VfxVariableType.SamplerCubeArray,
                    _ => VfxVariableType.Void,
                };

                var uniformBufferBindingOffset = isVertexShader ? 14u : 0;
                var uniformBufferBinding = binding - uniformBufferBindingOffset;

                var isGlobalsBuffer = uniformBufferBinding == 0 && set == 0;

                var name = resourceType switch
                {
                    SpirvResourceType.SeparateImage => GetNameForTexture(program, writeSequence, binding, vfxType),
                    SpirvResourceType.SeparateSamplers => GetNameForSampler(program, writeSequence, binding),
                    SpirvResourceType.StorageBuffer or SpirvResourceType.StorageImage => GetNameForStorageBuffer(program, writeSequence, binding),
                    SpirvResourceType.UniformBuffer => isGlobalsBuffer ? "_Globals_" : GetNameForUniformBuffer(program, writeSequence, uniformBufferBinding, set),
                    SpirvResourceType.StageInput => GetStageAttributeName(vsInputElements, currentStageInputIndex++, true),
                    SpirvResourceType.StageOutput => GetStageAttributeName(null, currentStageOutputIndex++, false),
                    _ => string.Empty,
                };

                // todo: add d3d semantic to hlsl vs input
                // spvc_compiler_hlsl_add_vertex_attribute_remap(location, semantic)

                if (string.IsNullOrEmpty(name))
                {
                    Console.WriteLine($"Unhandled resource type {resourceType}");
                    continue;
                }

                if (resourceType is SpirvResourceType.SeparateImage && vfxType is VfxVariableType.Void)
                {
                    name = $"{name}_unexpectedTypeId{resource.base_type_id}_{resource.type_id}";
                }

                SpirvCrossApi.spvc_compiler_set_name(compiler, resource.id, name);

                if (resourceType is SpirvResourceType.UniformBuffer)
                {
                    var bufferRanges = SpirvCrossApi.spvc_compiler_get_active_buffer_ranges(compiler, resource.id);

                    foreach (var bufferRange in bufferRanges)
                    {
                        var memberName = isGlobalsBuffer
                            ? GetGlobalBufferMemberName(program, writeSequence, offset: (int)bufferRange.offset / 4)
                            : GetBufferMemberName(program, name, offset: (int)bufferRange.offset / 4);

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

        private static string GetNameForTexture(VfxProgramData program, VfxVariableIndexArray writeSequence, uint image_binding, VfxVariableType vfxType)
        {
            var semgent1Params = writeSequence.Segment1
                .Select<VfxVariableIndexData, (VfxVariableIndexData Field, VfxVariableDescription Param)>(f => (f, program.VariableDescriptions[f.VariableIndex]));

            foreach (var field in writeSequence.Segment1)
            {
                var variable = program.VariableDescriptions[field.VariableIndex];

                if (variable.RegisterType is VfxRegisterType.SamplerState)
                {
                    Debug.Assert(variable.Flags == VariableFlags.SamplerFlag4);
                    continue;
                }

                if (variable.RegisterType is not VfxRegisterType.Texture)
                {
                    continue;
                }

                var isBindlessTextureArray = variable.Flags.HasFlag(VariableFlags.Bindless);
                Debug.Assert(variable.Flags.HasFlag(VariableFlags.TextureFlag3 | VariableFlags.SamplerFlag4));

                var startingPoint = isBindlessTextureArray ? TextureIndexStartingPoint : TextureStartingPoint;

                if (variable.VfxType is VfxVariableType.Sampler1D
                    or VfxVariableType.Sampler2D
                    or VfxVariableType.Sampler3D
                    or VfxVariableType.SamplerCube
                    or VfxVariableType.SamplerCubeArray
                    or VfxVariableType.Sampler2DArray
                    or VfxVariableType.Sampler1DArray
                    or VfxVariableType.Sampler3DArray)
                {

                    if (isBindlessTextureArray && vfxType != variable.VfxType)
                    {
                        continue;
                    }

                    if (field.Dest == image_binding - startingPoint)
                    {
                        return variable.Name;
                    }

                    continue;
                }
            }

            return "undetermined";
        }

        const int SamplerStartingPoint = 42;
        public static string GetNameForSampler(VfxProgramData program, VfxVariableIndexArray writeSequence, uint sampler_binding)
        {
            var semgent1Params = writeSequence.Segment1
                .Select<VfxVariableIndexData, (VfxVariableIndexData Field, VfxVariableDescription Param)>(f => (f, program.VariableDescriptions[f.VariableIndex]));

            var samplerSettings = string.Empty;

            foreach (var field in writeSequence.Segment1)
            {
                var param = program.VariableDescriptions[field.VariableIndex];

                if (param.RegisterType is not VfxRegisterType.SamplerState)
                {
                    continue;
                }

                if (field.Dest == sampler_binding - SamplerStartingPoint)
                {
                    var value = param.HasDynamicExpression
                        ? "dynamic"
                        : param.IntDefs[0].ToString(CultureInfo.InvariantCulture);

                    samplerSettings += $"{param.Name}_{value}__";
                }
            }

            return samplerSettings.Length > 0 ? samplerSettings[..^2] : "undetermined";
        }

        const int StorageBufferStartingPoint = 30;
        public static string GetNameForStorageBuffer(VfxProgramData program, VfxVariableIndexArray writeSequence, uint buffer_binding)
        {
            var semgent1Params = writeSequence.Segment1
                .Select<VfxVariableIndexData, (VfxVariableIndexData Field, VfxVariableDescription Param)>(f => (f, program.VariableDescriptions[f.VariableIndex]));

            foreach (var field in writeSequence.Segment1)
            {
                var param = program.VariableDescriptions[field.VariableIndex];

                if (param.VfxType is < VfxVariableType.StructuredBuffer or > VfxVariableType.RWStructuredBufferWithCounter)
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

        private static string GetNameForUniformBuffer(VfxProgramData program, VfxVariableIndexArray writeSequence, uint binding, uint set)
        {
            return writeSequence.Segment1
                .Select<VfxVariableIndexData, (VfxVariableIndexData Field, VfxVariableDescription Param)>(f => (f, program.VariableDescriptions[f.VariableIndex]))
                .Where(fp => fp.Param.VfxType is VfxVariableType.Cbuffer)
                .FirstOrDefault(fp => fp.Field.Dest == binding && fp.Field.LayoutSet == set).Param?.Name ?? "undetermined";
        }

        private static string GetGlobalBufferMemberName(VfxProgramData program, VfxVariableIndexArray writeSequence, int offset)
        {
            var globalBufferParameters = writeSequence.Globals
                .Select<VfxVariableIndexData, (VfxVariableIndexData Field, VfxVariableDescription Param)>(f => (f, program.VariableDescriptions[f.VariableIndex]))
                .ToList();

            return globalBufferParameters.FirstOrDefault(fp => fp.Field.Field2 == offset).Param?.Name ?? string.Empty;
        }

        // by offset
        // https://github.com/KhronosGroup/SPIRV-Cross/blob/f349c91274b91c1a7c173f2df70ec53080076191/spirv_hlsl.cpp#L2616
        private static string GetBufferMemberName(VfxProgramData program, string bufferName, int index = -1, int offset = -1)
        {
            var bufferParams = program.ExtConstantBufferDescriptions.FirstOrDefault(buffer => buffer.Name == bufferName)?.Variables;

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
                return bufferParams.FirstOrDefault(p => p.Offset == offset).Name;
            }
            else
            {
                return "undetermined";
            }
        }

        public static string GetStageAttributeName(ValveResourceFormat.ResourceTypes.Material.InputSignatureElement[] vsInputElements, int attributeIndex, bool input)
        {
            if (attributeIndex < vsInputElements?.Length)
            {
                return vsInputElements[attributeIndex].Name;
            }

            return $"{(input ? "input" : "output")}_{attributeIndex}";
        }
    }
}
