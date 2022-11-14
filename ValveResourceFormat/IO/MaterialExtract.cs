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
        _OneChannel,
        GA,
        _TwoChannels,
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

        //["multiblend"] = null,

        ["hero"] = new()
        {
            ["g_tColor"] = new[] { (Channel.RGB, "TextureColor"), (Channel.A, "TextureTranslucency") },
            ["g_tNormal"] = new[] { (Channel.RGB, "TextureNormal") },
            ["g_tCubeMap"] = new[] { (Channel.RGBA, "TextureCubeMap") },
            ["g_tCubeMapSeparateMask"] = new[] { (Channel.G, "TextureCubeMapSeparateMask") },
            ["g_tFresnelWarp"] = new[] { (Channel.R, "TextureFresnelWarpRim"), (Channel.G, "TextureFresnelWarpColor"), (Channel.B, "TextureFresnelWarpSpec") },
            ["g_tMasks1"] = new[] { (Channel.R, "TextureDetailMask"), (Channel.G, "TextureDiffuseWarpMask"), (Channel.B, "TextureMetalnessMask"), (Channel.A, "TextureSelfIllumMask") },
            ["g_tMasks2"] = new[] { (Channel.R, "TextureSpecularMask"), (Channel.G, "TextureRimMask"), (Channel.B, "TextureTintByBaseMask"), (Channel.A, "TextureSpecularExponent") },
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
            var texture = fileLoader?.LoadFile(filePath + "_c");
            if (texture is null)
            {
                continue;
            }

            var images = GetInputImagesForTexture(type, filePath, true, material.IntParams, material.VectorParams);
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

    public IEnumerable<UnpackInfo> GetInputImagesForTexture(string textureType, string texturePath, bool omitDefault,
            Dictionary<string, long> intParams, Dictionary<string, Vector4> vectorParams)
    {
        var shader = material.ShaderName[..^4]; // strip '.vfx'

        if (TextureMappings.TryGetValue(shader, out var mappings) && mappings.TryGetValue(textureType, out var channelMappings))
        {
            var isInput0 = true;
            foreach (var mapping in channelMappings)
            {
                var (channel, newTextureType) = mapping;
                if (newTextureType.Length == 0 && !TryFigureOutNonStaticMap(shader, textureType, intParams, out newTextureType))
                {
                    continue;
                }

                if (vectorParams.ContainsKey(newTextureType))
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
                    if (omitDefault)
                    {
                        isInput0 = false;
                        continue;
                    }

                    keepOriginalExtension = true;
                }

                var fileName = OutTextureName(texturePath, keepOriginalExtension, desiredSuffix);

                yield return new UnpackInfo
                {
                    TextureType = newTextureType,
                    FileName = fileName,
                    Channel = channel
                };

                isInput0 = false;
            }

            yield break;
        }

        // No pack info for this texture, so just clean up filename and try to guess its type.
        var guessedTextureType = textureType.Replace("g_t", "Texture", StringComparison.Ordinal);
        if (!vectorParams.ContainsKey(guessedTextureType))
        {
            Console.WriteLine($"Missing pack info for {textureType} in {shader}");
            yield return new UnpackInfo
            {
                TextureType = guessedTextureType,
                FileName = OutTextureName(texturePath, false),
                Channel = Channel.RGBA
            };
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
            foreach (var unpackInfo in GetInputImagesForTexture(key, value, false, material.IntParams, material.VectorParams))
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
            var systemAttributes = attributes.Where(attribute => attributesThatAreSystemAttributes.Contains(attribute.Name.ToLower())).ToList();
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
            subrectDefinition = redi2.SearchableUserData.Where(x => x.Key.ToLower() == "subrectdefinition").FirstOrDefault().Value as string;
        }
        else
        {
            var extraStringData = (Blocks.ResourceEditInfoStructs.ExtraStringData)editInfo.Structs[ResourceEditInfo.REDIStruct.ExtraStringData];
            subrectDefinition = extraStringData.List.Where(x => x.Name.ToLower() == "subrectdefinition").FirstOrDefault()?.Value;
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
}
