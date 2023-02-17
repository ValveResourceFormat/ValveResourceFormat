using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.Serialization.VfxEval;
using ValveResourceFormat.Utils;
using System.Globalization;

namespace ValveResourceFormat.IO;

public sealed class ShaderExtract
{
    public readonly struct ShaderExtractParams
    {
        public bool CollapseBuffers_InInclude { get; init; }
        public bool BufferIncludes_AsSeparateFiles { get; init; }
        public bool CollapseBuffers_InPlace { get; init; }
        public static HashSet<string> BuffersToCollapse => new()
        {
            "PerViewConstantBuffer_t",
            "PerViewConstantBufferVR_t",
            "PerViewLightingConstantBufferVr_t",
            "DotaGlobalParams_t",
        };

        public bool ForceWrite_UncertainEnumsAsInts { get; init; }
        public bool NoHungarianTypeGuessing { get; init; }
        public int ZFrameReadingCap { get; init; }
        public bool StaticComboAttributes_NoSeparateGlobals { get; init; }
        public bool StaticComboAttributes_NoConditionalReduce { get; init; }

        public static readonly ShaderExtractParams Inspect = new()
        {
            CollapseBuffers_InPlace = true,
            ZFrameReadingCap = 512,
        };

        public static readonly ShaderExtractParams Export = new()
        {
            CollapseBuffers_InInclude = true,
            BufferIncludes_AsSeparateFiles = true,
            ZFrameReadingCap = -1,
        };
    }

    public ShaderCollection Shaders { get; init; }

    public ShaderFile Features => Shaders.Features;
    public ShaderFile Vertex => Shaders.Vertex;
    public ShaderFile Geometry => Shaders.Geometry;
    public ShaderFile Domain => Shaders.Domain;
    public ShaderFile Hull => Shaders.Hull;
    public ShaderFile Pixel => Shaders.Pixel;
    public ShaderFile Compute => Shaders.Compute;
    public ShaderFile PixelShaderRenderState => Shaders.PixelShaderRenderState;
    public ShaderFile Raytracing => Shaders.Raytracing;

    private IReadOnlyList<string> FeatureNames { get; set; }
    private string[] Globals { get; set; }

    private ShaderExtractParams Options { get; set; }
    private Dictionary<string, IndentedTextWriter> IncludeWriters { get; set; }

    public ShaderExtract(Resource resource)
        : this((SboxShader)resource.DataBlock)
    { }

    public ShaderExtract(SboxShader sboxShaderCollection)
        : this(sboxShaderCollection.Shaders)
    { }

    public ShaderExtract(ShaderCollection shaderCollection)
    {
        Shaders = shaderCollection;

        if (Features == null)
        {
            throw new InvalidOperationException("Shader extract cannot continue without at least a features file.");
        }

        FeatureNames = Features.SfBlocks.Select(f => f.Name).ToList();
        Globals = Features.ParamBlocks.Select(p => p.Name).ToArray();
    }

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

    public string GetVfxFileName()
        => GetVfxNameFromShaderFile(Features);

    private static string GetVfxNameFromShaderFile(ShaderFile shaderFile)
    {
        return shaderFile.ShaderName + ".vfx";
    }

    private static string GetIncludeName(string bufferName)
    {
        return $"common/{bufferName}.fxc";
    }

    public string ToVFX()
    {
        return ToVFXInternal(ShaderExtractParams.Inspect);
    }

    public (string VfxContent, IDictionary<string, string> Includes) ToVFX(ShaderExtractParams options)
    {
        return (ToVFXInternal(options), IncludeWriters.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString()));
    }

    private string ToVFXInternal(ShaderExtractParams options)
    {
        Options = options;
        IncludeWriters = new();

        return "//=================================================================================================\n"
            + "// Reconstructed with VRF - https://vrf.steamdb.info/\n"
            + "//=================================================================================================\n"
            + HEADER()
            + MODES()
            + FEATURES()
            + COMMON()
            + VS()
            + GS()
            + HS()
            + DS()
            + PS()
            + CS()
            + RTX()
            ;
    }
    private string HEADER()
    {
        using var writer = new IndentedTextWriter();
        writer.WriteLine(nameof(HEADER));
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine($"Description = \"{Features.FeaturesHeader.FileDescription}\";");
        writer.WriteLine($"DevShader = {(Features.FeaturesHeader.DevShader == 0 ? "false" : "true")};");
        writer.WriteLine($"Version = {Features.FeaturesHeader.Version};");

        writer.Indent--;
        writer.WriteLine("}");

        return writer.ToString();
    }

    private string MODES()
    {
        using var writer = new IndentedTextWriter();
        writer.WriteLine(nameof(MODES));
        writer.WriteLine("{");
        writer.Indent++;

        foreach (var mode in Features.FeaturesHeader.Modes)
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
        return writer.ToString();
    }

    private string FEATURES()
    {
        using var writer = new IndentedTextWriter();
        writer.WriteLine();
        writer.WriteLine(nameof(FEATURES));
        writer.WriteLine("{");
        writer.Indent++;

        HandleFeatures(Features.SfBlocks, Features.SfConstraintBlocks, writer);

        writer.Indent--;
        writer.WriteLine("}");

        return writer.ToString();
    }

    private string COMMON()
    {
        using var writer = new IndentedTextWriter();
        writer.WriteLine();
        writer.WriteLine(nameof(COMMON));
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine("#include \"system.fxc\"");

        if (Vertex is not null)
        {
            WriteCBuffers(Vertex.BufferBlocks, writer);
        }

        WriteVsInput(writer);

        writer.Indent--;
        writer.WriteLine("}");

        return writer.ToString();
    }

    private void WriteVsInput(IndentedTextWriter writer)
    {
        if (Vertex is null)
        {
            return;
        }

        var symbols = new List<(string Name, string Type, string Option, int SemanticIndex)>();
        var masks = new List<bool[]>();
        var maxNameLength = 0;
        var maxSemanticLength = 0;

        foreach (var i in Enumerable.Range(0, Vertex.SymbolBlocks.Count))
        {
            for (var j = 0; j < Vertex.SymbolBlocks[i].SymbolsDefinition.Count; j++)
            {
                var symbol = Vertex.SymbolBlocks[i].SymbolsDefinition[j];
                var val = (symbol.Name, symbol.Type, symbol.Option, symbol.SemanticIndex);
                var existingIndex = symbols.IndexOf(val);
                if (existingIndex == -1)
                {
                    symbols.Insert(j, val);
                    var mask = new bool[Vertex.SymbolBlocks.Count];
                    mask[i] = true;
                    masks.Insert(j, mask);
                    maxNameLength = Math.Max(maxNameLength, symbol.Name.Length);
                    maxSemanticLength = Math.Max(maxSemanticLength, symbol.Type.Length);
                }
                else
                {
                    masks[existingIndex][i] = true;
                }
            }
        }

        ConfigMappingSParams staticConfig = new(Vertex);
        ConfigMappingDParams dynamicConfig = new(Vertex);

        var perConditionVsInputBlocks = new Dictionary<(string Name, int State), HashSet<int>>(staticConfig.SumStates + dynamicConfig.SumStates);

        foreach (var i in Enumerable.Range(0, Vertex.GetZFrameCount()))
        {
            if (Options.ZFrameReadingCap >= 0 && i >= Options.ZFrameReadingCap)
            {
                break;
            }

            using var zFrame = Vertex.GetZFrameFileByIndex(i);
            var staticConfigState = staticConfig.GetConfigState(zFrame.ZframeId);

            foreach (var vsEnd in zFrame.VsEndBlocks)
            {
                var dynamicConfigState = dynamicConfig.GetConfigState(vsEnd.BlockIdRef);
                var vsInputId = zFrame.VShaderInputs[vsEnd.BlockIdRef];

                for (var j = 0; j < staticConfigState.Length; j++)
                {
                    var staticCondition = (Vertex.SfBlocks[j].Name, staticConfigState[j]);
                    if (!perConditionVsInputBlocks.TryGetValue(staticCondition, out var staticVsBlocks))
                    {
                        staticVsBlocks = new HashSet<int>(Vertex.SymbolBlocks.Count);
                        perConditionVsInputBlocks.Add(staticCondition, staticVsBlocks);
                    }

                    staticVsBlocks.Add(vsInputId);

                    for (var k = 0; k < dynamicConfigState.Length; k++)
                    {
                        var dynamicCondition = (Vertex.DBlocks[k].Name, dynamicConfigState[k]);
                        if (!perConditionVsInputBlocks.TryGetValue(dynamicCondition, out var dynamicVsBlocks))
                        {
                            dynamicVsBlocks = new HashSet<int>(Vertex.SymbolBlocks.Count);
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

                    if (symbol.Option.Contains("UV", StringComparison.OrdinalIgnoreCase))
                    {
                        type = "float2";
                    }

                    type = $"{type,-7}";
                }

                var attributeVfx = symbol.Option.Length > 0 ? $" < Semantic( {symbol.Option} ); >" : string.Empty;
                var nameAlignSpaces = new string(' ', maxNameLength - symbol.Name.Length);
                var semanticAlignSpaces = new string(' ', maxSemanticLength - symbol.Type.Length);

                var field = $"{type}{symbol.Name}{nameAlignSpaces} : {symbol.Type}{symbol.SemanticIndex,-2}{semanticAlignSpaces}{attributeVfx};";
                writer.Write($"{field,-94}");

                if (masks[i].All(x => x) || Options.ZFrameReadingCap == 0)
                {
                    writer.WriteLine();
                    continue;
                }

                var conditions = new List<string>();
                foreach (var (condition, VsInputIds) in perConditionVsInputBlocks)
                {
                    if (VsInputIds.Count == Vertex.SymbolBlocks.Count)
                    {
                        continue;
                    }

                    if (VsInputIds.All(x => masks[i][x]))
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

    private void WriteCBuffers(List<BufferBlock> bufferBlocks, IndentedTextWriter writer)
    {
        foreach (var buffer in bufferBlocks)
        {
            if (ShaderExtractParams.BuffersToCollapse.Contains(buffer.Name))
            {
                if (Options.CollapseBuffers_InPlace)
                {
                    writer.WriteLine("cbuffer " + buffer.Name + ";");
                    continue;
                }

                if (Options.CollapseBuffers_InInclude && IncludeWriters is not null)
                {
                    var includeName = Options.BufferIncludes_AsSeparateFiles
                        ? GetIncludeName(buffer.Name)
                        : "common.fxc";

                    if (!IncludeWriters.TryGetValue(includeName, out var includeWriter))
                    {
                        includeWriter = new IndentedTextWriter();
                        IncludeWriters.Add(includeName, includeWriter);
                        writer.WriteLine($"#include \"{includeName}\"");
                    }

                    WriteCBuffer(includeWriter, buffer);
                    continue;
                }
            }

            writer.WriteLine();
            WriteCBuffer(writer, buffer);
        }
    }

    private void WriteCBuffer(IndentedTextWriter writer, BufferBlock buffer)
    {
        writer.WriteLine("cbuffer " + buffer.Name);
        writer.WriteLine("{");
        writer.Indent++;

        foreach (var member in buffer.BufferParams)
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

    private string VS()
        => HandleStageShared(Vertex, nameof(VS));

    private string GS()
        => HandleStageShared(Geometry, nameof(GS));

    private string HS()
        => HandleStageShared(Hull, nameof(HS));

    private string DS()
        => HandleStageShared(Domain, nameof(DS));

    private string PS()
        => HandleStageShared(Pixel, nameof(PS));

    private string CS()
        => HandleStageShared(Compute, nameof(CS));

    private string RTX()
        => HandleStageShared(Raytracing, nameof(RTX));

    private string HandleStageShared(ShaderFile shader, string stageName)
    {
        if (shader is null)
        {
            return string.Empty;
        }

        using var writer = new IndentedTextWriter();
        writer.WriteLine();
        writer.WriteLine(stageName);
        writer.WriteLine("{");
        writer.Indent++;

        HandleStaticCombos(shader.SfBlocks, shader.SfConstraintBlocks, writer);
        HandleDynamicCombos(shader.SfBlocks, shader.DBlocks, shader.DConstraintBlocks, writer);
        HandleParameters(shader.ParamBlocks, shader.ChannelBlocks, writer);

        if (shader.VcsProgramType == VcsProgramType.PixelShader && PixelShaderRenderState is not null)
        {
            HandleParameters(PixelShaderRenderState.ParamBlocks, PixelShaderRenderState.ChannelBlocks, writer);
        }

        // zframe stuff
        HandleZFrames(shader, writer);

        writer.Indent--;
        writer.WriteLine("}");
        return writer.ToString();
    }

    public class ConfigKeyComparer : IEqualityComparer<int[]>
    {
        public bool Equals(int[] x, int[] y)
        {
            return x == null || y == null
                ? x == null && y == null
                : x.SequenceEqual(y);
        }

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

    private void HandleZFrames(ShaderFile shader, IndentedTextWriter writer)
    {
        if (shader.GetZFrameCount() == 0 || Options.ZFrameReadingCap == 0)
        {
            return;
        }

        ConfigMappingSParams configGen = new(shader);

        var attributesDisect = new Dictionary<int[], HashSet<string>>(2 ^ Math.Max(0, shader.SfBlocks.Count - 1), new ConfigKeyComparer());
        var perConditionAttributes = new Dictionary<(int Index, int State), HashSet<string>>(configGen.SumStates);

        foreach (var i in Enumerable.Range(0, shader.GetZFrameCount()))
        {
            if (Options.ZFrameReadingCap >= 0 && i >= Options.ZFrameReadingCap)
            {
                break;
            }

            using var zFrame = shader.GetZFrameFileByIndex(i);
            var zframeAttributes = new HashSet<string>();
            GetZFrameAttributes(zFrame, shader.ParamBlocks, zframeAttributes);

            var configState = configGen.GetConfigState(zFrame.ZframeId);
            attributesDisect[configState] = zframeAttributes;
        }

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

                var conditions = string.Join(" && ", config.Select((v, i) => $"{shader.SfBlocks[i].Name} == {v}"));
                writer.WriteLine($"#if ({conditions})");
                writer.Indent++;
                foreach (var attribute in attributes)
                {
                    writer.WriteLine(attribute);
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
                    perConditionAttributes[key] = new HashSet<string>(attributes);
                }
            }
        }

        foreach (var (condition, attributes) in perConditionAttributes)
        {
            var rangeMin = shader.SfBlocks[condition.Index].RangeMin;
            var rangeMax = shader.SfBlocks[condition.Index].RangeMax;
            var stateIsIrrelevant = false;

            for (var j = rangeMin; j <= rangeMax; j++)
            {
                if (j == condition.State
                || !perConditionAttributes.TryGetValue((condition.Index, j), out var otherAttributes)
                || !otherAttributes.SetEquals(attributes))
                {
                    continue;
                }

                stateIsIrrelevant = true;
            }

            if (stateIsIrrelevant)
            {
                continue;
            }

            var stateName = shader.SfBlocks[condition.Index].Name;
            writer.WriteLine($"#if ({stateName} == {condition.State})");
            writer.Indent++;
            foreach (var attribute in attributes)
            {
                writer.WriteLine(attribute);
            }
            writer.Indent--;
            writer.WriteLine("#endif");
        }
    }

    private void GetZFrameAttributes(ZFrameFile zFrameFile, IReadOnlyList<ParamBlock> paramBlocks, HashSet<string> attributes)
    {
        foreach (var attribute in zFrameFile.Attributes)
        {
            var type = Vfx.GetTypeName(attribute.VfxType);
            string value = null;

            if (attribute.ConstValue is not null)
            {
                value = attribute.VfxType switch
                {
                    Vfx.Type.Bool => (bool)attribute.ConstValue ? "true" : "false",
                    Vfx.Type.Int => ((int)attribute.ConstValue).ToString(CultureInfo.InvariantCulture),
                    Vfx.Type.Float => ((float)attribute.ConstValue).ToString(CultureInfo.InvariantCulture),
                    _ => attribute.ConstValue.ToString(),
                };
            }
            else if (attribute.LinkedParameterIndex != 255)
            {
                value = paramBlocks[attribute.LinkedParameterIndex].Name;
            }
            else if (attribute.DynExpression.Length > 0)
            {
                value = new VfxEval(attribute.DynExpression, Globals, omitReturnStatement: true, FeatureNames).DynamicExpressionResult;
            }
            else
            {
                throw new InvalidOperationException("Whats the value of this attribute then?");
            }

            var attributeType = attribute.VfxType switch
            {
                Vfx.Type.Bool => "BoolAttribute",
                Vfx.Type.Int => "IntAttribute",
                Vfx.Type.Float => "FloatAttribute",
                Vfx.Type.Float2 => "Float2Attribute",
                Vfx.Type.Float3 => "Float3Attribute",
                Vfx.Type.Sampler2D => "TextureAttribute",
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
    }

    private void HandleFeatures(List<SfBlock> features, List<ConstraintBlock> constraints, IndentedTextWriter writer)
    {
        foreach (var feature in features)
        {
            var checkboxNames = feature.CheckboxNames.Count > 0
                ? " (" + string.Join(", ", feature.CheckboxNames.Select((x, i) => $"{i}=\"{x}\"")) + ")"
                : string.Empty;

            writer.WriteLine($"Feature( {feature.Name}, {feature.RangeMin}..{feature.RangeMax}{checkboxNames}, \"{feature.Category}\" );");
        }

        HandleRules(ConditionalType.Feature,
                    Enumerable.Empty<ICombo>().ToList(),
                    Enumerable.Empty<ICombo>().ToList(),
                    constraints,
                    writer);
    }

    private void HandleStaticCombos(List<SfBlock> staticCombos, List<ConstraintBlock> constraints, IndentedTextWriter writer)
    {
        HandleCombos(ConditionalType.Static, staticCombos.Cast<ICombo>().ToList(), writer);
        HandleRules(ConditionalType.Static,
                    staticCombos.Cast<ICombo>().ToList(),
                    Enumerable.Empty<ICombo>().ToList(),
                    constraints,
                    writer);
    }

    private void HandleDynamicCombos(List<SfBlock> staticCombos, List<DBlock> dynamicCombos, List<ConstraintBlock> constraints, IndentedTextWriter writer)
    {
        HandleCombos(ConditionalType.Dynamic, dynamicCombos.Cast<ICombo>().ToList(), writer);
        HandleRules(ConditionalType.Dynamic,
                    staticCombos.Cast<ICombo>().ToList(),
                    dynamicCombos.Cast<ICombo>().ToList(),
                    constraints,
                    writer);
    }

    private void HandleCombos(ConditionalType comboType, List<ICombo> combos, IndentedTextWriter writer)
    {
        foreach (var combo in combos)
        {
            if (combo.FeatureIndex != -1)
            {
                var fromFeature = comboType == ConditionalType.Dynamic ? "FromFeature" : string.Empty;
                writer.WriteLine($"{comboType}Combo{fromFeature}( {combo.Name}, {FeatureNames[combo.FeatureIndex]} );");
            }
            else if (combo.RangeMax != 0)
            {
                writer.WriteLine($"{comboType}Combo( {combo.Name}, {combo.RangeMin}..{combo.RangeMax} );");
            }
            else
            {
                // Not so sure about this one
                writer.WriteLine($"{comboType}Combo( {combo.Name}, {combo.RangeMax}, Sys( {combo.Arg3} ) );");
            }
        }
    }

    private void HandleRules(ConditionalType conditionalType, List<ICombo> staticCombos, List<ICombo> dynamicCombos, List<ConstraintBlock> constraints, IndentedTextWriter writer)
    {
        foreach (var rule in HandleConstraintsY(staticCombos, dynamicCombos, constraints))
        {
            if (conditionalType == ConditionalType.Feature)
            {
                writer.WriteLine($"FeatureRule( {rule.Constraint}, \"{rule.Description}\" );");
            }
            else
            {
                writer.WriteLine($"{conditionalType}ComboRule( {rule.Constraint} );");
            }
        }
    }

    private IEnumerable<(string Constraint, string Description)> HandleConstraintsY(List<ICombo> staticCombos, List<ICombo> dynamicCombos, List<ConstraintBlock> constraints)
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
            // e.g. FeatureRule( Requires1( F_REFRACT, F_TEXTURE_LAYERS=0, F_TEXTURE_LAYERS=1 ), "Refract requires Less than 2 Layers due to DX9" );
            if (constraint.Values.Length > 0)
            {
                if (constraint.Values.Length != constraint.ConditionalTypes.Length - 1)
                {
                    throw new InvalidOperationException("Expected to have 1 less value than conditionals.");
                }

                constrainedNames = constrainedNames.Take(1).Concat(constrainedNames.Skip(1).Select((s, i) => $"{s} == {constraint.Values[i]}")).ToList();
            }

            yield return ($"{constraint.Rule}{constraint.Range2[0]}( {string.Join(", ", constrainedNames)} )", constraint.Description);
        }
    }

    private void HandleParameters(List<ParamBlock> paramBlocks, List<ChannelBlock> channelBlocks, IndentedTextWriter writer)
    {
        if (paramBlocks.Count == 0)
        {
            return;
        }

        foreach (var byHeader in paramBlocks.GroupBy(p => ParseUiGroup(p.UiGroup).Heading))
        {
            writer.WriteLine();
            if (!string.IsNullOrEmpty(byHeader.Key))
            {
                writer.WriteLine($"// {byHeader.Key}");
            }

            foreach (var param in byHeader.OrderBy(p => ParseUiGroup(p.UiGroup).VariableOrder))
            {
                WriteParam(param);
            }
        }

        void WriteParam(ParamBlock param)
        {
            var attributes = new List<string>();

            // Render State
            if (param.ParamType is ParameterType.RenderState)
            {
                WriteState(writer, param);
            }
            // Sampler State
            else if (param.ParamType is ParameterType.SamplerState)
            {
                //WriteState(writer, param);
            }

            // User input
            else if (param.ParamType == ParameterType.InputTexture)
            {
                // Texture Input (unpacked)
                WriteInputTexture(writer, param);
            }
            else if (param.Id == 255)
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

                if (intDefsCutOff <= 3)
                {
                    if (floatDefsCutOff <= 3)
                    {
                        var defaults = string.Join(", ", param.FloatDefs[..^floatDefsCutOff]);
                        attributes.Add($"{GetFuncName("Default", floatDefsCutOff)}({defaults});");
                    }
                    else
                    {
                        var funcName = intDefsCutOff == 3 ? "Default" : "Default" + (4 - intDefsCutOff);
                        var defaults = string.Join(", ", param.IntDefs[..^intDefsCutOff]);
                        attributes.Add($"{GetFuncName("Default", intDefsCutOff)}({defaults});");
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
                        var mins = string.Join(", ", param.FloatMins[..^floatRangeCutOff]);
                        var maxs = string.Join(", ", param.FloatMaxs[..^floatRangeCutOff]);
                        attributes.Add($"{GetFuncName("Range", floatRangeCutOff)}({mins}, {maxs});");
                    }
                    else
                    {
                        var mins = string.Join(", ", param.IntMins[..^intRangeCutOff]);
                        var maxs = string.Join(", ", param.IntMaxs[..^intRangeCutOff]);
                        attributes.Add($"{GetFuncName("Range", intRangeCutOff)}({mins}, {maxs});");
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
                    var dynEx = new VfxEval(param.DynExp, globals, omitReturnStatement: true, FeatureNames).DynamicExpressionResult;
                    dynEx = dynEx.Replace(param.Name, "this", StringComparison.Ordinal);
                    dynEx = dynEx.Replace("\n", "\n" + new string('\t', writer.Indent + 1), StringComparison.Ordinal);

                    attributes.Add($"Expression({dynEx});");
                }

                writer.WriteLine($"{Vfx.GetTypeName(param.VfxType)} {param.Name}{GetVfxAttributes(attributes)};");
            }
            else if (param.ParamType == ParameterType.Texture)
            {
                if (param.ImageFormat != -1 && param.ChannelCount > 0)
                {
                    for (var i = 0; i < param.ChannelCount; i++)
                    {
                        var index = param.ChannelIndices[i];
                        if (index == -1 || index >= channelBlocks.Count)
                        {
                            throw new InvalidOperationException("Invalid channel block index");
                        }

                        attributes.Add(GetChannelFromChannelBlock(channelBlocks[index], paramBlocks));
                    }

                    var format = Features.VcsVersion switch
                    {
                        >= 66 => ((ImageFormatV66)param.ImageFormat).ToString(),
                        >= 64 => ((ImageFormat)param.ImageFormat).ToString(),
                        _ when !Options.ForceWrite_UncertainEnumsAsInts => ((ImageFormat)param.ImageFormat).ToString(),
                        _ => param.ImageFormat.ToString(CultureInfo.InvariantCulture),
                    };

                    attributes.Add($"OutputFormat({format});");
                    attributes.Add($"SrgbRead({(param.Id == 0 ? "false" : "true")});");

                    writer.WriteLine($"CreateTexture2DWithoutSampler({param.Name}){GetVfxAttributes(attributes)};");
                }
            }
            else
            {
                Console.WriteLine($"Unknown parameter type: {param.ParamType}");
            }
        }
    }

    private static void WriteInputTexture(IndentedTextWriter writer, ParamBlock param)
    {
        if (param.ParamType != ParameterType.InputTexture)
        {
            throw new ArgumentException($"Expected parameter of type {ParameterType.InputTexture}, got {param.ParamType}", nameof(param));
        }

        UnexpectedMagicException.ThrowIfNotEqual(UiType.Texture, param.UiType, nameof(param.UiType));
        UnexpectedMagicException.ThrowIfNotEqual(255, param.Id, nameof(param.Id));
        UnexpectedMagicException.ThrowIfNotEqual(-1, param.VecSize, nameof(param.VecSize));

        var mode = param.ColorMode == 0
            ? "Linear"
            : "Srgb";

        var imageSuffix = param.ImageSuffix.Length > 0
            ? "_" + param.ImageSuffix
            : string.Empty;

        var defaultValue = string.IsNullOrEmpty(param.FileRef)
            ? $"Default4({string.Join(", ", param.FloatDefs)})"
            : $"\"{param.FileRef}\"";

        writer.WriteLine($"CreateInputTexture2D({param.Name}, {mode}, {param.Arg12}, \"{param.ImageProcessor}\", \"{imageSuffix}\", \"{param.UiGroup}\", {defaultValue});");
    }

    private void WriteState(IndentedTextWriter writer, ParamBlock param)
    {
        var stateValue = param.DynExp.Length > 0
            ? new VfxEval(param.DynExp, Globals, omitReturnStatement: true, FeatureNames).DynamicExpressionResult
            : param.IntDefs[0].ToString(CultureInfo.InvariantCulture);

        if (param.ParamType == ParameterType.RenderState)
        {
            writer.WriteLine("{0}({1}, {2});", param.ParamType, param.Name, stateValue);
        }
        else
        {
            writer.WriteLine("{0}({1});", param.Name, stateValue);
        }
    }

    private static string GetVfxAttributes(List<string> attributes)
    {
        return attributes.Count > 0
            ? " < " + string.Join(" ", attributes) + " > "
            : string.Empty;
    }

    private static (string Heading, int HeadingOrder, string Group, int GroupOrder, int VariableOrder) ParseUiGroup(string uiGroup)
    {
        (string Heading, int HeadingOrder, string Group, int GroupOrder, int VariableOrder) parsed = (string.Empty, 0, string.Empty, 0, 0);

        if (uiGroup.Length == 0)
        {
            return parsed;
        }

        var parts = uiGroup.Split("/", 3, StringSplitOptions.TrimEntries).Select(p => p.Split(",", 2, StringSplitOptions.TrimEntries)).ToArray();
        for (var i = 0; i < parts.Length; i++)
        {
            var name = string.Empty;
            var order = 0;

            for (var j = 0; j < parts[i].Length; j++)
            {
                if (!int.TryParse(parts[i][j], out order))
                {
                    name = parts[i][j];
                }
            }

            switch (i)
            {
                case 0:
                    parsed.Heading = name;
                    parsed.HeadingOrder = order;
                    break;
                case 1:
                    if (parts.Length == 2)
                    {
                        parsed.VariableOrder = order;
                    }
                    else
                    {
                        parsed.Group = name;
                        parsed.GroupOrder = order;
                    }
                    break;
                case 2:
                    parsed.VariableOrder = order;
                    break;
            }
        }

        return parsed;
    }

    private static string GetChannelFromChannelBlock(ChannelBlock channelBlock, IReadOnlyList<ParamBlock> paramBlocks)
    {
        var mode = channelBlock.ColorMode == 0
            ? "Linear"
            : "Srgb";

        var cutoff = Array.IndexOf(channelBlock.InputTextureIndices, -1);
        var inputs = string.Join(", ", channelBlock.InputTextureIndices[..cutoff].Select(idx => paramBlocks[idx].Name));

        return $"Channel({channelBlock.Channel}, {channelBlock.Name}({inputs}), {mode});";
    }
}
