using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using Channel = ValveResourceFormat.CompiledShader.ChannelMapping;

#nullable disable

namespace ValveResourceFormat.IO;

public sealed class MaterialExtract
{
    public readonly struct UnpackInfo
    {
        public string TextureType { get; init; }
        public string FileName { get; init; }
        public Channel Channel { get; init; }
    }

    private static bool IsDefaultTexture(string textureFileName)
        => textureFileName.StartsWith("materials/default/", StringComparison.OrdinalIgnoreCase);

    private readonly Material material;
    private readonly ResourceEditInfo editInfo;
    private readonly IFileLoader fileLoader;
    private readonly IShaderDataProvider shaderDataProvider;

    public MaterialExtract(Material material, ResourceEditInfo editInfo, IFileLoader fileLoader,
        IShaderDataProvider shaderDataProvider = null)
    {
        ArgumentNullException.ThrowIfNull(material);
        this.material = material;
        this.editInfo = editInfo;
        this.fileLoader = fileLoader;
        this.shaderDataProvider = shaderDataProvider ?? new BasicShaderDataProvider();
    }

    public MaterialExtract(Resource resource, IFileLoader fileLoader = null)
        : this((Material)resource.DataBlock, resource.EditInfo, fileLoader)
    {
        if (fileLoader is not null)
        {
            shaderDataProvider = new ShaderDataProvider(fileLoader);
        }
    }

    public ContentFile ToContentFile()
    {
        var vmat = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(ToValveMaterial())
        };

        var handledTextures = new List<string>(material.TextureParams.Count);
        foreach (var (type, filePath) in material.TextureParams)
        {
            var textureCompiledName = filePath + GameFileLoader.CompiledFileSuffix;
            if (handledTextures.Contains(textureCompiledName))
            {
                continue;
            }

            handledTextures.Add(textureCompiledName);

            var texture = fileLoader?.LoadFile(textureCompiledName);
            if (texture is null)
            {
                continue;
            }

            var images = GetTextureUnpackInfos(type, filePath, omitDefaults: true, omitUniforms: true);
            var vtex = new TextureExtract(texture).ToMaterialMaps(images);

            if (vtex.SubFiles.Count > 0)
            {
                vmat.AdditionalFiles.Add(vtex);
            }
            else
            {
                vtex.Dispose();
            }
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
            // texture_suffix_ext_hash
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
                foreach (var suffix in BasicShaderDataProvider.CommonTextureSuffixes.Values)
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

    public IEnumerable<UnpackInfo> GetTextureUnpackInfos(string textureType, string texturePath, bool omitDefaults, bool omitUniforms)
    {
        var isInput0 = true;
        var shaderProvidedInputs = shaderDataProvider.GetInputsForTexture(textureType, material).ToList();
        foreach (var (channel, newTextureType) in shaderProvidedInputs)
        {
            if (omitUniforms && material.VectorParams.ContainsKey(newTextureType))
            {
                continue;
            }

            string desiredSuffix = null;
            if (!isInput0)
            {
                desiredSuffix = shaderDataProvider.GetSuffixForInputTexture(newTextureType, material) ?? "-" + channel;
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

    public string ToValveMaterial()
    {
        var root = new KVObject("Layer0", [])
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

        var originalTextures = new KVObject("Compiled Textures", []);
        foreach (var (key, value) in material.TextureParams)
        {
            foreach (var unpackInfo in GetTextureUnpackInfos(key, value, false, true))
            {
                root.Add(new KVObject(unpackInfo.TextureType, unpackInfo.FileName));
            }

            originalTextures.Add(new KVObject(key, value));
        }

        root.Add(originalTextures);

        if (material.DynamicExpressions.Count > 0)
        {
            var dynamicExpressionsNode = new KVObject("DynamicParams", []);
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
            "physicssurfaceproperties1",
            "physicssurfaceproperties2",
            "physicssurfaceproperties3",
            "physicssurfaceproperties4",
            "worldmappingwidth",
            "worldmappingheight"
        };

        if (attributes.Count > 0)
        {
            // Some attributes are actually SystemAttributes
            var systemAttributes = attributes.Where(attribute => attributesThatAreSystemAttributes.Contains(attribute.Name.ToLowerInvariant())).ToList();
            attributes = attributes.Except(systemAttributes).ToList();

            if (attributes.Count > 0)
            {
                root.Add(new KVObject("Attributes", attributes));
            }

            if (systemAttributes.Count > 0)
            {
                root.Add(new KVObject("SystemAttributes", systemAttributes));
            }
        }

        var subrectDefinition = editInfo.SearchableUserData
            .Where(x => x.Key.Equals("subrectdefinition", StringComparison.OrdinalIgnoreCase)).FirstOrDefault().Value;

        if (subrectDefinition is string def)
        {
            var toolattributes = new List<KVObject>()
                {
                    new("SubrectDefinition", def)
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
        public static readonly string[] layerNameConventions =
        [
            "Texture{0}A", // hlvr
            "Texture{0}1", // cs2
            "Texture{0}0",
            "TextureLayer1{0}", // steamvr
        ];

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

            return layerNameConventions.Any(template => string.Equals(string.Format(CultureInfo.InvariantCulture, template, unlayered), layered, StringComparison.Ordinal));
        }

        public int GetHashCode(string str) => str.GetHashCode(StringComparison.Ordinal);
    }

    internal sealed class ChannelMappingComparer : IEqualityComparer<(Channel Channel, string Name)>
    {
        private readonly LayeredTextureNameComparer _layeredTextureNameComparer;

        public ChannelMappingComparer(LayeredTextureNameComparer layeredTextureNameComparer)
        {
            _layeredTextureNameComparer = layeredTextureNameComparer;
        }

        public bool Equals((Channel Channel, string Name) x, (Channel Channel, string Name) y)
        {
            return x.Channel == y.Channel && _layeredTextureNameComparer.Equals(x.Name, y.Name);
        }

        public int GetHashCode((Channel Channel, string Name) obj) => obj.GetHashCode();
    }
}
