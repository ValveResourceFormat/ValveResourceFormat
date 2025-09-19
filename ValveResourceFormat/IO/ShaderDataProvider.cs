using System.Diagnostics;
using System.IO;
using System.Linq;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;
using Channel = ValveResourceFormat.CompiledShader.ChannelMapping;

#nullable disable

namespace ValveResourceFormat.IO
{
    public interface IShaderDataProvider
    {
        public IEnumerable<(Channel Channel, string Name)> GetInputsForTexture(string textureType, Material material);
        public string GetSuffixForInputTexture(string inputName, Material material);
    }

    public class ShaderDataProvider : IShaderDataProvider
    {
#pragma warning disable CA1859 // Use concrete types when possible for improved performance
        private readonly IFileLoader fileLoader;
        private readonly IShaderDataProvider basicProvider;
#pragma warning restore CA1859

        public ShaderDataProvider(IFileLoader fileLoader, bool useFallback = true)
        {
            this.fileLoader = fileLoader;
            basicProvider = useFallback ? new BasicShaderDataProvider() : null;
        }

        public IEnumerable<(Channel Channel, string Name)> GetInputsForTexture(string textureType, Material material)
        {
            return GetInputsForTexture_Internal(textureType, material) ?? basicProvider?.GetInputsForTexture(textureType, material);
        }

        public string GetSuffixForInputTexture(string inputName, Material material)
        {
            return GetSuffixForInputTexture_Internal(inputName, material) ?? basicProvider?.GetSuffixForInputTexture(inputName, material);
        }

        public static IDictionary<string, byte> GetMaterialFeatureState(Material material)
            => material.IntParams
                .Where(p => p.Key.StartsWith("F_", StringComparison.Ordinal))
                .ToDictionary(p => p.Key, p => (byte)p.Value);

        /// <summary>
        /// Produce a static configuration that fits the material's feature state.
        /// This configuration can be used to select one of the static variants contained in the shader file.
        /// </summary>
        /// <param name="features">Features vcs file that contains the feature definitions.</param>
        /// <param name="program">Stage for which the configuration will be generated.</param>
        /// <param name="featureParams">Feature parameters have the 'F_' prefix.</param>
        /// <param name="staticParams">Statics (not tied to a feature) that you want to override. Static parameters have the 'S_' prefix.</param>
        /// <returns>Static configuration and a generator that can be used to retreive the zframe id.</returns>
        public static (int[] StaticConfig, long StaticComboId) GetStaticConfiguration_ForFeatureState(
            VfxProgramData features,
            VfxProgramData program,
            IDictionary<string, byte> featureParams,
            IDictionary<string, byte> staticParams = null)
        {
            ArgumentNullException.ThrowIfNull(features, nameof(features));
            ArgumentNullException.ThrowIfNull(program, nameof(program));

            if (features.VcsProgramType != VcsProgramType.Features)
            {
                throw new ArgumentOutOfRangeException(nameof(features), $"Argument needs to be a shader file of type: {VcsProgramType.Features}");
            }

            if (program.VcsProgramType == VcsProgramType.Features)
            {
                throw new ArgumentOutOfRangeException(nameof(program), $"Static config cannot be built for shader files of type: {VcsProgramType.Features}");
            }

            var staticConfiguration = new int[program.StaticComboArray.Length];
            var configGen = new ConfigMappingParams(program);

            foreach (var condition in program.StaticComboArray)
            {
                if (condition.FeatureIndex == -1)
                {
                    if (staticParams != null && staticParams.TryGetValue(condition.Name, out var value))
                    {
                        staticConfiguration[condition.BlockIndex] = value;
                    }

                    continue;
                }

                var feature = features.StaticComboArray[condition.FeatureIndex];

                foreach (var (name, value) in featureParams)
                {
                    if (feature.Name == name)
                    {
                        // Check that data coming from the material is within the allowed range
                        if (value > feature.RangeMax)
                        {
                            Console.WriteLine($"Value for feature '{name}' is higher ({value}) than the maximum ({feature.RangeMax})."); // TODO: logger

                            staticConfiguration[condition.BlockIndex] = feature.RangeMax;
                        }
                        else if (value < feature.RangeMin)
                        {
                            Console.WriteLine($"Value for feature '{name}' is lower ({value}) than the minimum ({feature.RangeMin})."); // TODO: logger

                            staticConfiguration[condition.BlockIndex] = feature.RangeMin;
                        }
                        else
                        {
                            staticConfiguration[condition.BlockIndex] = value;
                        }

                        break;
                    }
                }
            }

            return (staticConfiguration, configGen.CalcStaticComboIdFromValues(staticConfiguration));
        }

        /// <summary>
        /// Removes switches that would point to a inexistent static combo.
        /// </summary>
        /// <param name="program">The file that contains the combos, and combo rules</param>
        /// <returns></returns>
        public static bool TryReduceStaticConfiguration(VfxProgramData program, int[] staticConfiguration, out int[] reducedConfiguration)
        {
            reducedConfiguration = [.. staticConfiguration];

            foreach (var constraint in program.StaticComboRules)
            {
                // Allow only one of the statics
                if (constraint.Rule == VfxRuleMethod.AllowNum)
                {
                    // Allow0 (disable this toggle)
                    var allow0 = constraint.ExtraRuleData[0] == 0;
                    if (allow0 && staticConfiguration[constraint.Indices[0]] != 0)
                    {
                        reducedConfiguration[constraint.Indices[0]] = 0;
                    }

                    // Allow1 (cannot both be active)
                    var allow1 = constraint.ExtraRuleData[0] == 1;
                    if (allow1 && staticConfiguration[constraint.Indices[0]] != 0 && staticConfiguration[constraint.Indices[1]] != 0)
                    {
                        reducedConfiguration[constraint.Indices[1]] = 0;
                    }
                }
                else if (constraint.Rule == VfxRuleMethod.Requires)
                {
                    var requires1 = constraint.ExtraRuleData[0] == 1;
                    if (requires1 && staticConfiguration[constraint.Indices[0]] == 0 && staticConfiguration[constraint.Indices[1]] == 1)
                    {
                        reducedConfiguration[constraint.Indices[1]] = 0;
                    }
                }
            }

            return !reducedConfiguration.Equals(staticConfiguration);
        }

        /// <summary>
        /// Get precise texture inputs by querying the shader files.
        /// </summary>
        private List<(Channel Channel, string Name)> GetInputsForTexture_Internal(string textureType, Material material)
        {
            var shader = fileLoader.LoadShader(material.ShaderName);
            if (shader?.Features == null)
            {
                return null;
            }

            var @params = Array.FindAll(shader.Features.VariableDescriptions, p => p.Name == textureType);

            if (@params.Length == 0)
            {
                // Texture not defined in the shader file, export the texture anyway.
                // Most likely, the texture was removed by a shader update, and the material was not recompiled.
                return [(Channel.RGBA, BasicShaderDataProvider.ConvertTextureToInput(textureType))];
            }

            // vr_simple (hlvr): g_tAmbientOcclusion[1] ChannelIndices (-1,-1,-1,-1)
            @params = [.. @params.Where(static p => p.ChannelCount > 0)];

            if (@params.Length == 0)
            {
                throw new InvalidDataException($"None of the variants of '{textureType}' had any channel defined in shader '{shader.Features.ShaderName}'.");
            }

            var determinedParameter = @params[0];
            var originatingShaderFile = shader.Features;

            if (@params.Length > 1)
            {
                (determinedParameter, originatingShaderFile) = DetermineParameterReferencedByMaterial(shader, material, textureType);
            }

            return [.. GetParameterInputs(determinedParameter, originatingShaderFile)];

            /// <summary>
            /// Determine which of the parameter variants is the one referenced by the material.
            /// </summary>
            (VfxVariableDescription, VfxProgramData) DetermineParameterReferencedByMaterial(ShaderCollection shader, Material material, string paramName,
                KeyValuePair<string, byte> forcedStatic = default)
            {
                var featureState = GetMaterialFeatureState(material);

                // Pixel shader first
                var collectionOrdered = shader
                    .Where(sh => sh.VcsProgramType != VcsProgramType.Features && sh.StaticComboEntries.Count > 0)
                    .OrderByDescending(sh => sh.VcsProgramType == VcsProgramType.PixelShader);

                foreach (var shaderFile in collectionOrdered)
                {
                    var fileParams = Array.FindAll(shaderFile.VariableDescriptions, p => p.Name == paramName);
                    if (fileParams.Length == 0)
                    {
                        continue;
                    }

                    // Dota seems to want one of S_MODE_FORWARD / S_MODE_DEFERRED enabled for textures
                    // to be referenced in the writeseq blocks.
                    var staticState = new Dictionary<string, byte>(2) { { "S_MODE_FORWARD", 1 } };

                    if (forcedStatic.Key is not null)
                    {
                        staticState.Add(forcedStatic.Key, forcedStatic.Value);
                    }

                    var staticConfig = GetStaticConfiguration_ForFeatureState(shader.Features, shaderFile, featureState, staticState).StaticConfig;

                    var configGen = new ConfigMappingParams(shaderFile);
                    var staticComboId = configGen.CalcStaticComboIdFromValues(staticConfig);

                    // It can happen that the shader feature rules don't match static rules, producing
                    // materials with bad feature configuration. That or the material data is just bad/incompatible.
                    if (!shaderFile.StaticComboEntries.ContainsKey(staticComboId))
                    {
                        var reduced = TryReduceStaticConfiguration(shaderFile, staticConfig, out var reducedConfig);
                        if (!reduced)
                        {
                            throw new NotImplementedException("Feature state points to a missing static combo, likely because constraint solver is not implemented.");
                        }

                        staticComboId = configGen.CalcStaticComboIdFromValues(reducedConfig);
                        if (!shaderFile.StaticComboEntries.ContainsKey(staticComboId))
                        {
                            throw new InvalidOperationException("Constraint solver failed to produce a valid static combo.");
                        }

                        staticConfig = reducedConfig;
                    }

                    shaderFile.StaticComboCache.EnsureCapacity(staticConfig.Length);

                    var staticVariant = shaderFile.StaticComboCache.Get(staticComboId);

                    // Should non-leading write sequences be checked too?
                    foreach (var writeSequenceField in staticVariant.VariablesFromStaticCombo.Fields)
                    {
                        var referencedParam = fileParams.FirstOrDefault(p => p.BlockIndex == writeSequenceField.VariableIndex);
                        if (referencedParam != null)
                        {
                            return (referencedParam, shaderFile);
                        }
                    }

                    if (forcedStatic.Key is null)
                    {
                        // Try again with S_MODE_TOOLS_VIS
                        // Fixes hlvr/pak01/materials/skybox/sky_stars_01.vmat
                        // Jumps from zframe 0x230a to 0x280230a, ends up matching the 2nd g_tNormal, with Box mips.
                        return DetermineParameterReferencedByMaterial(shader, material, paramName, forcedStatic: new KeyValuePair<string, byte>("S_MODE_TOOLS_VIS", 1));
                    }
                }

                // TODO: Ignore possibly unused texture for export? (Issue #652)
                throw new InvalidDataException(
                    $"Varying parameter '{paramName}' in '{shader.Features.ShaderName}' could not be resolved. "
                    + $"Features ({string.Join(", ", featureState.Select(p => $"{p.Key}={p.Value}"))})");
            }

            IEnumerable<(Channel Channel, string Name)> GetParameterInputs(VfxVariableDescription param, VfxProgramData program)
            {
                for (var i = 0; i < param.ChannelCount; i++)
                {
                    var channelIndex = param.ChannelIndices[i];
                    var channel = program.TextureChannelProcessors[channelIndex];

                    var cutoff = Array.IndexOf(channel.InputTextureIndices, -1);
                    var textureProcessorInputs = channel.InputTextureIndices[..cutoff].Select(idx => program.VariableDescriptions[idx].Name).ToArray();

                    if (channel.TexProcessorName == "HemiOctIsoRoughness_RG_B" || channel.TexProcessorName == "AnisoNormal")
                    {
                        yield return (Channel.RGB, textureProcessorInputs[0]);
                        if (textureProcessorInputs.Length == 2)
                        {
                            yield return (Channel.A, textureProcessorInputs[1]);
                        }

                        yield break;
                    }

                    // Compiler generated texture
                    // https://github.com/ValveResourceFormat/ValveResourceFormat/issues/630
                    if (channel.TexProcessorName == "AnisoRoughness_RG" && textureProcessorInputs.Length > 1)
                    {
                        Debug.Assert(textureProcessorInputs[0] == "TextureNormal" && textureProcessorInputs[1] == "TextureRoughness");
                        yield break;
                    }

                    // Seems like we already undo the swizzle in texture decoder. Issue: #591
                    if (channel.Channel == Channel.AG)
                    {
                        yield return (Channel.RG, textureProcessorInputs[0]);
                        yield break;
                    }

                    yield return (channel.Channel, textureProcessorInputs[0]);
                }
            }
        }

        private string GetSuffixForInputTexture_Internal(string inputName, Material material)
        {
            var shader = fileLoader.LoadShader(material.ShaderName);
            if (shader?.Features != null)
            {
                foreach (var param in shader.Features.VariableDescriptions)
                {
                    if (param.Name == inputName && !string.IsNullOrEmpty(param.ImageSuffix))
                    {
                        return "_" + param.ImageSuffix;
                    }
                }
            }

            return null;
        }
    }

    public class BasicShaderDataProvider : IShaderDataProvider
    {
        public static readonly Dictionary<string, Dictionary<string, (Channel Channel, string Name)[]>> TextureMappings = new()
        {
            ["global_lit_simple"] = new()
            {
                ["g_tColor"] = [(Channel.RGB, "TextureColor"), (Channel.A, "TextureTranslucency")],
                ["g_tNormal"] = [(Channel.RGB, "TextureNormal")],
                ["g_tSpecular"] = [(Channel.R, "TextureReflectance"), (Channel.G, "TextureSelfIllum"), (Channel.B, "TextureBloom")],
            },

            ["multiblend"] = new()
            {
                ["g_tColor0"] = [(Channel.RGB, "TextureColor0")],
                ["g_tColor1"] = [(Channel.RGB, "TextureColor1"), (Channel.A, "TextureRevealMask1")],
                ["g_tColor2"] = [(Channel.RGB, "TextureColor2"), (Channel.A, "TextureRevealMask2")],
                ["g_tColor3"] = [(Channel.RGB, "TextureColor3"), (Channel.A, "TextureRevealMask3")],
                ["g_tSpecular0"] = [(Channel.R, "TextureReflectance0"), (Channel.G, "TextureSelfIllum0"), (Channel.B, "TextureBloom0")],
                ["g_tSpecular1"] = [(Channel.R, "TextureReflectance1"), (Channel.G, "TextureSelfIllum1"), (Channel.B, "TextureBloom1")],
                ["g_tSpecular2"] = [(Channel.R, "TextureReflectance2"), (Channel.G, "TextureSelfIllum2"), (Channel.B, "TextureBloom2")],
                ["g_tSpecular3"] = [(Channel.R, "TextureReflectance3"), (Channel.G, "TextureSelfIllum3"), (Channel.B, "TextureBloom3")],
                ["g_tTintMasks"] = [(Channel.R, "TextureTintMask0"), (Channel.G, "TextureTintMask1"), (Channel.B, "TextureTintMask2"), (Channel.A, "TextureTintMask3")],
                ["g_tTint2Masks"] = [(Channel.R, "TextureTint2Mask0"), (Channel.G, "TextureTint2Mask1"), (Channel.B, "TextureTint2Mask2"), (Channel.A, "TextureTint2Mask3")],
            },

            ["hero"] = new()
            {
                ["g_tColor"] = [(Channel.RGB, "TextureColor"), (Channel.A, "TextureTranslucency")],
                ["g_tNormal"] = [(Channel.RGB, "TextureNormal")],
                ["g_tCubeMap"] = [(Channel.RGBA, "TextureCubeMap")],
                ["g_tCubeMapSeparateMask"] = [(Channel.G, "TextureCubeMapSeparateMask")],
                ["g_tFresnelWarp"] = [(Channel.R, "TextureFresnelWarpRim"), (Channel.G, "TextureFresnelWarpColor"), (Channel.B, "TextureFresnelWarpSpec")],
                ["g_tMasks1"] = [(Channel.R, "TextureDetailMask"), (Channel.G, "TextureDiffuseWarpMask"), (Channel.B, "TextureMetalnessMask"), (Channel.A, "TextureSelfIllumMask")],
                ["g_tMasks2"] = [(Channel.R, "TextureSpecularMask"), (Channel.G, "TextureRimMask"), (Channel.B, "TextureTintByBaseMask"), (Channel.A, "TextureSpecularExponent")],
                ["g_tDetail"] = [(Channel.RGBA, "TextureDetail")],
                ["g_tDetail2"] = [(Channel.RGBA, "TextureDetail2")],
            },

            ["grasstile_preview"] = new()
            {
                ["g_tColor"] = [(Channel.RGB, "TextureColor"), (Channel.A, "TextureTranslucency")],
                ["g_tTintMask"] = [(Channel.G, "TextureTintMask")],
                ["g_tSpecular"] = [(Channel.G, "TextureReflectance")],
                ["g_tSelfIllum"] = [(Channel.G, "TextureSelfIllum")],
            },

            ["generic"] = new()
            {
                ["g_tColor"] = [(Channel.RGB, "TextureColor")],
                ["g_tNormal"] = [(Channel.RGB, "TextureNormal")],
                ["g_tMetalnessReflectanceFresnel"] = [(Channel.R, "TextureMetalness"), (Channel.G, "TextureReflectance"), (Channel.B, "TextureFresnel")],
                ["g_tRoughness"] = [(Channel.R, "TextureRoughness"),],
            },

            ["vr_standard"] = new()
            {
                ["g_tColor"] = [(Channel.RGB, "TextureColor"), (Channel.A, "TextureTranslucency")],
                ["g_tColor1"] = [(Channel.RGB, "TextureColor")],
                ["g_tColor2"] = [(Channel.RGB, "TextureColor")],
                ["g_tNormal"] = [(Channel.RGB, "TextureNormal")],
                ["g_tNormal1"] = [(Channel.RGB, "TextureNormal")],
                ["g_tNormal2"] = [(Channel.RGB, "TextureNormal")],
            },

            ["vr_complex"] = new()
            {
                ["g_tColor"] = [(Channel.RGB, "TextureColor"), (Channel.A, string.Empty)], // Alpha can be metal or translucency
                ["g_tNormal"] = [(Channel.RGB, "TextureNormal"), (Channel.A, "TextureRoughness")], // TODO: Figure out anisotropic gloss

                // These all work fine thanks to consistent names, but we can clean them up to save disk size.
                // E.g. RGBA -> R (Grayscale)
                ["g_tAmbientOcclusion"] = [(Channel.R, "TextureAmbientOcclusion")],
                ["g_tTintMask"] = [(Channel.R, "TextureTintMask")],

                ["g_tMetalness"] = [(Channel.R, "TextureMetalness")],
                ["g_tSelfIllumMask"] = [(Channel.R, "TextureSelfIllumMask")],
                ["g_tBentNormal"] = [(Channel.RGB, "TextureBentNormal")], // ATI2N

                ["g_tDetail"] = [(Channel.RGB, "TextureDetail")],
                ["g_tDetailMask"] = [(Channel.R, "TextureDetailMask")],
                ["g_tNormalDetail"] = [(Channel.RGB, "TextureNormalDetail")], // ATI2N

                ["g_tSquishColor"] = [(Channel.RGB, "TextureSquishColor")],
                ["g_tStretchColor"] = [(Channel.RGB, "TextureStretchColor")],
                ["g_tSquishNormal"] = [(Channel.RGB, "TextureSquishNormal")],
                ["g_tStretchNormal"] = [(Channel.RGB, "TextureStretchNormal")],
                ["g_tSquishAmbientOcclusion"] = [(Channel.R, "TextureSquishAmbientOcclusion")],
                ["g_tStretchAmbientOcclusion"] = [(Channel.R, "TextureStretchAmbientOcclusion")],

            },

            ["vr_simple"] = new()
            {
                ["g_tColor"] = [(Channel.RGB, "TextureColor"), (Channel.A, string.Empty)], // Alpha can be ao, metal or nothing at all
                ["g_tNormal"] = [(Channel.RGB, "TextureNormal"), (Channel.A, "TextureRoughness")],

                ["g_tAmbientOcclusion"] = [(Channel.R, "TextureAmbientOcclusion")],
                ["g_tTintMask"] = [(Channel.R, "TextureTintMask")],
            },

            ["vr_simple_2way_blend"] = new()
            {
                ["g_tColorA"] = [(Channel.RGB, "TextureColorA"), (Channel.A, "TextureMetalnessA")],
                ["g_tNormalA"] = [(Channel.RGB, "TextureNormalA"), (Channel.A, "TextureRoughnessA")],
                ["g_tColorB"] = [(Channel.RGB, "TextureColorB"), (Channel.A, "TextureMetalnessB")],
                ["g_tNormalB"] = [(Channel.RGB, "TextureNormalB"), (Channel.A, "TextureRoughnessB")],

                ["g_tMask"] = [(Channel.R, "TextureMask")],
            },

            ["vr_eyeball"] = new()
            {
                ["g_tColor"] = [(Channel.RGB, "TextureColor"), (Channel.A, "TextureReflectance")],
                ["g_tIris"] = [(Channel.RGB, "IrisNormal"), (Channel.A, "IrisRoughness")],
                ["g_tNormal"] = [(Channel.AG, "TextureNormal")],

                ["g_tIrisMask"] = [(Channel.R, "TextureIrisMask")],
                ["g_tSelfIllumMask"] = [(Channel.R, "TextureSelfIllumMask")],
            },

            ["csgo_weapon"] = new()
            {
                ["g_tColor"] = [(Channel.RGB, "TextureColor")],
                ["g_tMetalness"] = [(Channel.R, "TextureRoughness"), (Channel.G, "TextureMetalness")],
                ["g_tAmbientOcclusion"] = [(Channel.R, "TextureAmbientOcclusion")],
            },

            ["sky"] = new()
            {
                ["g_tSkyTexture"] = [(Channel.RGBA, "SkyTexture")],
            }
        };

        public static readonly Dictionary<string, string> CommonTextureSuffixes = new()
        {
            { "TextureDetailMask", "_detailmask" },
            { "TextureDiffuseWarpMask", "_diffusemask" },
            { "TextureMetalnessMask", "_metalnessmask" },
            { "TextureSelfIllumMask", "_selfillummask" },

            { "TextureSpecularMask", "_specmask" },
            { "TextureRimMask", "_rimmask" },
            { "TextureTintByBaseMask", "_basetintmask" },
            { "TextureSpecularExponent", "_specexp" },
            { "TextureRevealMask", "_blend" },

            { "TextureColor", "_color" },
            { "TextureNormal", "_normal" },
            { "TextureRoughness", "_rough" },
            { "TextureMetalness", "_metal" },
            { "TextureAmbientOcclusion", "_ao" },
            { "TextureReflectance", "_refl"},
            { "TextureTranslucency", "_trans"},
        };

        /// <summary>
        /// Get hardcoded texture inputs. If no mappings are found it will be a single *guessed* RGBA input.
        /// </summary>
        public IEnumerable<(Channel Channel, string Name)> GetInputsForTexture(string textureType, Material material)
        {
            var shaderName = Path.ChangeExtension(material.ShaderName, null); // strip '.vfx'

            if (!(TextureMappings.TryGetValue(shaderName, out var shaderSpecific) && shaderSpecific.TryGetValue(textureType, out var channelMappings)))
            {
                yield return (Channel.RGBA, ConvertTextureToInput(textureType));
                yield break;
            }

            foreach (var mapping in channelMappings)
            {
                var (channel, newTextureType) = mapping;
                if (newTextureType.Length == 0 && !TryFigureOutNonStaticMap(shaderName, textureType, material.IntParams, out newTextureType))
                {
                    continue;
                }

                yield return (channel, newTextureType);
            }
        }

        public static string ConvertTextureToInput(string textureType)
        {
            return textureType.Replace("g_t", "Texture", StringComparison.Ordinal);
        }

        public string GetSuffixForInputTexture(string inputName, Material material)
        {
            foreach (var (commonType, commonSuffix) in CommonTextureSuffixes)
            {
                // Allow matching TextureColorB with TextureColor
                if (inputName.StartsWith(commonType, StringComparison.OrdinalIgnoreCase))
                {
                    return commonSuffix;
                }
            }

            return null;
        }

        private static bool TryFigureOutNonStaticMap(string shader, string textureType, Dictionary<string, long> intParams, out string newTextureType)
        {
            if (shader == "vr_simple" && textureType == "g_tColor")
            {
                if (intParams.GetValueOrDefault("F_METALNESS_TEXTURE") != 0)
                {
                    newTextureType = "TextureMetalness";
                    return true;
                }

                if (intParams.GetValueOrDefault("F_AMBIENT_OCCLUSION_TEXTURE") != 0)
                {
                    newTextureType = "TextureAmbientOcclusion";
                    return true;
                }
            }

            else if (shader == "vr_complex" && textureType == "g_tColor")
            {
                newTextureType = "TextureMetalness";

                if (intParams.GetValueOrDefault("F_TRANSLUCENT") != 0
                || intParams.GetValueOrDefault("F_ALPHA_TEST") != 0)
                {
                    newTextureType = "TextureTranslucency";
                }

                return true;
            }

            newTextureType = null;
            return false;
        }
    }
}
