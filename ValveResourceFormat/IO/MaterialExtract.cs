using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

using ChannelMapping = System.ValueTuple<ValveResourceFormat.IO.MaterialExtract.Channel, string>;

namespace ValveResourceFormat.IO;
public sealed class MaterialExtract
{
    public readonly struct UnpackInfo
    {
        public string TextureType { get; init; }
        public string FileName { get; init; }
        public Channel Channel { get; init; }
    }

    public enum Channel
    {
        R,
        G,
        B,
        A,
        _Single,
        GA,
        _Double,
        RGB,
        RGBA
    }

    public static readonly Dictionary<string, Dictionary<string, ChannelMapping[]>> TextureMappings = new()
    {
        ["global_lit_simple"] = new()
        {
            ["g_tColor"] = new[] { (Channel.RGB, "TextureColor"), (Channel.A, "TextureTranslucency") },
            ["g_tNormal"] = new[] { (Channel.RGB, "TextureNormal") },
            ["g_tSpecular"] = new[] { (Channel.R, "TextureReflectance"), (Channel.G, "TextureSelfIllum"), (Channel.B, "TextureBloom") },
        },

        ["multiblend"] = new()
        {
            ["g_tColor0"] = new[] { (Channel.RGB, "TextureColor0") },
            ["g_tColor1"] = new[] { (Channel.RGB, "TextureColor1"), (Channel.A, "TextureRevealMask1") },
            ["g_tColor2"] = new[] { (Channel.RGB, "TextureColor2"), (Channel.A, "TextureRevealMask2") },
            ["g_tColor3"] = new[] { (Channel.RGB, "TextureColor3"), (Channel.A, "TextureRevealMask3") },
            ["g_tSpecular0"] = new[] { (Channel.R, "TextureReflectance0"), (Channel.G, "TextureSelfIllum0"), (Channel.B, "TextureBloom0") },
            ["g_tSpecular1"] = new[] { (Channel.R, "TextureReflectance1"), (Channel.G, "TextureSelfIllum1"), (Channel.B, "TextureBloom1") },
            ["g_tSpecular2"] = new[] { (Channel.R, "TextureReflectance2"), (Channel.G, "TextureSelfIllum2"), (Channel.B, "TextureBloom2") },
            ["g_tSpecular3"] = new[] { (Channel.R, "TextureReflectance3"), (Channel.G, "TextureSelfIllum3"), (Channel.B, "TextureBloom3") },
            ["g_tTintMasks"] = new[] { (Channel.R, "TextureTintMask0"), (Channel.G, "TextureTintMask1"), (Channel.B, "TextureTintMask2"), (Channel.A, "TextureTintMask3") },
            ["g_tTint2Masks"] = new[] { (Channel.R, "TextureTint2Mask0"), (Channel.G, "TextureTint2Mask1"), (Channel.B, "TextureTint2Mask2"), (Channel.A, "TextureTint2Mask3") },
        },

        ["hero"] = new()
        {
            ["g_tColor"] = new[] { (Channel.RGB, "TextureColor"), (Channel.A, "TextureTranslucency") },
            ["g_tNormal"] = new[] { (Channel.RGB, "TextureNormal") },
            ["g_tCubeMap"] = new[] { (Channel.RGBA, "TextureCubeMap") },
            ["g_tCubeMapSeparateMask"] = new[] { (Channel.G, "TextureCubeMapSeparateMask") },
            ["g_tFresnelWarp"] = new[] { (Channel.R, "TextureFresnelWarpRim"), (Channel.G, "TextureFresnelWarpColor"), (Channel.B, "TextureFresnelWarpSpec") },
            ["g_tMasks1"] = new[] { (Channel.R, "TextureDetailMask"), (Channel.G, "TextureDiffuseWarpMask"), (Channel.B, "TextureMetalnessMask"), (Channel.A, "TextureSelfIllumMask") },
            ["g_tMasks2"] = new[] { (Channel.R, "TextureSpecularMask"), (Channel.G, "TextureRimMask"), (Channel.B, "TextureTintByBaseMask"), (Channel.A, "TextureSpecularExponent") },
            ["g_tDetail"] = new[] { (Channel.RGBA, "TextureDetail") },
            ["g_tDetail2"] = new[] { (Channel.RGBA, "TextureDetail2") },
        },

        ["grasstile_preview"] = new()
        {
            ["g_tColor"] = new[] { (Channel.RGB, "TextureColor"), (Channel.A, "TextureTranslucency") },
            ["g_tTintMask"] = new[] { (Channel.G, "TextureTintMask") },
            ["g_tSpecular"] = new[] { (Channel.G, "TextureReflectance") },
            ["g_tSelfIllum"] = new[] { (Channel.G, "TextureSelfIllum") },
        },

        ["generic"] = new()
        {
            ["g_tColor"] = new[] { (Channel.RGB, "TextureColor") },
            ["g_tNormal"] = new[] { (Channel.RGB, "TextureNormal") },
            ["g_tMetalnessReflectanceFresnel"] = new[] { (Channel.R, "TextureMetalness"), (Channel.G, "TextureReflectance"), (Channel.B, "TextureFresnel") },
            ["g_tRoughness"] = new[] { (Channel.R, "TextureRoughness"), },
        },

        ["vr_standard"] = new()
        {
            ["g_tColor"] = new[] { (Channel.RGB, "TextureColor"), (Channel.A, "TextureTranslucency") },
            ["g_tColor1"] = new[] { (Channel.RGB, "TextureColor") },
            ["g_tColor2"] = new[] { (Channel.RGB, "TextureColor") },
            ["g_tNormal"] = new[] { (Channel.RGB, "TextureNormal") },
            ["g_tNormal1"] = new[] { (Channel.RGB, "TextureNormal") },
            ["g_tNormal2"] = new[] { (Channel.RGB, "TextureNormal") },
        },

        ["vr_complex"] = new()
        {
            ["g_tColor"] = new[] { (Channel.RGB, "TextureColor"), (Channel.A, string.Empty) }, // Alpha can be metal or translucency
            ["g_tNormal"] = new[] { (Channel.RGB, "TextureNormal"), (Channel.A, "TextureRoughness") }, // TODO: Figure out anisotropic gloss

            // These all work fine thanks to consistent names, but we can clean them up to save disk size.
            // E.g. RGBA -> R (Grayscale)
            ["g_tAmbientOcclusion"] = new[] { (Channel.R, "TextureAmbientOcclusion") },
            ["g_tTintMask"] = new[] { (Channel.R, "TextureTintMask") },

            ["g_tMetalness"] = new[] { (Channel.R, "TextureMetalness") },
            ["g_tSelfIllumMask"] = new[] { (Channel.R, "TextureSelfIllumMask") },
            ["g_tBentNormal"] = new[] { (Channel.RGB, "TextureBentNormal") }, // ATI2N

            ["g_tDetail"] = new[] { (Channel.RGB, "TextureDetail") },
            ["g_tDetailMask"] = new[] { (Channel.R, "TextureDetailMask") },
            ["g_tNormalDetail"] = new[] { (Channel.RGB, "TextureNormalDetail") }, // ATI2N

            ["g_tSquishColor"] = new[] { (Channel.RGB, "TextureSquishColor") },
            ["g_tStretchColor"] = new[] { (Channel.RGB, "TextureStretchColor") },
            ["g_tSquishNormal"] = new[] { (Channel.RGB, "TextureSquishNormal") },
            ["g_tStretchNormal"] = new[] { (Channel.RGB, "TextureStretchNormal") },
            ["g_tSquishAmbientOcclusion"] = new[] { (Channel.R, "TextureSquishAmbientOcclusion") },
            ["g_tStretchAmbientOcclusion"] = new[] { (Channel.R, "TextureStretchAmbientOcclusion") },

        },

        ["vr_simple"] = new()
        {
            ["g_tColor"] = new[] { (Channel.RGB, "TextureColor"), (Channel.A, string.Empty) }, // Alpha can be ao, metal or nothing at all
            ["g_tNormal"] = new[] { (Channel.RGB, "TextureNormal"), (Channel.A, "TextureRoughness") },

            ["g_tAmbientOcclusion"] = new[] { (Channel.R, "TextureAmbientOcclusion") },
            ["g_tTintMask"] = new[] { (Channel.R, "TextureTintMask") },
        },

        ["vr_simple_2way_blend"] = new()
        {
            ["g_tColorA"] = new[] { (Channel.RGB, "TextureColorA"), (Channel.A, "TextureMetalnessA") },
            ["g_tNormalA"] = new[] { (Channel.RGB, "TextureNormalA"), (Channel.A, "TextureRoughnessA") },
            ["g_tColorB"] = new[] { (Channel.RGB, "TextureColorB"), (Channel.A, "TextureMetalnessB") },
            ["g_tNormalB"] = new[] { (Channel.RGB, "TextureNormalB"), (Channel.A, "TextureRoughnessB") },

            ["g_tMask"] = new[] { (Channel.R, "TextureMask") },
        },

        ["vr_eyeball"] = new()
        {
            ["g_tColor"] = new[] { (Channel.RGB, "TextureColor"), (Channel.A, "TextureReflectance") },
            ["g_tIris"] = new[] { (Channel.RGB, "IrisNormal"), (Channel.A, "IrisRoughness") },
            ["g_tNormal"] = new[] { (Channel.GA, "TextureNormal") },

            ["g_tIrisMask"] = new[] { (Channel.R, "TextureIrisMask") },
            ["g_tSelfIllumMask"] = new[] { (Channel.R, "TextureSelfIllumMask") },
        },

        ["sky"] = new()
        {
            ["g_tSkyTexture"] = new[] { (Channel.RGBA, "SkyTexture") },
        }
    };

    public static readonly Dictionary<string, ChannelMapping[]> GltfTextureMappings = new()
    {
        ["BaseColor"] = new[] { (Channel.RGB, "TextureColor"), (Channel.A, "TextureTranslucency") },
        ["Normal"] = new[] { (Channel.RGB, "TextureNormal") },
        ["MetallicRoughness"] = new[] { (Channel.R, string.Empty), (Channel.G, "TextureRoughness"), (Channel.B, "TextureMetalness") },
        ["Occlusion"] = new[] { (Channel.R, "TextureAmbientOcclusion") },
        ["Emissive"] = new[] { (Channel.R, "TextureSelfIllumMask") },
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
    };

    private static bool IsDefaultTexture(string textureFileName)
        => textureFileName.StartsWith("materials/default/", StringComparison.OrdinalIgnoreCase);

    private readonly Material material;
    private readonly ResourceEditInfo editInfo;
    private readonly IFileLoader fileLoader;

    public MaterialExtract(Material material, ResourceEditInfo editInfo, IFileLoader fileLoader)
    {
        this.material = material;
        this.editInfo = editInfo;
        this.fileLoader = fileLoader;
    }

    public MaterialExtract(Resource resource, IFileLoader fileLoader = null)
        : this((Material)resource.DataBlock, resource.EditInfo, fileLoader)
    {
    }

    public ContentFile ToContentFile()
    {
        var vmat = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(ToValveMaterial())
        };

        foreach (var (type, filePath) in material.TextureParams)
        {
            if (vmat.ExternalRefsHandled.ContainsKey(filePath + "_c"))
            {
                continue;
            }

            var texture = fileLoader?.LoadFile(filePath + "_c");
            if (texture is null)
            {
                continue;
            }

            var images = GetTextureUnpackInfos(type, filePath, material, omitDefaults: true, omitUniforms: true);
            var vtex = new TextureExtract(texture).ToMaterialMaps(images);

            vmat.ExternalRefsHandled.Add(filePath + "_c", vtex);
            vmat.SubFilesAreExternal = true;
            vmat.SubFiles.AddRange(vtex.SubFiles);
        }

        return vmat;
    }

    public static string OutTextureName(string texturePath, bool keepOriginalExtension, string desiredSuffix = null)
    {
        if (!texturePath.EndsWith(".vtex", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{texturePath} must have .vtex", nameof(texturePath));
        }

        texturePath = Path.ChangeExtension(texturePath, null); // strip '.vtex'
        if (texturePath.EndsWith(".generated", StringComparison.OrdinalIgnoreCase))
        {
            texturePath = Path.ChangeExtension(texturePath, null);
        }

        var textureParts = Path.GetFileName(texturePath).Split('_');
        if (textureParts.Length > 2)
        {
            if (textureParts[^1].All("0123456789abcdef".Contains)             // This is a hash
            && textureParts[^2].Length >= 3 && textureParts[^2].Length <= 4 // This is the original extension
            && !string.IsNullOrEmpty(string.Join("_", textureParts[..^2])))   // Name of the Input[0] image
            {
                texturePath = Path.Combine(Path.GetDirectoryName(texturePath), string.Join("_", textureParts[..^2]));
                texturePath = texturePath.Replace(Path.DirectorySeparatorChar, '/');
            }
            // Also seen "{MAT}_vmat_{G_TPARAM}_{HASH}.vtex"
        }

        if (desiredSuffix is not null)
        {
            // Strip e.g. _color if we want _metal
            if (!texturePath.EndsWith(desiredSuffix, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var suffix in CommonTextureSuffixes.Values)
                {
                    if (texturePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        texturePath = texturePath[..^suffix.Length];
                        break;
                    }
                }
            }
            // Keep the hash to avoid conflicts
            texturePath += "_" + textureParts[^1] + desiredSuffix;
        }

        return Path.ChangeExtension(texturePath, keepOriginalExtension ? textureParts[^2] : "png");
    }

    public static IEnumerable<ChannelMapping> GetTextureInputs(string shaderName, string textureType, Dictionary<string, long> featureState)
    {
        shaderName = shaderName[..^4]; // strip '.vfx'

        if (!(TextureMappings.TryGetValue(shaderName, out var shaderSpecific) && shaderSpecific.TryGetValue(textureType, out var channelMappings)))
        {
            yield return (Channel.RGBA, textureType.Replace("g_t", "Texture", StringComparison.Ordinal));
            yield break;
        }

        foreach (var mapping in channelMappings)
        {
            var (channel, newTextureType) = mapping;
            if (newTextureType.Length == 0 && !TryFigureOutNonStaticMap(shaderName, textureType, featureState, out newTextureType))
            {
                continue;
            }

            yield return (channel, newTextureType);
        }
    }

    public static IEnumerable<UnpackInfo> GetTextureUnpackInfos(string textureType, string texturePath, Material material, bool omitDefaults, bool omitUniforms)
    {
        var isInput0 = true;
        foreach (var (channel, newTextureType) in GetTextureInputs(material.ShaderName, textureType, material.IntParams))
        {
            if (omitUniforms && material.VectorParams.ContainsKey(newTextureType))
            {
                continue;
            }

            string desiredSuffix = null;
            if (!isInput0)
            {
                desiredSuffix = "-" + channel;
                foreach (var (commonType, commonSuffix) in CommonTextureSuffixes)
                {
                    // Allow matching TextureColorB with TextureColor
                    if (newTextureType.StartsWith(commonType, StringComparison.OrdinalIgnoreCase))
                    {
                        desiredSuffix = commonSuffix;
                        break;
                    }
                }
            }

            var keepOriginalExtension = false;
            if (isInput0 && IsDefaultTexture(texturePath))
            {
                if (omitDefaults)
                {
                    isInput0 = false;
                    continue;
                }

                keepOriginalExtension = true;
            }

            yield return new UnpackInfo
            {
                TextureType = newTextureType,
                FileName = OutTextureName(texturePath, keepOriginalExtension, desiredSuffix),
                Channel = channel
            };

            isInput0 = false;
        }
    }

    public static bool TryFigureOutNonStaticMap(string shader, string textureType, Dictionary<string, long> intParams, out string newTextureType)
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

    public string ToValveMaterial()
    {
        var root = new KVObject("Layer0", new List<KVObject>())
        {
            new KVObject("shader", material.ShaderName)
        };

        foreach (var (key, value) in material.IntParams)
        {
            root.Add(new KVObject(key, value));
        }

        foreach (var (key, value) in material.FloatParams)
        {
            root.Add(new KVObject(key, value));
        }

        foreach (var (key, value) in material.VectorParams)
        {
            root.Add(new KVObject(key, $"[{value.X:N6} {value.Y:N6} {value.Z:N6} {value.W:N6}]"));
        }

        var originalTextures = new KVObject("VRF Original Textures", new List<KVObject>());
        foreach (var (key, value) in material.TextureParams)
        {
            foreach (var unpackInfo in GetTextureUnpackInfos(key, value, material, false, true))
            {
                root.Add(new KVObject(unpackInfo.TextureType, unpackInfo.FileName));
            }

            originalTextures.Add(new KVObject(key, value));
        }

        root.Add(originalTextures);

        if (material.DynamicExpressions.Count > 0)
        {
            var dynamicExpressionsNode = new KVObject("DynamicParams", new List<KVObject>());
            root.Add(dynamicExpressionsNode);
            foreach (var (key, value) in material.DynamicExpressions)
            {
                dynamicExpressionsNode.Add(new KVObject(key, value));
            }
        }

        var attributes = new List<KVObject>();

        foreach (var (key, value) in material.IntAttributes)
        {
            // not defined by user, so skip it
            if (key.Equals("representativetexturewidth", StringComparison.OrdinalIgnoreCase)
            || key.Equals("representativetextureheight", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            attributes.Add(new KVObject(key, value));
        }

        foreach (var (key, value) in material.FloatAttributes)
        {
            // Skip `int` definition if there is a `float` definition
            attributes = attributes.Where(existing_key => existing_key.Name != key).ToList();
            attributes.Add(new KVObject(key, value));
        }

        foreach (var (key, value) in material.VectorAttributes)
        {
            attributes.Add(new KVObject(key, $"[{value.X:N6} {value.Y:N6} {value.Z:N6} {value.W:N6}]"));
        }

        foreach (var (key, value) in material.StringAttributes)
        {
            attributes.Add(new KVObject(key, value ?? string.Empty));
        }

        var attributesThatAreSystemAttributes = new HashSet<string>
        {
            "physicssurfaceproperties",
            "worldmappingwidth",
            "worldmappingheight"
        };

        if (attributes.Any())
        {
            // Some attributes are actually SystemAttributes
            var systemAttributes = attributes.Where(attribute => attributesThatAreSystemAttributes.Contains(attribute.Name.ToLowerInvariant())).ToList();
            attributes = attributes.Except(systemAttributes).ToList();

            if (attributes.Any())
            {
                root.Add(new KVObject("Attributes", attributes));
            }

            if (systemAttributes.Any())
            {
                root.Add(new KVObject("SystemAttributes", systemAttributes));
            }
        }

        string subrectDefinition = null;

        if (editInfo is ResourceEditInfo2 redi2)
        {
            subrectDefinition = redi2.SearchableUserData.Where(x => x.Key.ToLowerInvariant() == "subrectdefinition").FirstOrDefault().Value as string;
        }
        else
        {
            var extraStringData = (Blocks.ResourceEditInfoStructs.ExtraStringData)editInfo.Structs[ResourceEditInfo.REDIStruct.ExtraStringData];
            subrectDefinition = extraStringData.List.Where(x => x.Name.ToLowerInvariant() == "subrectdefinition").FirstOrDefault()?.Value;
        }

        if (subrectDefinition != null)
        {
            var toolattributes = new List<KVObject>()
                {
                    new KVObject("SubrectDefinition", subrectDefinition)
                };

            root.Add(new KVObject("ToolAttributes", toolattributes));
        }

        using var ms = new MemoryStream();
        KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(ms, root);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// TextureColorA will be equal to TextureColor, as they are both the first layer
    /// </summary>
    internal sealed class LayeredTextureNameComparer : IEqualityComparer<string>
    {
        public readonly HashSet<string> _unlayeredTextures;

        public LayeredTextureNameComparer(HashSet<string> unlayeredTextures)
        {
            _unlayeredTextures = unlayeredTextures;
        }

        public bool Equals(string layered, string unlayered)
        {
            if (layered.Equals(unlayered, StringComparison.Ordinal))
            {
                return true;
            }

            var valid = _unlayeredTextures.Contains(unlayered);
            if (!valid && !_unlayeredTextures.Contains(layered))
            {
                return false;
            }

            if (!valid)
            {
                (layered, unlayered) = (unlayered, layered);
            }

            if (unlayered.StartsWith("Texture", StringComparison.Ordinal))
            {
                unlayered = unlayered[7..];
            }

            var layerNameConventions = new[] { $"Texture{unlayered}A", $"Texture{unlayered}0", $"TextureLayer1{unlayered}" };
            return layerNameConventions.Contains(layered, StringComparer.Ordinal);
        }

        public int GetHashCode(string str) => str.GetHashCode(StringComparison.Ordinal);
    }

    internal sealed class ChannelMappingComparer : IEqualityComparer<ChannelMapping>
    {
        private readonly LayeredTextureNameComparer _layeredTextureNameComparer;

        public ChannelMappingComparer(LayeredTextureNameComparer layeredTextureNameComparer)
        {
            _layeredTextureNameComparer = layeredTextureNameComparer;
        }

        public bool Equals(ChannelMapping x, ChannelMapping y)
        {
            return x.Item1 == y.Item1 && _layeredTextureNameComparer.Equals(x.Item2, y.Item2);
        }

        public int GetHashCode(ChannelMapping obj) => obj.GetHashCode();
    }
}
