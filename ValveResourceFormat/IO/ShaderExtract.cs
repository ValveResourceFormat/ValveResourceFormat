using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.VfxEval;

namespace ValveResourceFormat.IO;

/// <summary>
/// Extracts Source 2 compiled shaders to readable shader code.
/// </summary>
public sealed class ShaderExtract
{
    /// <summary>
    /// Configuration parameters for shader extraction.
    /// </summary>
    public readonly struct ShaderExtractParams
    {
        /// <summary>Gets a value indicating whether to collapse buffers in an include file.</summary>
        public bool CollapseBuffers_InInclude { get; init; }
        /// <summary>Gets a value indicating whether to collapse buffers in place.</summary>
        public bool CollapseBuffers_InPlace { get; init; }
        /// <summary>Gets a value indicating whether to collapse all buffers.</summary>
        // If true, *all* buffers will be collapsed, otherwise only the ones in BuffersToCollapse.
        public bool CollapseAllBuffers { get; init; }
        /// <summary>Gets the set of buffer names to collapse.</summary>
        public static HashSet<string> BuffersToCollapse =>
        [
            //"PerViewConstantBuffer_t",
            //"PerViewConstantBufferVR_t",
            //"PerViewLightingConstantBufferVr_t",
            //"DotaGlobalParams_t",
        ];

        /// <summary>Gets a value indicating whether to write uncertain enums as integers.</summary>
        public bool ForceWrite_UncertainEnumsAsInts { get; init; }
        /// <summary>Gets a value indicating whether to disable Hungarian notation type guessing.</summary>
        public bool NoHungarianTypeGuessing { get; init; }
        /// <summary>Gets a value indicating whether to write parameters in raw format.</summary>
        public bool WriteParametersRaw { get; init; }
        /// <summary>Gets or sets a value indicating whether static combos can be read.</summary>
        public bool CanReadStaticCombos
        {
            get => StaticComboReadingCap != 0;
            init
            {
                var cap = StaticComboReadingCap == 0 ? -1 : StaticComboReadingCap;
                StaticComboReadingCap = value ? cap : 0;
            }
        }
        /// <summary>Gets the maximum number of static combos to read.</summary>
        public int StaticComboReadingCap { get; init; }
        /// <summary>Gets a value indicating whether to disable separate globals in static combo attributes.</summary>
        public bool StaticComboAttributes_NoSeparateGlobals { get; init; }
        /// <summary>Gets a value indicating whether to disable conditional reduce in static combo attributes.</summary>
        public bool StaticComboAttributes_NoConditionalReduce { get; init; }

        private static readonly ShaderExtractParams Shared = new()
        {
            WriteParametersRaw = true,
        };

        /// <summary>Parameters optimized for shader inspection.</summary>
        public static readonly ShaderExtractParams Inspect = Shared with
        {
            // CollapseBuffers_InPlace = true,
            StaticComboReadingCap = 512,
        };

        /// <summary>Parameters optimized for shader export.</summary>
        public static readonly ShaderExtractParams Export = Shared with
        {
            CollapseAllBuffers = true,
            CollapseBuffers_InInclude = true,
            StaticComboReadingCap = -1,
        };
    }

    /// <summary>Gets the shader collection.</summary>
    public ShaderCollection Shaders { get; init; }

    /// <summary>Gets the features program data.</summary>
    public VfxProgramData? Features => Shaders.Features;
    /// <summary>Gets the mesh shader program data.</summary>
    public VfxProgramData? Mesh => Shaders.Mesh;
    /// <summary>Gets the geometry shader program data.</summary>
    public VfxProgramData? Geometry => Shaders.Geometry;
    /// <summary>Gets the vertex shader program data.</summary>
    public VfxProgramData? Vertex => Shaders.Vertex;
    /// <summary>Gets the domain shader program data.</summary>
    public VfxProgramData? Domain => Shaders.Domain;
    /// <summary>Gets the hull shader program data.</summary>
    public VfxProgramData? Hull => Shaders.Hull;
    /// <summary>Gets the pixel shader program data.</summary>
    public VfxProgramData? Pixel => Shaders.Pixel;
    /// <summary>Gets the compute shader program data.</summary>
    public VfxProgramData? Compute => Shaders.Compute;
    /// <summary>Gets the pixel shader render state data.</summary>
    public VfxProgramData? PixelShaderRenderState => Shaders.PixelShaderRenderState;
    /// <summary>Gets the raytracing shader program data.</summary>
    public VfxProgramData? Raytracing => Shaders.Raytracing;

    private readonly string[] FeatureNames;
    private readonly string[] Globals;
    private HashSet<string> VariantParameterNames = [];
    private HashSet<int> VariantParameterIndices = [];

    private ShaderExtractParams Options;
    private Dictionary<string, IndentedTextWriter> IncludeWriters = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ShaderExtract"/> class.
    /// </summary>
    public ShaderExtract(Resource resource)
        : this((SboxShader)resource.GetBlockByType(BlockType.SPRV)!)
    { }

    /// <inheritdoc cref="ShaderExtract(Resource)"/>
    public ShaderExtract(SboxShader sboxShaderCollection)
        : this(sboxShaderCollection.Shaders)
    { }

    /// <inheritdoc cref="ShaderExtract(Resource)"/>
    public ShaderExtract(ShaderCollection shaderCollection)
    {
        Shaders = shaderCollection;

        if (Features == null)
        {
            throw new InvalidOperationException("Shader extract cannot continue without at least a features file.");
        }

        FeatureNames = [.. Features.StaticComboArray.Select(f => f.Name)];
        Globals = [.. Features.VariableDescriptions.Select(p => p.Name)];
    }

    /// <summary>
    /// Converts the shader to a content file with extracted shader code.
    /// </summary>
    public ContentFile ToContentFile()
    {
        var vfx = ToVFX(ShaderExtractParams.Export);

        var extract = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(vfx.VfxContent)
        };

        foreach (var (fileName, content) in vfx.Includes)
        {
            extract.AddSubFile(fileName, () => Encoding.UTF8.GetBytes(content));
        }

        return extract;
    }

    /// <summary>
    /// Gets the VFX file name for this shader.
    /// </summary>
    public string GetVfxFileName()
        => GetVfxNameFromShaderFile(Features!);

    private static string GetVfxNameFromShaderFile(VfxProgramData program)
    {
        return program.ShaderName + ".vfx";
    }

    private static string GetIncludeName(string bufferName)
    {
        return $"common/{bufferName}.fxc";
    }

    /// <summary>
    /// Converts the shader to VFX format for inspection.
    /// </summary>
    public string ToVFX()
    {
        return ToVFXInternal(ShaderExtractParams.Inspect);
    }

    /// <summary>
    /// Converts the shader to VFX format with includes using specified options.
    /// </summary>
    public (string VfxContent, IDictionary<string, string> Includes) ToVFX(in ShaderExtractParams options)
    {
        return (ToVFXInternal(options), IncludeWriters.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString()));
    }

    private string ToVFXInternal(in ShaderExtractParams options)
    {
        Options = options;
        IncludeWriters = [];
        PreprocessCommon();

        using var output = new IndentedTextWriter();
        output.WriteLine("//=================================================================================================");
        output.WriteLine($"// Reconstructed with {StringToken.VRF_GENERATOR}");
        output.WriteLine("//=================================================================================================");

        HEADER(output);
        MODES(output);
        FEATURES(output);
        COMMON(output);
        MS(output);
        VS_INPUT(output);
        VS(output);
        GS(output);
        HS(output);
        DS(output);
        PS(output);
        CS(output);
        RTX(output);

        return output.ToString();
    }

    class CommonBlocks
    {
        public readonly HashSet<ConstantBufferDescription> BufferBlocks = new(new BufferBlockComparer());

        public class BufferBlockComparer : IEqualityComparer<ConstantBufferDescription>
        {
            public bool Equals(ConstantBufferDescription? x, ConstantBufferDescription? y) => x?.Name == y?.Name;
            public int GetHashCode(ConstantBufferDescription obj) => (int)obj.BlockCrc;
        }
    }

    private readonly CommonBlocks Common = new();

    private void PreprocessCommon()
    {
        var firstPass = true;
        var stages = Shaders
            .Where(s =>
                !(s.VcsProgramType is VcsProgramType.Features
                    or VcsProgramType.PixelShaderRenderState
                    or VcsProgramType.RaytracingShader))
            .ToList();

        if (stages.Count < 2)
        {
            return;
        }

        foreach (var stage in stages)
        {
            if (firstPass)
            {
                firstPass = false;
                Common.BufferBlocks.UnionWith(stage.ExtConstantBufferDescriptions);
                continue;
            }

            Common.BufferBlocks.IntersectWith(stage.ExtConstantBufferDescriptions);

            if (Common.BufferBlocks.Count == 0)
            {
                break;
            }
        }
    }

    private void HEADER(IndentedTextWriter writer)
    {
        writer.WriteLine(nameof(HEADER));
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine($"Description = \"{Features!.FeaturesHeader!.FileDescription}\";");
        writer.WriteLine($"DevShader = {(Features.FeaturesHeader.DevShader ? "true" : "false")};");
        writer.WriteLine($"Version = {Features.FeaturesHeader.Version};");

        var programs = Shaders
            .Where(s => s.VcsProgramType != VcsProgramType.Features)
            .Select(s => ShaderUtilHelpers.ComputeVcsProgramType(s.VcsProgramType));
        writer.WriteLine();
        writer.WriteLine($"// VcsVersion: {Features.VcsVersion}");
        writer.WriteLine($"// Platform: {Features.VcsPlatformType}_SM{Features.VcsShaderModelType}");
        writer.WriteLine($"// Programs: {string.Join(", ", programs)}");

        writer.Indent--;
        writer.WriteLine("}");
    }

    private void MODES(IndentedTextWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine(nameof(MODES));
        writer.WriteLine("{");
        writer.Indent++;

        foreach (var mode in Features!.FeaturesHeader!.Modes)
        {
            if (string.IsNullOrEmpty(mode.Shader))
            {
                writer.WriteLine($"{mode.Name}({mode.StaticConfig});");
            }
            else
            {
                writer.WriteLine($"{mode.Name}(\"{mode.Shader}\");");
            }
        }

        writer.Indent--;
        writer.WriteLine("}");
    }

    private void FEATURES(IndentedTextWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine(nameof(FEATURES));
        writer.WriteLine("{");
        writer.Indent++;

        HandleFeatures(Features!.StaticComboArray, Features.StaticComboRules, writer);

        writer.Indent--;
        writer.WriteLine("}");
    }

    private void COMMON(IndentedTextWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine(nameof(COMMON));
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine("#include \"system.fxc\"");

        WriteCBuffers(Common.BufferBlocks, writer);

        writer.Indent--;
        writer.WriteLine("}");
    }

    private void VS_INPUT(IndentedTextWriter writer)
    {
        if (Vertex is null)
        {
            return;
        }

        var symbols = new List<Material.InputSignatureElement>();
        var masks = new List<bool[]>();
        var maxNameLength = 0;
        var maxSemanticLength = 0;

        for (var i = 0; i < Vertex.VSInputSignatures.Length; i++)
        {
            for (var j = 0; j < Vertex.VSInputSignatures[i].SymbolsDefinition.Length; j++)
            {
                var symbol = Vertex.VSInputSignatures[i].SymbolsDefinition[j];
                var existingIndex = symbols.IndexOf(symbol);
                if (existingIndex == -1)
                {
                    symbols.Insert(j, symbol);
                    var mask = new bool[Vertex.VSInputSignatures.Length];
                    mask[i] = true;
                    masks.Insert(j, mask);
                    maxNameLength = Math.Max(maxNameLength, symbol.Name.Length);
                    maxSemanticLength = Math.Max(maxSemanticLength, symbol.D3DSemanticName.Length);
                }
                else
                {
                    masks[existingIndex][i] = true;
                }
            }
        }

        ConfigMappingParams staticConfig = new(Vertex, isDynamic: false);
        ConfigMappingParams dynamicConfig = new(Vertex, isDynamic: true);

        var perConditionVsInputBlocks = new Dictionary<(string Name, int State), HashSet<int>>(staticConfig.SumStates + dynamicConfig.SumStates);

        var programIndex = 0;
        foreach (var staticComboEntry in Vertex.StaticComboEntries)
        {
            if (Options.StaticComboReadingCap >= 0 && ++programIndex >= Options.StaticComboReadingCap)
            {
                break;
            }

            var staticCombo = staticComboEntry.Value.Unserialize();
            var staticConfigState = staticConfig.GetConfigState(staticCombo.StaticComboId);

            foreach (var vsEnd in staticCombo.DynamicCombos)
            {
                var dynamicConfigState = dynamicConfig.GetConfigState(vsEnd.DynamicComboId);
                var vsInputId = staticCombo.VShaderInputs[vsEnd.ShaderFileId];

                for (var j = 0; j < staticConfigState.Length; j++)
                {
                    var staticCondition = (Vertex.StaticComboArray[j].Name, staticConfigState[j]);
                    if (!perConditionVsInputBlocks.TryGetValue(staticCondition, out var staticVsBlocks))
                    {
                        staticVsBlocks = new HashSet<int>(Vertex.VSInputSignatures.Length);
                        perConditionVsInputBlocks.Add(staticCondition, staticVsBlocks);
                    }

                    staticVsBlocks.Add(vsInputId);

                    for (var k = 0; k < dynamicConfigState.Length; k++)
                    {
                        var dynamicCondition = (Vertex.DynamicComboArray[k].Name, dynamicConfigState[k]);
                        if (!perConditionVsInputBlocks.TryGetValue(dynamicCondition, out var dynamicVsBlocks))
                        {
                            dynamicVsBlocks = new HashSet<int>(Vertex.VSInputSignatures.Length);
                            perConditionVsInputBlocks.Add(dynamicCondition, dynamicVsBlocks);
                        }

                        dynamicVsBlocks.Add(vsInputId);
                    }
                }
            }
        }

        if (symbols.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine($"struct VS_INPUT");
            writer.WriteLine("{");
            writer.Indent++;

            foreach (var (symbol, i) in symbols.Select((symbol, i) => (symbol, i)))
            {
                var type = string.Empty;
                if (!Options.NoHungarianTypeGuessing && symbol.Name.Length > 2)
                {
                    var typeChar = symbol.Name[1] == '_' ? symbol.Name[2] : symbol.Name[0];
                    type = typeChar switch
                    {
                        'f' or 'v' => "float4",
                        'n' => "uint4",
                        'b' => "bool4",
                        _ => string.Empty
                    };

                    if (symbol.Semantic.Contains("UV", StringComparison.OrdinalIgnoreCase))
                    {
                        type = "float2";
                    }
                    else if (symbol.Semantic == "PosXyz")
                    {
                        type = "float3";
                    }

                    type = $"{type,-7}";
                }

                var attributeVfx = symbol.Semantic.Length > 0 ? $" < Semantic( {symbol.Semantic} ); >" : string.Empty;
                var nameAlignSpaces = new string(' ', maxNameLength - symbol.Name.Length);
                var semanticAlignSpaces = new string(' ', maxSemanticLength - symbol.D3DSemanticName.Length);

                var field = $"{type}{symbol.Name}{nameAlignSpaces} : {symbol.D3DSemanticName}{symbol.D3DSemanticIndex,-2}{semanticAlignSpaces}{attributeVfx};";
                writer.Write($"{field,-94}");

                if (masks[i].All(x => x) || !Options.CanReadStaticCombos)
                {
                    writer.WriteLine();
                    continue;
                }

                var conditions = new List<string>();
                foreach (var (condition, VsInputIds) in perConditionVsInputBlocks)
                {
                    if (VsInputIds.Count == Vertex.VSInputSignatures.Length)
                    {
                        continue;
                    }

                    if (VsInputIds.Where(x => x >= 0).All(x => masks[i][x]))
                    {
                        conditions.Add($"{condition.Name}={condition.State}");
                    }
                }

                if (conditions.Count > 0)
                {
                    writer.WriteLine($" // {string.Join(", ", conditions)}");
                }
                else
                {
                    writer.WriteLine();
                }
            }

            writer.Indent--;
            writer.WriteLine("};");
        }
    }

    private void WriteCBuffers(IEnumerable<ConstantBufferDescription> bufferBlocks, IndentedTextWriter writer)
    {
        foreach (var buffer in bufferBlocks)
        {
            if (Options.CollapseAllBuffers || ShaderExtractParams.BuffersToCollapse.Contains(buffer.Name))
            {
                if (Options.CollapseBuffers_InPlace)
                {
                    writer.WriteLine("cbuffer " + buffer.Name + ";");
                    continue;
                }

                if (Options.CollapseBuffers_InInclude && IncludeWriters is not null)
                {
                    var includeName = GetIncludeName(buffer.Name);

                    if (!IncludeWriters.TryGetValue(includeName, out var includeWriter))
                    {
                        includeWriter = new IndentedTextWriter();
                        IncludeWriters.Add(includeName, includeWriter);
                        WriteCBuffer(includeWriter, buffer);
                    }

                    writer.WriteLine($"#include \"{includeName}\"");
                    continue;
                }
            }

            writer.WriteLine();
            WriteCBuffer(writer, buffer);
        }
    }

    private void WriteCBuffer(IndentedTextWriter writer, ConstantBufferDescription buffer)
    {
        writer.WriteLine("cbuffer " + buffer.Name);
        writer.WriteLine("{");
        writer.Indent++;

        foreach (var member in buffer.Variables)
        {
            var type = "DWORD";
            if (!Options.NoHungarianTypeGuessing && member.Name.Length > 3)
            {
                type = member.Name[2] switch
                {
                    'f' or 'v' or 'm' => "float",
                    'n' => "int",
                    'b' => "bool",
                    _ => type
                };
            }

            var dim1 = member.VectorSize > 1
                ? member.VectorSize.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            var dim2 = member.Depth > 1
                ? "x" + member.Depth.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            var array = member.Length > 1
                ? "[" + member.Length.ToString(CultureInfo.InvariantCulture) + "]"
                : string.Empty;

            writer.WriteLine($"{type}{dim1}{dim2} {member.Name}{array};");
        }

        writer.Indent--;
        writer.WriteLine("};");
    }

    private void MS(IndentedTextWriter writer)
        => HandleStageShared(writer, Mesh, nameof(MS));

    private void VS(IndentedTextWriter writer)
        => HandleStageShared(writer, Vertex, nameof(VS));

    private void GS(IndentedTextWriter writer)
        => HandleStageShared(writer, Geometry, nameof(GS));

    private void HS(IndentedTextWriter writer)
        => HandleStageShared(writer, Hull, nameof(HS));

    private void DS(IndentedTextWriter writer)
        => HandleStageShared(writer, Domain, nameof(DS));

    private void PS(IndentedTextWriter writer)
        => HandleStageShared(writer, Pixel, nameof(PS));

    private void CS(IndentedTextWriter writer)
        => HandleStageShared(writer, Compute, nameof(CS));

    private void RTX(IndentedTextWriter writer)
        => HandleStageShared(writer, Raytracing, nameof(RTX));

    private void HandleStageShared(IndentedTextWriter writer, VfxProgramData? program, string stageName)
    {
        if (program is null)
        {
            return;
        }

        writer.WriteLine();
        writer.WriteLine(stageName);
        writer.WriteLine("{");
        writer.Indent++;

        HandleStaticCombos(program.StaticComboArray, program.StaticComboRules, writer);
        HandleDynamicCombos(program.StaticComboArray, program.DynamicComboArray, program.DynamicComboRules, writer);

        WriteCBuffers(program.ExtConstantBufferDescriptions.Where(b => !Common.BufferBlocks.Contains(b)), writer);

        HandleParameters(program.VariableDescriptions, program.TextureChannelProcessors, writer);

        HandleZFrames(program, writer);

        if (program.VcsProgramType == VcsProgramType.PixelShader && PixelShaderRenderState is not null)
        {
            writer.WriteLine();
            writer.WriteLine("// PSRS");
            HandleParameters(PixelShaderRenderState.VariableDescriptions, PixelShaderRenderState.TextureChannelProcessors, writer);
            HandleZFrames(PixelShaderRenderState, writer);
        }

        writer.Indent--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Compares shader configuration keys for equality.
    /// </summary>
    public class ConfigKeyComparer : IEqualityComparer<int[]>
    {
        /// <inheritdoc/>
        public bool Equals(int[]? x, int[]? y)
        {
            return x == null || y == null
                ? x == null && y == null
                : x.SequenceEqual(y);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Generates a hash code by treating the array as a bit field.
        /// </remarks>
        public int GetHashCode(int[] obj)
        {
            // this will collide with non boolean states, but those are rare
            var sum = 0;
            for (var i = 0; i < obj.Length; i++)
            {
                if (obj[i] != 0)
                {
                    sum |= 1 << i;
                }
            }
            return sum;
        }
    }

    private void HandleZFrames(VfxProgramData program, IndentedTextWriter writer)
    {
        if (program.StaticComboEntries.Count == 0 || !Options.CanReadStaticCombos)
        {
            return;
        }

        ConfigMappingParams staticConfig = new(program);

        // Attributes
        var attributesDisect = new Dictionary<int[], HashSet<string>>(2 ^ Math.Max(0, program.StaticComboArray.Length - 1), new ConfigKeyComparer());
        var perConditionAttributes = new Dictionary<(int Index, int State), HashSet<string>>(staticConfig.SumStates);

        // Parameters
        var perConditionParameters = new Dictionary<(int Index, int State), HashSet<int>>(staticConfig.SumStates);
        var hasParameters = program.VariableDescriptions.Length > 0;

        // Raw glsl (old) or SPIR-V reflected source for variant 0
        var variant0Source = new StringBuilder();

        var programIndex = 0;
        foreach (var staticComboEntry in program.StaticComboEntries)
        {
            if (Options.StaticComboReadingCap >= 0 && programIndex++ >= Options.StaticComboReadingCap)
            {
                break;
            }

            var staticCombo = staticComboEntry.Value.Unserialize();
            var zframeAttributes = GetZFrameAttributes(staticCombo, program.VariableDescriptions);

            var staticConfigState = staticConfig.GetConfigState(staticCombo.StaticComboId);
            attributesDisect[staticConfigState] = zframeAttributes;

            if (staticCombo.ShaderFiles.Length > 0 && variant0Source.Length == 0)
            {
                var dynamicId = 0;
                var gpuSource = staticCombo.ShaderFiles[dynamicId];
                if (gpuSource is VfxShaderFileGL glsl)
                {
                    variant0Source.AppendLine("// --------- GLSL source begin --------- ");
                    variant0Source.Append(glsl.GetDecompiledFile());
                    variant0Source.AppendLine("// ---------  GLSL source end  --------- ");
                }
                else if (gpuSource is VfxShaderFileVulkan spirv && !spirv.IsEmpty() && ShaderUtilHelpers.IsSpirvCrossAvailable())
                {
                    variant0Source.Append(spirv.GetDecompiledFile());
                    variant0Source.AppendLine("// ---------  SPIRV -> HLSL end  --------- ");
                }
            }

            if (!hasParameters)
            {
                continue;
            }

            var variantParameters = GetVariantZFrameParameters(staticCombo);
            for (var j = 0; j < staticConfigState.Length; j++)
            {
                var key = (j, staticConfigState[j]);
                if (perConditionParameters.TryGetValue(key, out var set))
                {
                    set.IntersectWith(variantParameters);
                    if (set.Count == 0)
                    {
                        perConditionParameters.Remove(key);
                    }
                }
                else
                {
                    perConditionParameters[key] = variantParameters;
                }
            }
        }

        if (hasParameters && VariantParameterIndices?.Count > 0)
        {
            writer.WriteLine();
            WriteVariantParameters(program.StaticComboArray, program.VariableDescriptions, program.TextureChannelProcessors, writer, perConditionParameters);
        }

        if (variant0Source.Length > 0)
        {
            writer.WriteLine();

            foreach (var line in variant0Source.ToString().AsSpan().EnumerateLines())
            {
                writer.WriteLine(line);
            }

            writer.WriteLine();
        }

        WriteAttributes(program.StaticComboArray, writer, attributesDisect, perConditionAttributes);
    }

    private void WriteVariantParameters(VfxCombo[] sfBlocks, VfxVariableDescription[] paramBlocks, VfxTextureChannelProcessor[] channelBlocks,
        IndentedTextWriter writer, Dictionary<(int Index, int State), HashSet<int>> perConditionParameters)
    {
        var written = new HashSet<int>();
        foreach (var (condition, parameters) in perConditionParameters)
        {
            if (IsIrrelevantCondition(sfBlocks, perConditionParameters, condition, parameters))
            {
                continue;
            }

            if (parameters.Count == 0 || parameters.All(p =>
                paramBlocks[p].RegisterType == VfxRegisterType.SamplerState || paramBlocks[p].RegisterType == VfxRegisterType.Buffer))
            {
                continue;
            }

            var stateName = sfBlocks[condition.Index].Name;
            writer.WriteLine($"#if ({stateName} == {condition.State})");
            writer.Indent++;
            foreach (var param in parameters)
            {
                WriteParam(paramBlocks[param], paramBlocks, channelBlocks, writer);
                written.Add(param);
            }
            writer.Indent--;
            writer.WriteLine("#endif");
        }

        if (written.Count == VariantParameterIndices.Count)
        {
            var inconclusiveVariantParameters = VariantParameterIndices.Where(p => !written.Contains(p)).ToList();
            if (inconclusiveVariantParameters.Count > 0)
            {
                writer.WriteLine();
                writer.WriteLine("// Variant parameters");
                foreach (var param in inconclusiveVariantParameters)
                {
                    WriteParam(paramBlocks[param], paramBlocks, channelBlocks, writer);
                }
            }
        }
    }

    private void WriteAttributes(VfxCombo[] sfBlocks, IndentedTextWriter writer,
        Dictionary<int[], HashSet<string>> attributesDisect, Dictionary<(int Index, int State), HashSet<string>> perConditionAttributes)
    {
        var written = new HashSet<string>();
        if (attributesDisect.Values.First().Count != 0 || !Options.StaticComboAttributes_NoSeparateGlobals)
        {
            var invariants = new HashSet<string>(attributesDisect.Values.First());
            foreach (var attributes in attributesDisect.Values.Skip(1))
            {
                // If count of this reaches 0, there can't be any invariant attributes!
                if (invariants.Count == 0)
                {
                    break;
                }

                invariants.IntersectWith(attributes);
            }

            if (invariants.Count > 0)
            {
                writer.WriteLine();

                foreach (var attribute in invariants)
                {
                    writer.WriteLine(attribute);
                    written.Add(attribute);
                }

                writer.WriteLine();

                // Remove invariants from the disect
                foreach (var (config, attributes) in attributesDisect)
                {
                    attributes.ExceptWith(invariants);
                }
            }
        }

        if (Options.StaticComboAttributes_NoConditionalReduce)
        {
            foreach (var (config, attributes) in attributesDisect)
            {
                if (attributes.Count == 0)
                {
                    continue;
                }

                var conditions = string.Join(" && ", config.Select((v, i) => $"{sfBlocks[i].Name} == {v}"));
                writer.WriteLine($"#if ({conditions})");
                writer.Indent++;
                foreach (var attribute in attributes)
                {
                    writer.WriteLine(attribute);
                    written.Add(attribute);
                }
                writer.Indent--;
                writer.WriteLine("#endif");
            }

            return;
        }


        foreach (var (config, attributes) in attributesDisect)
        {
            if (attributes.Count == 0)
            {
                continue;
            }

            for (var i = 0; i < config.Length; i++)
            {
                var key = (i, config[i]);
                if (perConditionAttributes.TryGetValue(key, out var set))
                {
                    set.IntersectWith(attributes);
                    if (set.Count == 0)
                    {
                        perConditionAttributes.Remove(key);
                    }
                }
                else
                {
                    perConditionAttributes[key] = [.. attributes];
                }
            }
        }

        foreach (var (condition, attributes) in perConditionAttributes)
        {
            if (IsIrrelevantCondition(sfBlocks, perConditionAttributes, condition, attributes))
            {
                continue;
            }

            var stateName = sfBlocks[condition.Index].Name;
            writer.WriteLine($"#if ({stateName} == {condition.State})");
            writer.Indent++;
            foreach (var attribute in attributes)
            {
                writer.WriteLine(attribute);
                written.Add(attribute);
            }
            writer.Indent--;
            writer.WriteLine("#endif");
        }

        var missed = attributesDisect.Values.SelectMany(a => a).Where(a => !written.Contains(a)).ToHashSet();
        if (missed.Count == 0)
        {
            return;
        }

        writer.WriteLine();
        writer.WriteLine("// Variant Attributes");
        foreach (var attribute in missed)
        {
            writer.WriteLine(attribute);
        }
    }

    private static bool IsIrrelevantCondition<T>(VfxCombo[] sfBlocks, Dictionary<(int Index, int State),
        HashSet<T>> perConditionStuff, (int Index, int State) condition, HashSet<T> stuff)
    {
        var rangeMin = sfBlocks[condition.Index].RangeMin;
        var rangeMax = sfBlocks[condition.Index].RangeMax;
        var stateIsIrrelevant = false;

        for (var j = rangeMin; j <= rangeMax; j++)
        {
            if (j == condition.State
            || !perConditionStuff.TryGetValue((condition.Index, j), out var otherAttributes)
            || !otherAttributes.SetEquals(stuff))
            {
                continue;
            }

            stateIsIrrelevant = true;
        }

        return stateIsIrrelevant;
    }

    private HashSet<string> GetZFrameAttributes(VfxStaticComboData zFrameFile, VfxVariableDescription[] paramBlocks)
    {
        var attributes = new HashSet<string>(zFrameFile.Attributes.Length);
        foreach (var attribute in zFrameFile.Attributes)
        {
            var type = ShaderUtilHelpers.GetVfxVariableTypeString(attribute.VfxType);
            string? value = null;

            if (attribute.ConstValue is not null)
            {
                value = attribute.VfxType switch
                {
                    VfxVariableType.Bool => (bool)attribute.ConstValue ? "true" : "false",
                    VfxVariableType.Int => ((int)attribute.ConstValue).ToString(CultureInfo.InvariantCulture),
                    VfxVariableType.Float => ((float)attribute.ConstValue).ToString(CultureInfo.InvariantCulture),
                    VfxVariableType.String => (string)attribute.ConstValue,
                    VfxVariableType.Float2 => ((Vector2)attribute.ConstValue).ToString().Trim('<', '>'),
                    VfxVariableType.Float3 => ((Vector3)attribute.ConstValue).ToString().Trim('<', '>'),
                    VfxVariableType.Float4 => ((Vector4)attribute.ConstValue).ToString().Trim('<', '>'),

                    _ => attribute.ConstValue.ToString(),
                };
            }
            else if (attribute.LinkedParameterIndex != -1)
            {
                value = paramBlocks[attribute.LinkedParameterIndex].Name;
            }
            else if (attribute.DynExpression!.Length > 0)
            {
                value = new VfxEval(attribute.DynExpression, Globals, omitReturnStatement: true, FeatureNames).DynamicExpressionResult!;
            }
            else
            {
                throw new InvalidOperationException("Whats the value of this attribute then?");
            }

            var attributeType = attribute.VfxType switch
            {
                VfxVariableType.Bool => "BoolAttribute",
                VfxVariableType.Int => "IntAttribute",
                VfxVariableType.Float => "FloatAttribute",
                VfxVariableType.Float2 => "Float2Attribute",
                VfxVariableType.Float3 => "Float3Attribute",
                VfxVariableType.Float4 => "Float4Attribute",
                VfxVariableType.String => "StringAttribute",
                VfxVariableType.Sampler2D => "TextureAttribute",
                _ => null
            };

            if (attributeType is not null)
            {
                attributes.Add($"{attributeType}({attribute.Name0}, {value});");
            }
            else
            {
                attributes.Add($"Attribute({type}, {attribute.Name0}, {value});");
            }
        }
        return attributes;
    }

    /// <summary>
    /// Gets the variant Z-frame parameters from the static combo data.
    /// </summary>
    public HashSet<int> GetVariantZFrameParameters(VfxStaticComboData zFrameFile)
    {
        var parameters = new HashSet<int>(VariantParameterIndices.Count);
        foreach (var writeseq in Enumerable.Concat(Enumerable.Repeat(zFrameFile.VariablesFromStaticCombo, 1), zFrameFile.DynamicComboVariables))
        {
            foreach (var field in writeseq.Fields)
            {
                parameters.Add(field.VariableIndex);
            }
        }

        parameters.IntersectWith(VariantParameterIndices);
        return parameters;
    }

    private void HandleFeatures(VfxCombo[] features, VfxRule[] constraints, IndentedTextWriter writer)
    {
        foreach (var feature in features)
        {
            var checkboxNames = feature.Strings.Length > 0
                ? " (" + string.Join(", ", feature.Strings.Select((x, i) => $"{i}=\"{x}\"")) + ")"
                : string.Empty;

            writer.WriteLine($"Feature( {feature.Name}, {feature.RangeMin}..{feature.RangeMax}{checkboxNames}, \"{feature.AliasName}\" );");
        }

        HandleRules(VfxRuleType.Feature,
                    [],
                    [],
                    constraints,
                    writer);
    }

    private void HandleStaticCombos(VfxCombo[] staticCombos, VfxRule[] constraints, IndentedTextWriter writer)
    {
        HandleCombos(VfxRuleType.Static, staticCombos, writer);
        HandleRules(VfxRuleType.Static,
                    staticCombos,
                    [],
                    constraints,
                    writer);
    }

    private void HandleDynamicCombos(
        VfxCombo[] staticCombos,
        VfxCombo[] dynamicCombos,
        VfxRule[] constraints,
        IndentedTextWriter writer)
    {
        HandleCombos(VfxRuleType.Dynamic, dynamicCombos, writer);
        HandleRules(VfxRuleType.Dynamic,
                    staticCombos,
                    dynamicCombos,
                    constraints,
                    writer);
    }

    private void HandleCombos(VfxRuleType comboType, VfxCombo[] combos, IndentedTextWriter writer)
    {
        foreach (var combo in combos)
        {
            if (combo.FeatureIndex != -1)
            {
                var fromFeature = comboType == VfxRuleType.Dynamic ? "FromFeature" : string.Empty;
                var equalsNotEqualsValue = (VfxStaticComboSourceType)combo.ComboSourceType switch
                {
                    VfxStaticComboSourceType.__SET_BY_FEATURE_EQ__ => $"=={combo.FeatureComparisonValue}",
                    VfxStaticComboSourceType.__SET_BY_FEATURE_NE__ => $"!={combo.FeatureComparisonValue}",
                    _ => string.Empty
                };

                writer.WriteLine($"{comboType}Combo{fromFeature}( {combo.Name}, {FeatureNames[combo.FeatureIndex]}{equalsNotEqualsValue} );");
            }
            else if (combo.RangeMax != 0)
            {
                writer.WriteLine($"{comboType}Combo( {combo.Name}, {combo.RangeMin}..{combo.RangeMax} );");
            }
            else
            {
                // Not sure about this one
                writer.WriteLine($"{comboType}Combo( {combo.Name}, {combo.RangeMax}, Sys( {combo.ComboSourceType} ) );");
            }
        }
    }

    private void HandleRules(VfxRuleType conditionalType, VfxCombo[] staticCombos, VfxCombo[] dynamicCombos, VfxRule[] constraints,
        IndentedTextWriter writer)
    {
        foreach (var constraint in constraints)
        {
            var constrainedNames = new List<string>(constraint.ConditionalTypes.Length);
            foreach ((var Type, var Index, var Value) in Enumerable.Zip(constraint.ConditionalTypes, constraint.Indices, constraint.Values))
            {
                if (Type == VfxRuleType.Unknown)
                {
                    break;
                }

                var name = Type switch
                {
                    VfxRuleType.Feature => FeatureNames[Index],
                    VfxRuleType.Static => staticCombos[Index].Name,
                    VfxRuleType.Dynamic => dynamicCombos[Index].Name,
                    _ => throw new UnreachableException(),
                };

                if (Value != -1)
                {
                    constrainedNames.Add($"{name}={Value}");
                }
                else
                {
                    constrainedNames.Add(name);
                }
            }

            // By value constraint
            // e.g. FeatureRule( Requires( F_REFRACT, F_TEXTURE_LAYERS=0, F_TEXTURE_LAYERS=1 ), "Refract requires Less than 2 Layers due to DX9" );

            var ruleName = constraint.Rule == VfxRuleMethod.AllowNum
                ? $"Allow{constraint.ExtraRuleData[0]}"
                : constraint.Rule.ToString();

            var rule = $"{ruleName}( {string.Join(", ", constrainedNames)} )";

            writer.WriteLine(conditionalType == VfxRuleType.Feature
                ? $"FeatureRule( {rule}, \"{constraint.Description}\" );"
                : $"{conditionalType}ComboRule( {rule} );"
            );
        }
    }

    private void HandleParameters(VfxVariableDescription[] paramBlocks, VfxTextureChannelProcessor[] channelBlocks, IndentedTextWriter writer)
    {
        if (paramBlocks.Length == 0)
        {
            return;
        }

        VariantParameterNames = [];
        var encountered = new HashSet<string>(paramBlocks.Length);
        foreach (var paramBlock in paramBlocks)
        {
            if (encountered.Contains(paramBlock.Name))
            {
                VariantParameterNames.Add(paramBlock.Name);
                continue;
            }

            encountered.Add(paramBlock.Name);
        }

        VariantParameterIndices = [];
        foreach (var paramBlock in paramBlocks)
        {
            if (VariantParameterNames.Contains(paramBlock.Name))
            {
                VariantParameterIndices.Add(paramBlock.BlockIndex);
            }
        }

        foreach (var byHeader in paramBlocks.GroupBy(p => p.UiGroup.Heading))
        {
            writer.WriteLine();
            if (!string.IsNullOrEmpty(byHeader.Key))
            {
                writer.WriteLine($"// {byHeader.Key}");
            }

            foreach (var param in byHeader.OrderBy(p => p.UiGroup.VariableOrder))
            {
                if (Options.CanReadStaticCombos && !Options.WriteParametersRaw && VariantParameterNames.Contains(param.Name))
                {
                    continue;
                }

                WriteParam(param, paramBlocks, channelBlocks, writer);
            }
        }
    }

    private void WriteParam(VfxVariableDescription param, VfxVariableDescription[] paramBlocks, VfxTextureChannelProcessor[] channelBlocks, IndentedTextWriter writer)
    {
        var annotations = new List<string>();

        if (param.RegisterType is VfxRegisterType.RenderState)
        {
            WriteState(writer, param);
        }
        else if (param.RegisterType is VfxRegisterType.SamplerState)
        {
            //WriteState(writer, param);
        }
        else if (param.RegisterType is VfxRegisterType.InputTexture)
        {
            WriteInputTexture(writer, param);
        }
        else if (param.ExtConstantBufferId == -1)
        {
            WriteVariable(param, paramBlocks, writer, annotations);
        }
        else if (param.RegisterType == VfxRegisterType.Texture)
        {
            WriteTexture(param, paramBlocks, channelBlocks, writer, annotations);
        }
    }

    enum Boolean
    {
        False,
        True
    }

    static readonly Dictionary<string, Type> RenderStateEnumSource = new()
    {
        // RsRasterizerStateDesc
        [nameof(VfxRenderStateInfoPixelShader.RsRasterizerStateDesc.FillMode)] = typeof(VfxRenderStateInfoPixelShader.RsRasterizerStateDesc.RsFillMode),
        [nameof(VfxRenderStateInfoPixelShader.RsRasterizerStateDesc.CullMode)] = typeof(VfxRenderStateInfoPixelShader.RsRasterizerStateDesc.RsCullMode),
        [nameof(VfxRenderStateInfoPixelShader.RsRasterizerStateDesc.DepthClipEnable)] = typeof(Boolean),
        [nameof(VfxRenderStateInfoPixelShader.RsRasterizerStateDesc.MultisampleEnable)] = typeof(Boolean),

        // RsDepthStencilStateDesc
        [nameof(VfxRenderStateInfoPixelShader.RsDepthStencilStateDesc.DepthFunc)] = typeof(VfxRenderStateInfoPixelShader.RsDepthStencilStateDesc.RsComparison),
        ["DepthEnable"] = typeof(Boolean),
        [nameof(VfxRenderStateInfoPixelShader.RsDepthStencilStateDesc.DepthWriteEnable)] = typeof(Boolean),

        // RsBlendStateDesc
        [nameof(VfxRenderStateInfoPixelShader.RsBlendStateDesc.AlphaToCoverageEnable)] = typeof(Boolean),
        [nameof(VfxRenderStateInfoPixelShader.RsBlendStateDesc.IndependentBlendEnable)] = typeof(Boolean),
        [nameof(VfxRenderStateInfoPixelShader.RsBlendStateDesc.SrgbWriteEnable)] = typeof(Boolean),
        [nameof(VfxRenderStateInfoPixelShader.RsBlendStateDesc.BlendEnable)] = typeof(Boolean),
        [nameof(VfxRenderStateInfoPixelShader.RsBlendStateDesc.SrcBlend)] = typeof(VfxRenderStateInfoPixelShader.RsBlendStateDesc.RsBlendMode),
        [nameof(VfxRenderStateInfoPixelShader.RsBlendStateDesc.BlendOp)] = typeof(VfxRenderStateInfoPixelShader.RsBlendStateDesc.RsBlendOp),
        [nameof(VfxRenderStateInfoPixelShader.RsBlendStateDesc.SrcBlendAlpha)] = typeof(VfxRenderStateInfoPixelShader.RsBlendStateDesc.RsBlendMode),
        [nameof(VfxRenderStateInfoPixelShader.RsBlendStateDesc.BlendOpAlpha)] = typeof(VfxRenderStateInfoPixelShader.RsBlendStateDesc.RsBlendOp),
        ["DstBlendAlpha"] = typeof(VfxRenderStateInfoPixelShader.RsBlendStateDesc.RsBlendMode),
        ["DstBlend"] = typeof(VfxRenderStateInfoPixelShader.RsBlendStateDesc.RsBlendMode),
        ["ColorWriteEnable"] = typeof(VfxRenderStateInfoPixelShader.RsBlendStateDesc.RsColorWriteEnableBits),
    };

    private void WriteState(IndentedTextWriter writer, VfxVariableDescription param)
    {
        string enumMapper(int v)
        {
            var enumSource = RenderStateEnumSource.GetValueOrDefault(param.Name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7'));
            return enumSource?.GetEnumName(v) ?? v.ToString(CultureInfo.InvariantCulture);
        }

        var stateValue = param.DynExp.Length > 0
            ? new VfxEval(param.DynExp, Globals, omitReturnStatement: true, FeatureNames, enumMapper).DynamicExpressionResult
            : enumMapper(param.IntDefs[0]);

        if (param.RegisterType == VfxRegisterType.RenderState)
        {
            writer.WriteLine("{0}({1}, {2});", param.RegisterType, param.Name, stateValue);
        }
        else
        {
            writer.WriteLine("{0}({1});", param.Name, stateValue);
        }
    }

    private void WriteVariable(VfxVariableDescription param, VfxVariableDescription[] paramBlocks, IndentedTextWriter writer, List<string> annotations)
    {
        var intDefsCutOff = 0;
        var floatDefsCutOff = 0;
        for (var i = 3; i >= 0; i--)
        {
            if (param.IntDefs[i] == 0)
            {
                intDefsCutOff++;
            }

            if (param.FloatDefs[i] == 0f)
            {
                floatDefsCutOff++;
            }
        }

        static string GetFuncName(string func, int cutOff)
            => cutOff == 3 ? func : func + (4 - cutOff);

        if (intDefsCutOff <= 3 || floatDefsCutOff <= 3)
        {
            if (floatDefsCutOff <= intDefsCutOff)
            {
                var defaults = string.Join(", ", param.FloatDefs[..^floatDefsCutOff]);
                annotations.Add($"{GetFuncName("Default", floatDefsCutOff)}({defaults});");
            }
            else
            {
                var defaults = string.Join(", ", param.IntDefs[..^intDefsCutOff]);
                annotations.Add($"{GetFuncName("Default", intDefsCutOff)}({defaults});");
            }
        }

        var intRangeCutOff = 0;
        var floatRangeCutOff = 0;
        for (var i = 3; i >= 0; i--)
        {
            if (param.IntMins[i] == 0 && param.IntMaxs[i] == 1)
            {
                intRangeCutOff++;
            }

            if (param.FloatMins[i] == 0f && param.FloatMaxs[i] == 1f)
            {
                floatRangeCutOff++;
            }
        }

        if (intRangeCutOff <= 3 && param.IntMins[0] != -VfxVariableDescription.IntInf)
        {
            if (floatRangeCutOff <= 3 && param.FloatMins[0] != -VfxVariableDescription.FloatInf)
            {
                var mins = string.Join(", ", param.FloatMins[..^floatRangeCutOff]);
                var maxs = string.Join(", ", param.FloatMaxs[..^floatRangeCutOff]);
                annotations.Add($"{GetFuncName("Range", floatRangeCutOff)}({mins}, {maxs});");
            }
            else
            {
                var mins = string.Join(", ", param.IntMins[..^intRangeCutOff]);
                var maxs = string.Join(", ", param.IntMaxs[..^intRangeCutOff]);
                annotations.Add($"{GetFuncName("Range", intRangeCutOff)}({mins}, {maxs});");
            }
        }

        HandleParameterAttribute(param, paramBlocks, annotations);

        if (param.UiStep > 0f)
        {
            annotations.Add($"UiStep({param.UiStep});");
        }

        if (param.UiType != UiType.None)
        {
            annotations.Add($"UiType({param.UiType});");
        }

        if (param.UiGroup.CompactString.Length > 0)
        {
            annotations.Add($"UiGroup(\"{param.UiGroup.CompactString}\");");
        }

        if (param.MaxRes > 0)
        {
            annotations.Add($"MaxRes(\"{param.MaxRes}\");");
        }

        var stageSpecificGlobals = new Lazy<string[]>(() => [.. paramBlocks.Select(p => p.Name)]);

        if (param.DynExp.Length > 0)
        {
            var dynEx = GetDynamicExpressionStringShared(param.DynExp, param, writer, FeatureNames, stageSpecificGlobals.Value);
            annotations.Add($"Expression({dynEx});");
        }

        if (param.UiVisibilityExp.Length > 0)
        {
            var dynEx = GetDynamicExpressionStringShared(param.UiVisibilityExp, param, writer, FeatureNames, stageSpecificGlobals.Value);
            annotations.Add($"UiVisibility({dynEx});");
        }

        writer.WriteLine($"{ShaderUtilHelpers.GetVfxVariableTypeString(param.VfxType)} {param.Name}{GetVfxAttributes(annotations)};");
    }

    private static void WriteInputTexture(IndentedTextWriter writer, VfxVariableDescription param)
    {
        if (param.RegisterType != VfxRegisterType.InputTexture)
        {
            throw new ArgumentException($"Expected parameter of type {VfxRegisterType.InputTexture}, got {param.RegisterType}", nameof(param));
        }

        UnexpectedMagicException.Assert(param.UiType == UiType.Texture, param.UiType);
        UnexpectedMagicException.Assert(param.ExtConstantBufferId == -1, param.ExtConstantBufferId);
        UnexpectedMagicException.Assert(param.RegisterElements == -1, param.RegisterElements);

        var mode = param.ColorMode == 0
            ? "Linear"
            : "Srgb";

        var imageSuffix = param.ImageSuffix.Length > 0
            ? "_" + param.ImageSuffix
            : string.Empty;

        var defaultValue = string.IsNullOrEmpty(param.DefaultInputTexture)
            ? $"Default4({string.Join(", ", param.FloatDefs)})"
            : $"\"{param.DefaultInputTexture}\"";

        writer.WriteLine($"CreateInputTexture2D({param.Name}, {mode}, {param.MinPrecisionBits}, \"{param.ImageProcessor}\", \"{imageSuffix}\", \"{param.UiGroup}\", {defaultValue});");
    }

    private void WriteTexture(VfxVariableDescription param, VfxVariableDescription[] paramBlocks, VfxTextureChannelProcessor[] channelBlocks, IndentedTextWriter writer, List<string> annotations)
    {
        for (var i = 0; i < param.ChannelCount; i++)
        {
            var index = param.ChannelIndices[i];
            if (index == -1 || index >= channelBlocks.Length)
            {
                throw new InvalidOperationException("Invalid channel block index");
            }

            annotations.Add(GetChannelFromChannelBlock(channelBlocks[index], paramBlocks));
        }

        HandleParameterAttribute(param, paramBlocks, annotations);

        if (param.ImageFormat != ImageFormat.UNKNOWN)
        {
            var format = Features!.VcsVersion switch
            {
                >= 66 => param.ImageFormat.ToString(),
                >= 64 => ((LegacyImageFormat)param.ImageFormat).ToString(),
                >= 61 when !Options.ForceWrite_UncertainEnumsAsInts => ((LegacyImageFormat)param.ImageFormat).ToString(),
                _ => ((int)param.ImageFormat).ToString(CultureInfo.InvariantCulture),
            };

            annotations.Add($"OutputFormat({format});");
        }

        annotations.Add($"SrgbRead({(param.SrgbRead ? "true" : "false")});");

        const string Sampler = "Sampler";
        var typeString = param.VfxType.ToString();

        typeString = typeString.StartsWith(Sampler, StringComparison.Ordinal)
            ? "Texture" + typeString[Sampler.Length..]
            : typeString; // not even a texture type?

        writer.WriteLine($"{typeString} {param.Name}{GetVfxAttributes(annotations)};");
    }

    private static void HandleParameterAttribute(VfxVariableDescription param, VfxVariableDescription[] paramBlocks, List<string> annotations)
    {
        if (param.VariableSource > VfxVariableSourceType.__SetByArtistAndExpression__)
        {
            annotations.Add($"Source({param.VariableSource});");

            if (param.SourceIndex != -1)
            {
                annotations.Add($"SourceArg({paramBlocks[param.SourceIndex].Name})");
            }
        }

        if (param.StringData.Length > 0)
        {
            if (param.UiType == UiType.Enum)
            {
                var optionOrOptions = param.StringData.Contains(',', StringComparison.Ordinal)
                    ? "UiOptions"
                    : "UiOption";
                annotations.Add($"{optionOrOptions}(\"{param.StringData}\");");
            }
            else
            {
                annotations.Add($"Attribute(\"{param.StringData}\");");
            }
        }
    }

    private static string GetVfxAttributes(List<string> attributes)
    {
        return attributes.Count > 0
            ? " < " + string.Join(" ", attributes) + " >"
            : string.Empty;
    }

    private static string GetDynamicExpressionStringShared(byte[] bytecode, VfxVariableDescription param, IndentedTextWriter writer, string[] features, string[] globals)
    {
        var dynEx = new VfxEval(bytecode, globals, omitReturnStatement: true, features).DynamicExpressionResult;
        dynEx = dynEx.Replace(param.Name, "this", StringComparison.Ordinal);
        dynEx = dynEx.Replace("\n", "\n" + new string('\t', writer.Indent + 1), StringComparison.Ordinal);
        return dynEx;
    }

    private static string GetChannelFromChannelBlock(VfxTextureChannelProcessor channelBlock, VfxVariableDescription[] paramBlocks)
    {
        var mode = channelBlock.ColorMode == 0
            ? "Linear"
            : "Srgb";

        var cutoff = Array.IndexOf(channelBlock.InputTextureIndices, -1);
        var inputs = string.Join(", ", channelBlock.InputTextureIndices[..cutoff].Select(idx => paramBlocks[idx].Name));

        return $"Channel({channelBlock.Channel}, {channelBlock.TexProcessorName}({inputs}), {mode});";
    }
}
