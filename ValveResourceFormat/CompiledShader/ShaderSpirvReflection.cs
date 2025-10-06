using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ValveResourceFormat.ResourceTypes;
using Vortice.SPIRV;
using Vortice.SpirvCross;
using SpirvResourceType = Vortice.SpirvCross.ResourceType;

namespace ValveResourceFormat.CompiledShader;

#nullable disable

/// <summary>
/// Provides SPIR-V reflection and decompilation utilities for shaders.
/// </summary>
public static partial class ShaderSpirvReflection
{
    private const int TextureStartingPoint = 30;
    private const int TextureIndexStartingPoint = 30;

    private const int SamplerStartingPoint = 42;

    private const int StorageBufferStartingPoint = 30;

    /// <summary>
    /// Reflects and decompiles SPIR-V bytecode to a target shader language.
    /// </summary>
    /// <param name="vulkanSource">The Vulkan shader source containing SPIR-V bytecode.</param>
    /// <param name="backend">The target shader language backend.</param>
    /// <param name="code">The decompiled shader code.</param>
    /// <returns>True if decompilation succeeded, false otherwise.</returns>
    public static bool ReflectSpirv(VfxShaderFileVulkan vulkanSource, Backend backend, out string code)
    {
        static bool Error(out string code, spvc_context context)
        {
            var lastError = SpirvCrossApi.spvc_context_get_last_error_string(context);
            code = lastError ?? string.Empty;
            return false;
        }

        var result = SpirvCrossApi.spvc_context_create(out var context);

        using var buffer = new StringWriter(CultureInfo.InvariantCulture);

        try
        {
            result = SpirvCrossApi.spvc_context_parse_spirv(context, vulkanSource.Bytecode, out var parsedIr);

            if (result != Result.Success) // Typically InvalidSPIRV
            {
                return Error(out code, context);
            }

            result = SpirvCrossApi.spvc_context_create_compiler(context, backend, parsedIr, CaptureMode.TakeOwnership, out var compiler);
            result = SpirvCrossApi.spvc_compiler_create_compiler_options(compiler, out var options);

            if (backend == Backend.GLSL)
            {
                SpirvCrossApi.spvc_compiler_options_set_uint(options, CompilerOption.GLSLVersion, 460);
                SpirvCrossApi.spvc_compiler_options_set_bool(options, CompilerOption.GLSLES, SpirvCrossApi.SPVC_FALSE);
                SpirvCrossApi.spvc_compiler_options_set_bool(options, CompilerOption.GLSLVulkanSemantics,
                    SpirvCrossApi.SPVC_TRUE);
                SpirvCrossApi.spvc_compiler_options_set_bool(options,
                    CompilerOption.GLSLEmitUniformBufferAsPlainUniforms, SpirvCrossApi.SPVC_TRUE);
            }
            else if (backend == Backend.HLSL)
            {
                SpirvCrossApi.spvc_compiler_options_set_uint(options, CompilerOption.HLSLShaderModel, 61);
                SpirvCrossApi.spvc_compiler_options_set_uint(options, CompilerOption.HLSLUseEntryPointName, 1);
            }

            result = SpirvCrossApi.spvc_compiler_install_compiler_options(compiler, options);

            if (vulkanSource.ParentCombo.ParentProgramData.VcsProgramType is not VcsProgramType.RaytracingShader)
            {
                result = SpirvCrossApi.spvc_compiler_create_shader_resources(compiler, out var resources);

                RenameResource(compiler, resources, SpirvResourceType.SeparateImage, vulkanSource);
                RenameResource(compiler, resources, SpirvResourceType.SeparateSamplers, vulkanSource);

                RenameResource(compiler, resources, SpirvResourceType.StorageBuffer, vulkanSource);
                RenameResource(compiler, resources, SpirvResourceType.UniformBuffer, vulkanSource);

                RenameResource(compiler, resources, SpirvResourceType.StageInput, vulkanSource);
                RenameResource(compiler, resources, SpirvResourceType.StageOutput, vulkanSource);
            }

            result = SpirvCrossApi.spvc_compiler_compile(compiler, out code);

            if (result != Result.Success)
            {
                return Error(out code, context);
            }

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

            code = ReplaceCommonPatterns(code);

            buffer.WriteLine(
                $"// SPIR-V source ({vulkanSource.BytecodeSize} bytes), {backend} reflection with SPIRV-Cross by KhronosGroup");
            buffer.WriteLine($"// {StringToken.VRF_GENERATOR}");
            buffer.WriteLine();
            buffer.WriteLine(code);
        }
        finally
        {
            SpirvCrossApi.spvc_context_release_allocations(context);
            SpirvCrossApi.spvc_context_destroy(context);
        }

        code = buffer.ToString();
        return result == Result.Success;
    }

    private static void RenameResource(spvc_compiler compiler, spvc_resources resources, SpirvResourceType resourceType,
        VfxShaderFile shaderFile)
    {
        var staticComboData = shaderFile.ParentCombo;
        var program = staticComboData.ParentProgramData;
        // var leadingWriteSequence = shader.ZFrameCache.Get(zFrameId).DataBlocks[dynamicId];

        var dynamicBlockIndex =
            Array.Find(staticComboData.DynamicCombos, r => r.ShaderFileId == shaderFile.ShaderFileId).DynamicComboId;
        var writeSequence = staticComboData.DynamicComboVariables[(int)dynamicBlockIndex];

        var reflectedResources = SpirvCrossApi.spvc_resources_get_resource_list_for_type(resources, resourceType);

        var (currentStageInputIndex, currentStageOutputIndex) = (0, 0);
        var isVertexShader = program.VcsProgramType is VcsProgramType.VertexShader;
        Material.InputSignatureElement[] vsInputElements = null;

        if (isVertexShader)
        {
            var inputSignature = program.VSInputSignatures[staticComboData.VShaderInputs[shaderFile.ShaderFileId]];

            var unorderedElements = inputSignature.SymbolsDefinition.ToList();
            vsInputElements = new Material.InputSignatureElement[unorderedElements.Count];
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
                "PerVertexLighting" // todo: confirm this
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
            }
        }

        foreach (var resource in reflectedResources)
        {
            var location =
                (int)SpirvCrossApi.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.Location);
            var index = SpirvCrossApi.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.Index);
            var binding = SpirvCrossApi.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.Binding);
            var set = SpirvCrossApi.spvc_compiler_get_decoration(compiler, resource.id, SpvDecoration.DescriptorSet);

            var vfxType = resource.base_type_id switch
            {
                406 => VfxVariableType.Sampler2D,
                407 => VfxVariableType.Sampler3D,
                408 => VfxVariableType.SamplerCube,
                472 => VfxVariableType.SamplerCubeArray,
                _ => VfxVariableType.Void
            };

            var uniformBufferBindingOffset = 0u; // used to have 14 offset on vertex shader
            var uniformBufferBinding = binding - uniformBufferBindingOffset;

            // confirmed 0 in vs, gs, 1 in ps
            var globalsBufferSet = program.VcsProgramType is VcsProgramType.PixelShader ? 1 : 0;

            var isGlobalsBuffer = uniformBufferBinding == 0 && set == globalsBufferSet;

            var name = resourceType switch
            {
                SpirvResourceType.SeparateImage => GetNameForTexture(program, writeSequence, binding, vfxType),
                SpirvResourceType.SeparateSamplers => GetNameForSampler(program, writeSequence, binding),
                SpirvResourceType.StorageBuffer or SpirvResourceType.StorageImage => GetNameForStorageBuffer(program,
                    writeSequence, binding),
                SpirvResourceType.UniformBuffer => isGlobalsBuffer
                    ? "_Globals_"
                    : GetNameForUniformBuffer(program, writeSequence, uniformBufferBinding, set),
                SpirvResourceType.StageInput => GetStageAttributeName(vsInputElements, currentStageInputIndex++, true),
                SpirvResourceType.StageOutput => GetStageAttributeName(null, currentStageOutputIndex++, false),
                _ => string.Empty
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
                    unsafe
                    {
                        var memberName = isGlobalsBuffer
                            ? GetGlobalBufferMemberName(program, writeSequence, (int)bufferRange.offset / 4)
                            : GetBufferMemberName(program, name, offset: (int)bufferRange.offset / 4);

                        if (string.IsNullOrEmpty(memberName))
                        {
                            continue;
                        }

                        fixed (byte* memberNameBytes = memberName.GetUtf8Span())
                        {
                            SpirvCrossApi.spvc_compiler_set_member_name(compiler, resource.base_type_id, bufferRange.index,
                                memberNameBytes);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets the variable name for a texture at a given binding point.
    /// </summary>
    /// <param name="program">The shader program data.</param>
    /// <param name="writeSequence">The write sequence containing variable indices.</param>
    /// <param name="imageBinding">The image binding point.</param>
    /// <param name="vfxType">The VFX variable type to match.</param>
    /// <returns>The texture variable name, or "undetermined" if not found.</returns>
    public static string GetNameForTexture(VfxProgramData program, VfxVariableIndexArray writeSequence,
        uint imageBinding, VfxVariableType vfxType)
    {
        var semgent1Params = writeSequence.RenderState
            .Select<VfxVariableIndexData, (VfxVariableIndexData Field, VfxVariableDescription Param)>(f =>
                (f, program.VariableDescriptions[f.VariableIndex]));

        foreach (var field in writeSequence.RenderState)
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

                if (field.Dest == imageBinding - startingPoint)
                {
                    return variable.Name;
                }
            }
        }

        return "undetermined";
    }

    /// <summary>
    /// Gets the variable name for a sampler at a given binding point.
    /// </summary>
    /// <param name="program">The shader program data.</param>
    /// <param name="writeSequence">The write sequence containing variable indices.</param>
    /// <param name="samplerBinding">The sampler binding point.</param>
    /// <returns>The sampler variable name, or "undetermined" if not found.</returns>
    public static string GetNameForSampler(VfxProgramData program, VfxVariableIndexArray writeSequence,
        uint samplerBinding)
    {
        var semgent1Params = writeSequence.RenderState
            .Select<VfxVariableIndexData, (VfxVariableIndexData Field, VfxVariableDescription Param)>(f =>
                (f, program.VariableDescriptions[f.VariableIndex]));

        var samplerSettings = string.Empty;

        foreach (var field in writeSequence.RenderState)
        {
            var param = program.VariableDescriptions[field.VariableIndex];

            if (param.RegisterType is not VfxRegisterType.SamplerState)
            {
                continue;
            }

            if (field.Dest == samplerBinding - SamplerStartingPoint)
            {
                var value = param.HasDynamicExpression
                    ? "dynamic"
                    : param.IntDefs[0].ToString(CultureInfo.InvariantCulture);

                samplerSettings += $"{param.Name}_{value}__";
            }
        }

        return samplerSettings.Length > 0 ? samplerSettings[..^2] : "undetermined";
    }

    /// <summary>
    /// Gets the variable name for a storage buffer at a given binding point.
    /// </summary>
    /// <param name="program">The shader program data.</param>
    /// <param name="writeSequence">The write sequence containing variable indices.</param>
    /// <param name="bufferBinding">The buffer binding point.</param>
    /// <returns>The storage buffer variable name, or "undetermined" if not found.</returns>
    public static string GetNameForStorageBuffer(VfxProgramData program, VfxVariableIndexArray writeSequence,
        uint bufferBinding)
    {
        var semgent1Params = writeSequence.RenderState
            .Select<VfxVariableIndexData, (VfxVariableIndexData Field, VfxVariableDescription Param)>(f =>
                (f, program.VariableDescriptions[f.VariableIndex]));

        foreach (var field in writeSequence.RenderState)
        {
            var param = program.VariableDescriptions[field.VariableIndex];

            if (param.VfxType is < VfxVariableType.StructuredBuffer or > VfxVariableType.RWStructuredBufferWithCounter)
            {
                continue;
            }

            if (field.Dest == bufferBinding - StorageBufferStartingPoint)
            {
                return param.Name;
            }
        }

        return "undetermined";
    }

    /// <summary>
    /// Gets the variable name for a uniform buffer at a given binding point and descriptor set.
    /// </summary>
    /// <param name="program">The shader program data.</param>
    /// <param name="writeSequence">The write sequence containing variable indices.</param>
    /// <param name="binding">The buffer binding point.</param>
    /// <param name="set">The descriptor set index.</param>
    /// <returns>The uniform buffer variable name, or "undetermined" if not found.</returns>
    public static string GetNameForUniformBuffer(VfxProgramData program, VfxVariableIndexArray writeSequence,
        uint binding, uint set)
    {
        return writeSequence.RenderState
            .Select<VfxVariableIndexData, (VfxVariableIndexData Field, VfxVariableDescription Param)>(f =>
                (f, program.VariableDescriptions[f.VariableIndex]))
            .Where(fp => fp.Param.VfxType is VfxVariableType.Cbuffer)
            .FirstOrDefault(fp => fp.Field.Dest == binding && fp.Field.LayoutSet == set).Param?.Name ?? "undetermined";
    }

    /// <summary>
    /// Gets the member name for a global buffer variable at a given offset.
    /// </summary>
    /// <param name="program">The shader program data.</param>
    /// <param name="writeSequence">The write sequence containing variable indices.</param>
    /// <param name="offset">The offset in the buffer.</param>
    /// <returns>The member name, or an empty string if not found.</returns>
    public static string GetGlobalBufferMemberName(VfxProgramData program, VfxVariableIndexArray writeSequence,
        int offset)
    {
        return writeSequence.Globals
            .Select<VfxVariableIndexData, (VfxVariableIndexData Field, VfxVariableDescription Param)>(f =>
                (f, program.VariableDescriptions[f.VariableIndex]))
            .FirstOrDefault(fp => fp.Field.Field2 == offset).Param?.Name ?? string.Empty;
    }

    // by offset
    // https://github.com/KhronosGroup/SPIRV-Cross/blob/f349c91274b91c1a7c173f2df70ec53080076191/spirv_hlsl.cpp#L2616
    /// <summary>
    /// Gets the member name for a buffer variable by index or offset.
    /// </summary>
    /// <param name="program">The shader program data.</param>
    /// <param name="bufferName">The buffer name.</param>
    /// <param name="index">The member index (optional).</param>
    /// <param name="offset">The member offset (optional).</param>
    /// <returns>The member name, or an empty string if not found.</returns>
    public static string GetBufferMemberName(VfxProgramData program, string bufferName, int index = -1,
        int offset = -1)
    {
        var bufferParams = program.ExtConstantBufferDescriptions.FirstOrDefault(buffer => buffer.Name == bufferName)
            ?.Variables;

        if (bufferParams is null)
        {
            return string.Empty;
        }

        if (index != -1)
        {
            return bufferParams[index].Name;
        }

        if (offset != -1)
        {
            return bufferParams.FirstOrDefault(p => p.Offset == offset).Name;
        }

        return "undetermined";
    }

    /// <summary>
    /// Gets the name for a shader stage input or output attribute.
    /// </summary>
    /// <param name="vsInputElements">The input signature elements array.</param>
    /// <param name="attributeIndex">The attribute index.</param>
    /// <param name="input">True if this is an input attribute, false for output.</param>
    /// <returns>The attribute name from the signature, or a generated name if not found.</returns>
    public static string GetStageAttributeName(Material.InputSignatureElement[] vsInputElements, int attributeIndex,
        bool input)
    {
        if (attributeIndex < vsInputElements?.Length)
        {
            return vsInputElements[attributeIndex].Name;
        }

        return $"{(input ? "input" : "output")}_{attributeIndex}";
    }

    [GeneratedRegex(@"\bclamp\s*\(\s*([^\(\),]+?)\s*,\s*(?:0\.?0?|vec[2-4]\s*\(\s*0\.?0?\s*\))\s*,\s*(?:1\.?0?|vec[2-4]\s*\(\s*1\.?0?\s*\))\s*\)", RegexOptions.Compiled)]
    private static partial Regex Clamp01();

    private static string ReplaceCommonPatterns(string code)
    {
        code = Clamp01().Replace(code, "saturate($1)");

        return code;
    }
}
