using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.Serialization.VfxEval;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.IO;

public sealed class ShaderExtract
{
    public readonly struct ShaderExtractParams
    {
        public bool CollapseCommonBuffers { get; init; }
        public bool FirstVsInput_Only { get; init; }

        public static readonly ShaderExtractParams Inspect = new()
        {
            CollapseCommonBuffers = true,
            FirstVsInput_Only = true,
        };

        public static readonly ShaderExtractParams Export = new()
        {
            CollapseCommonBuffers = false,
            FirstVsInput_Only = true,
        };

        public static HashSet<string> CommonBuffers => new()
        {
            "PerViewConstantBuffer_t",
            "PerViewConstantBufferVR_t",
            "PerViewLightingConstantBufferVr_t",
            "DotaGlobalParams_t",
        };
    }

    public ShaderFile Features { get; init; }
    public ShaderFile GeometryShader { get; init; }
    public ShaderFile VertexShader { get; init; }
    public ShaderFile PixelShader { get; init; }
    public ShaderFile ComputeShader { get; init; }
    public ShaderFile RaytracingShader { get; init; }

    private ShaderExtractParams Options { get; set; }
    private List<string> FeatureNames { get; set; }
    private string[] Globals { get; set; }

    public ShaderExtract(Resource resource)
        : this((SboxShader)resource.DataBlock)
    { }

    public ShaderExtract(SboxShader sboxShaderCollection)
    {
        Features = sboxShaderCollection.Features;
        GeometryShader = sboxShaderCollection.Geometry;
        VertexShader = sboxShaderCollection.Vertex;
        PixelShader = sboxShaderCollection.Pixel;
        ComputeShader = sboxShaderCollection.Compute;
    }

    public ShaderExtract(SortedDictionary<(VcsProgramType, string), ShaderFile> shaderCollection)
        : this(shaderCollection.Values)
    { }

    public ShaderExtract(IEnumerable<ShaderFile> shaderCollection)
    {
        foreach (var shader in shaderCollection)
        {
            if (shader.VcsProgramType == VcsProgramType.Features)
            {
                Features = shader;
            }

            if (shader.VcsProgramType == VcsProgramType.GeometryShader)
            {
                GeometryShader = shader;
            }

            if (shader.VcsProgramType == VcsProgramType.VertexShader)
            {
                VertexShader = shader;
            }

            if (shader.VcsProgramType == VcsProgramType.PixelShader)
            {
                PixelShader = shader;
            }

            if (shader.VcsProgramType == VcsProgramType.ComputeShader)
            {
                ComputeShader = shader;
            }

            if (shader.VcsProgramType == VcsProgramType.RaytracingShader)
            {
                RaytracingShader = shader;
            }
        }
    }

    public ContentFile ToContentFile()
    {
        var vfx = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(ToVFX(ShaderExtractParams.Export))
        };

        // TODO: includes..

        return vfx;
    }

    public string ToVFX()
    {
        return ToVFX(ShaderExtractParams.Inspect);
    }

    public string ToVFX(ShaderExtractParams @params)
    {
        // TODO: IndentedTextWriter
        FeatureNames = Features.SfBlocks.Select(f => f.Name).ToList();
        Globals = Features.ParamBlocks.Select(p => p.Name).ToArray();
        Options = @params;

        return "//=================================================================================================\n"
            + "// Reconstructed with VRF - https://vrf.steamdb.info/\n"
            + "//=================================================================================================\n"
            + HEADER()
            + MODES()
            + FEATURES()
            + COMMON()
            + GS()
            + VS()
            + PS()
            + CS()
            + RTX()
            ;
    }

    private string HEADER()
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine("HEADER");
        stringBuilder.AppendLine("{");

        stringBuilder.AppendLine($"\tDescription = \"{Features.FeaturesHeader.FileDescription}\";");
        stringBuilder.AppendLine($"\tDevShader = {(Features.FeaturesHeader.DevShader == 0 ? "false" : "true")};");
        stringBuilder.AppendLine($"\tVersion = {Features.FeaturesHeader.Version};");

        stringBuilder.AppendLine("}");

        return stringBuilder.ToString();
    }

    private string MODES()
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine();
        stringBuilder.AppendLine("MODES");
        stringBuilder.AppendLine("{");

        foreach (var mode in Features.FeaturesHeader.Modes)
        {
            if (string.IsNullOrEmpty(mode.Shader))
            {
                stringBuilder.AppendLine($"\t{mode.Name}({mode.StaticConfig});");
            }
            else
            {
                stringBuilder.AppendLine($"\t{mode.Name}(\"{mode.Shader}\");");
            }
        }

        stringBuilder.AppendLine("}");
        return stringBuilder.ToString();
    }

    private string FEATURES()
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine();
        stringBuilder.AppendLine("FEATURES");
        stringBuilder.AppendLine("{");

        HandleFeatures(Features.SfBlocks, Features.SfConstraintsBlocks, stringBuilder);

        stringBuilder.AppendLine("}");

        return stringBuilder.ToString();
    }

    private string COMMON()
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine();
        stringBuilder.AppendLine("COMMON");
        stringBuilder.AppendLine("{");

        stringBuilder.AppendLine("\t#include \"system.fxc\"");

        if (VertexShader is not null)
        {
            HandleCBuffers(VertexShader.BufferBlocks, stringBuilder);
        }

        HandleVsInput(stringBuilder);

        stringBuilder.AppendLine("}");

        return stringBuilder.ToString();
    }

    private void HandleVsInput(StringBuilder stringBuilder)
    {
        if (VertexShader is null)
        {
            return;
        }

        foreach (var i in Enumerable.Range(0, VertexShader.SymbolBlocks.Count))
        {
            stringBuilder.AppendLine();

            var index = VertexShader.SymbolBlocks.Count > 1 && !Options.FirstVsInput_Only
                ? $" ({i})"
                : string.Empty;
            stringBuilder.AppendLine($"\tstruct VS_INPUT{index}");
            stringBuilder.AppendLine("\t{");

            foreach (var symbol in VertexShader.SymbolBlocks[i].SymbolsDefinition)
            {
                var attributeVfx = symbol.Option.Length > 0 ? $" < Semantic({symbol.Option}); >" : string.Empty;
                // TODO: type
                stringBuilder.AppendLine($"\t\tfloat4 {symbol.Name} : {symbol.Type}{symbol.SemanticIndex}{attributeVfx};");
            }

            stringBuilder.AppendLine("\t};");

            if (Options.FirstVsInput_Only)
            {
                break;
            }
        }
    }

    private void HandleCBuffers(List<BufferBlock> bufferBlocks, StringBuilder stringBuilder)
    {
        foreach (var buffer in bufferBlocks)
        {
            stringBuilder.AppendLine();
            stringBuilder.Append("\tcbuffer " + buffer.Name);

            if (Options.CollapseCommonBuffers && ShaderExtractParams.CommonBuffers.Contains(buffer.Name))
            {
                stringBuilder.AppendLine(";");
                continue;
            }

            stringBuilder.AppendLine();
            stringBuilder.AppendLine("\t{");

            foreach (var member in buffer.BufferParams)
            {
                var dim1 = member.VectorSize > 1
                    ? member.VectorSize.ToString()
                    : string.Empty;

                var dim2 = member.Depth > 1
                    ? "x" + member.Depth.ToString()
                    : string.Empty;

                var array = member.Length > 1
                    ? "[" + member.Length.ToString() + "]"
                    : string.Empty;

                stringBuilder.AppendLine($"\t\tfloat{dim1}{dim2} {member.Name}{array};");
            }

            stringBuilder.AppendLine("\t};");
        }
    }

    private string GS()
    {
        if (GeometryShader is null)
        {
            return string.Empty;
        }

        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine();
        stringBuilder.AppendLine("GS");
        stringBuilder.AppendLine("{");

        HandleCommons(GeometryShader, stringBuilder);

        stringBuilder.AppendLine("}");

        return stringBuilder.ToString();
    }

    private string VS()
    {
        if (VertexShader is null)
        {
            return string.Empty;
        }

        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine();
        stringBuilder.AppendLine("VS");
        stringBuilder.AppendLine("{");

        HandleCommons(VertexShader, stringBuilder);

        stringBuilder.AppendLine("}");

        return stringBuilder.ToString();
    }

    private string PS()
    {
        if (PixelShader is null)
        {
            return string.Empty;
        }

        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine();
        stringBuilder.AppendLine("PS");
        stringBuilder.AppendLine("{");

        HandleCommons(PixelShader, stringBuilder);

        foreach (var mipmap in Features.MipmapBlocks)
        {
            stringBuilder.AppendLine($"\t{GetChannelFromMipmap(mipmap)}");
        }

        stringBuilder.AppendLine("}");
        return stringBuilder.ToString();
    }

    private string CS()
    {
        if (ComputeShader is null)
        {
            return string.Empty;
        }

        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine();
        stringBuilder.AppendLine("CS");
        stringBuilder.AppendLine("{");

        HandleCommons(ComputeShader, stringBuilder);

        stringBuilder.AppendLine("}");
        return stringBuilder.ToString();
    }

    private string RTX()
    {
        if (RaytracingShader is null)
        {
            return string.Empty;
        }

        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine();
        stringBuilder.AppendLine("RTX");
        stringBuilder.AppendLine("{");
        HandleCommons(RaytracingShader, stringBuilder);

        stringBuilder.AppendLine("}");
        return stringBuilder.ToString();
    }

    private void HandleCommons(ShaderFile shader, StringBuilder stringBuilder)
    {
        HandleStaticCombos(shader.SfBlocks, shader.SfConstraintsBlocks, stringBuilder);
        HandleDynamicCombos(shader.SfBlocks, shader.DBlocks, shader.DConstraintsBlocks, stringBuilder);
        HandleParameters(shader.ParamBlocks, stringBuilder);
    }

    private void HandleFeatures(List<SfBlock> features, List<SfConstraintsBlock> constraints, StringBuilder sb)
    {
        foreach (var feature in features)
        {
            var checkboxNames = feature.CheckboxNames.Count > 0
                ? " (" + string.Join(", ", feature.CheckboxNames.Select((x, i) => $"{i}=\"{x}\"")) + ")"
                : string.Empty;

            // Verify RangeMin
            sb.AppendLine($"\tFeature( {feature.Name}, {feature.RangeMin}..{feature.RangeMax}{checkboxNames}, \"{feature.Category}\" );");
        }

        HandleRules(ConditionalType.Feature,
                    Enumerable.Empty<ICombo>().ToList(),
                    Enumerable.Empty<ICombo>().ToList(),
                    constraints.Cast<IComboConstraints>().ToList(),
                    sb);
    }

    private void HandleStaticCombos(List<SfBlock> staticCombos, List<SfConstraintsBlock> constraints, StringBuilder sb)
    {
        HandleCombos(ConditionalType.Static, staticCombos.Cast<ICombo>().ToList(), sb);
        HandleRules(ConditionalType.Static,
                    staticCombos.Cast<ICombo>().ToList(),
                    Enumerable.Empty<ICombo>().ToList(),
                    constraints.Cast<IComboConstraints>().ToList(),
                    sb);
    }

    private void HandleDynamicCombos(List<SfBlock> staticCombos, List<DBlock> dynamicCombos, List<DConstraintsBlock> constraints, StringBuilder sb)
    {
        HandleCombos(ConditionalType.Dynamic, dynamicCombos.Cast<ICombo>().ToList(), sb);
        HandleRules(ConditionalType.Dynamic,
                    staticCombos.Cast<ICombo>().ToList(),
                    dynamicCombos.Cast<ICombo>().ToList(),
                    constraints.Cast<IComboConstraints>().ToList(),
                    sb);
    }

    private void HandleCombos(ConditionalType comboType, List<ICombo> combos, StringBuilder sb)
    {
        foreach (var staticCombo in combos)
        {
            if (staticCombo.FeatureIndex != -1)
            {
                sb.AppendLine($"\t{comboType}Combo( {staticCombo.Name}, {FeatureNames[staticCombo.FeatureIndex]}, Sys( ALL ) );");
                continue;
            }
            else if (staticCombo.RangeMax != 0)
            {
                sb.AppendLine($"\t{comboType}Combo( {staticCombo.Name}, {staticCombo.RangeMin}..{staticCombo.RangeMax}, Sys( ALL ) );");
            }
            else
            {
                sb.AppendLine($"\t{comboType}Combo( {staticCombo.Name}, {staticCombo.RangeMax}, Sys( {staticCombo.Sys} ) );");
            }
        }
    }

    private void HandleRules(ConditionalType conditionalType, List<ICombo> staticCombos, List<ICombo> dynamicCombos, List<IComboConstraints> constraints, StringBuilder sb)
    {
        foreach (var rule in HandleConstraintsY(staticCombos, dynamicCombos, constraints))
        {
            if (conditionalType == ConditionalType.Feature)
            {
                sb.AppendLine($"\tFeatureRule( {rule.Constraint}, \"{rule.Description}\" );");
            }
            else
            {
                sb.AppendLine($"\t{conditionalType}ComboRule( {rule.Constraint} );");
            }
        }
    }

    private IEnumerable<(string Constraint, string Description)> HandleConstraintsY(List<ICombo> staticCombos, List<ICombo> dynamicCombos, List<IComboConstraints> constraints)
    {
        foreach (var constraint in constraints)
        {
            var constrainedNames = new List<string>(constraint.ConditionalTypes.Length);
            foreach ((var Type, var Index) in Enumerable.Zip(constraint.ConditionalTypes, constraint.Indices))
            {
                if (Type == ConditionalType.Feature)
                {
                    constrainedNames.Add(FeatureNames[Index]);
                }
                else if (Type == ConditionalType.Static)
                {
                    constrainedNames.Add(staticCombos[Index].Name);
                }
                else if (Type == ConditionalType.Dynamic)
                {
                    constrainedNames.Add(dynamicCombos[Index].Name);
                }
            }

            // By value constraint
            if (constraint.Values.Length > 0)
            {
                if (constraint.Values.Length != constraint.ConditionalTypes.Length - 1)
                {
                    throw new InvalidOperationException("Expected to have 1 less value than conditionals.");
                }

                // The format for this not known, but should be something like:
                // FeatureRule( Requires1( F_REFRACT, F_TEXTURE_LAYERS=0, F_TEXTURE_LAYERS=1 ), "Refract requires Less than 2 Layers due to DX9" );
                constrainedNames = constrainedNames.Take(1).Concat(constrainedNames.Skip(1).Select((s, i) => $"{s}={constraint.Values[i]}")).ToList();
            }

            yield return ($"{constraint.Rule}{constraint.Range2[0]}( {string.Join(", ", constrainedNames)} )", constraint.Description);
        }
    }

    private void HandleParameters(List<ParamBlock> paramBlocks, StringBuilder sb)
    {
        foreach (var param in paramBlocks.OrderBy(x => x.Id == 255))
        {
            // BoolAttribute
            // FloatAttribute
            var attributes = new List<string>();

            // Todo merge these two's logic.
            // Render State
            if (param.ParamType is ParameterType.RenderState)
            {
                if (Enum.TryParse<RenderState>(param.Name, false, out var result))
                {
                    if ((byte)result != param.Id)
                    {
                        Console.WriteLine($"{param.Name} = {param.Id},");
                    }
                }
                else
                {
                    Console.WriteLine($"{param.Name} = {param.Id},");
                }

                if (param.DynExp.Length > 0)
                {
                    var dynEx = new VfxEval(param.DynExp, Globals, omitReturnStatement: true, FeatureNames).DynamicExpressionResult;
                    sb.AppendLine($"\tRenderState({param.Name}, {dynEx});");
                }
                else
                {
                    sb.AppendLine($"\tRenderState({param.Name}, {param.IntDefs[0]});");
                }
            }

            // Sampler State
            else if (param.ParamType is ParameterType.SamplerState)
            {
                if (Enum.TryParse<SamplerState>(param.Name, false, out var result))
                {
                    if ((byte)result != param.Id)
                    {
                        Console.WriteLine($"{param.Name} = {param.Id},");
                    }
                }
                else
                {
                    Console.WriteLine($"{param.Name} = {param.Id},");
                }

                sb.AppendLine($"\t{param.Name}({param.IntDefs[0]}); // Sampler");
            }

            // User input
            else if (param.Id == 255)
            {
                // Texture Input (unpacked)
                if (param.VecSize == -1)
                {
                    if (param.UiType != UiType.Texture)
                    {
                        throw new UnexpectedMagicException($"Expected {UiType.Texture}, got", (int)param.UiType, nameof(param.UiType));
                    }

                    if (param.ParamType != ParameterType.InputTexture)
                    {
                        throw new UnexpectedMagicException($"Expected {ParameterType.InputTexture}, got", (int)param.ParamType, nameof(param.ParamType));
                    }

                    var default4 = $"Default4({string.Join(", ", param.FloatDefs)})";

                    var mode = param.IntArgs1[2] == 0
                        ? "Linear"
                        : "Srgb";

                    var textureSuffix = param.Suffix.Length > 0
                        ? "_" + param.Suffix
                        : string.Empty;

                    sb.AppendLine($"\tCreateInputTexture2D({param.Name}, {mode}, {param.IntArgs1[3]}, \"{param.Command1}\", \"{textureSuffix}\", \"{param.UiGroup}\", {default4});");
                    // param.FileRef materials/default/default_cube.png
                    continue;
                }


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

                if (intDefsCutOff <= 3)
                {
                    if (floatDefsCutOff <= 3)
                    {
                        attributes.Add($"{GetFuncName("Default", floatDefsCutOff)}({string.Join(", ", param.FloatDefs[..^floatDefsCutOff])});");
                    }
                    else
                    {
                        var funcName = intDefsCutOff == 3 ? "Default" : "Default" + (4 - intDefsCutOff);
                        attributes.Add($"{GetFuncName("Default", intDefsCutOff)}({string.Join(", ", param.IntDefs[..^intDefsCutOff])});");
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

                if (intRangeCutOff <= 3 && param.IntMins[0] != -ParamBlock.IntInf)
                {
                    if (floatRangeCutOff <= 3 && param.FloatMins[0] != -ParamBlock.FloatInf)
                    {
                        attributes.Add($"{GetFuncName("Range", floatRangeCutOff)}({string.Join(", ", param.FloatMins[..^floatRangeCutOff])}, {string.Join(", ", param.FloatMaxs[..^floatRangeCutOff])})");
                    }
                    else
                    {
                        attributes.Add($"{GetFuncName("Range", intRangeCutOff)}({string.Join(", ", param.IntMins[..^intRangeCutOff])}, {string.Join(", ", param.IntMaxs[..^intRangeCutOff])});");
                    }
                }

                if (param.AttributeName.Length > 0)
                {
                    attributes.Add($"Attribute(\"{param.AttributeName}\");");
                }

                if (param.UiType != UiType.None)
                {
                    attributes.Add($"UiType({param.UiType});");
                }

                if (param.UiGroup.Length > 0)
                {
                    attributes.Add($"UiGroup(\"{param.UiGroup}\");");
                }

                if (param.DynExp.Length > 0)
                {
                    var globals = paramBlocks.Select(p => p.Name).ToArray();
                    var dynEx = new VfxEval(param.DynExp, globals, omitReturnStatement: true, FeatureNames).DynamicExpressionResult.Replace($"EVAL[{param.Name}]", "this");
                    attributes.Add($"Expression({dynEx});");
                }

                var attributesVfx = attributes.Count > 0
                    ? " < " + string.Join(" ", attributes) + " > "
                    : string.Empty;

                sb.AppendLine($"\t{Vfx.Types.GetValueOrDefault(param.VfxType, $"unkntype{param.VfxType}")} {param.Name}{attributesVfx};");
            }
            else
            {

            }
        }
    }

    private string GetChannelFromMipmap(MipmapBlock mipmapBlock)
    {
        var mode = mipmapBlock.ColorMode == 0
            ? "Linear"
            : "Srgb";

        var cutoff = Array.IndexOf(mipmapBlock.InputTextureIndices, -1);
        var inputs = string.Join(", ", mipmapBlock.InputTextureIndices[..cutoff].Select(idx => Features.ParamBlocks[idx].Name));

        return $"Channel( {mipmapBlock.Channel}, {mipmapBlock.Name}( {inputs} ) );";
    }
}
