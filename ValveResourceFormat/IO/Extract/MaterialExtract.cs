using System.Collections.Frozen;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using Channel = ValveResourceFormat.CompiledShader.ChannelMapping;

namespace ValveResourceFormat.IO;

/// <summary>
/// Extracts Source 2 materials to editable vmat format.
/// </summary>
public sealed class MaterialExtract
{
    /// <summary>
    /// Information about how to unpack a texture channel.
    /// </summary>
    public readonly struct UnpackInfo
    {
        /// <summary>Gets the texture type.</summary>
        public string TextureType { get; init; }
        /// <summary>Gets the file name.</summary>
        public string FileName { get; init; }
        /// <summary>Gets the channel mapping.</summary>
        public Channel Channel { get; init; }
    }

    private const string LayerNamePrefix = "layer_name_";

    private static readonly FrozenSet<string> SystemAttributeNames = new[]
    {
        "physicssurfaceproperties",
        "physicssurfaceproperties1",
        "physicssurfaceproperties2",
        "physicssurfaceproperties3",
        "physicssurfaceproperties4",
        "worldmappingwidth",
        "worldmappingheight",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly Material material;
    private readonly ResourceEditInfo? editInfo;
    private readonly IFileLoader? fileLoader;
    private readonly IShaderDataProvider shaderDataProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="MaterialExtract"/> class.
    /// </summary>
    public MaterialExtract(Material material, ResourceEditInfo? editInfo, IFileLoader? fileLoader,
        IShaderDataProvider? shaderDataProvider = null)
    {
        ArgumentNullException.ThrowIfNull(material);
        this.material = material;
        this.editInfo = editInfo;
        this.fileLoader = fileLoader;
        this.shaderDataProvider = shaderDataProvider ?? new BasicShaderDataProvider();
    }

    /// <inheritdoc cref="MaterialExtract(Material, ResourceEditInfo, IFileLoader, IShaderDataProvider)"/>
    public MaterialExtract(Resource resource, IFileLoader? fileLoader = null)
        : this((Material)resource.DataBlock!, resource.EditInfo, fileLoader)
    {
        if (fileLoader is not null)
        {
            shaderDataProvider = new ShaderDataProvider(fileLoader);
        }
    }

    /// <summary>
    /// Converts the material to a content file with associated textures.
    /// </summary>
    public ContentFile ToContentFile()
    {
        var textureInputs = GetTextureInputs();
        var vmat = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(ToValveMaterial(textureInputs))
        };

        foreach (var inputs in textureInputs.GroupBy(static input => input.Texture))
        {
            if (inputs.Key is not { } texture)
            {
                continue;
            }

            var textureExtract = new TextureExtract(texture);
            var vtex = textureExtract.ToMaterialMaps(inputs.Select(static input => input.Image));
            vtex.TakeOwnership(texture);

            if (inputs.FirstOrDefault(static input => input.IsPrimary) is { Image.FileName: { } primaryFileName }
                && textureExtract.GetImageSettings() is { } settings)
            {
                vtex.AddSubFile(Path.GetFileNameWithoutExtension(primaryFileName) + ".txt", () => Encoding.UTF8.GetBytes(settings));
            }

            vtex.KeepFullPath = true;
            vmat.AdditionalFiles.Add(vtex);
        }

        return vmat;
    }

    /// <summary>
    /// Gets the images the texture parameters are written as, each input key once, with the loaded texture each one
    /// is unpacked from. A key that is the first input of a parameter is not taken by another parameter's other inputs.
    /// A texture no image is unpacked from is not kept.
    /// </summary>
    private List<(Resource? Texture, UnpackInfo Image, bool IsPrimary)> GetTextureInputs()
    {
        var textures = material.TextureParams.Select(param =>
        {
            var texture = fileLoader?.LoadFileCompiled(param.Value);
            return (Texture: texture, Images: GetTextureUnpackInfos(param.Key, param.Value, texture?.DataBlock as Texture).ToList());
        }).ToList();

        var primaryKeys = textures.Where(static texture => texture.Images.Count > 0).Select(static texture => texture.Images[0].TextureType).ToHashSet();
        var inputs = new List<(Resource? Texture, UnpackInfo Image, bool IsPrimary)>();
        var inputKeys = new HashSet<string>();

        foreach (var (texture, images) in textures)
        {
            var unpacked = false;

            for (var i = 0; i < images.Count; i++)
            {
                if ((i == 0 || !primaryKeys.Contains(images[i].TextureType)) && inputKeys.Add(images[i].TextureType))
                {
                    inputs.Add((texture, images[i], i == 0));
                    unpacked = true;
                }
            }

            if (!unpacked)
            {
                texture?.Dispose();
            }
        }

        return inputs;
    }

    /// <summary>
    /// Gets the images a texture parameter is written as, one per shader input. They are named after the compiled
    /// texture with the input key appended. HDR textures are not unpacked, and a texture
    /// the compiler generated from a constant is left to the constant.
    /// </summary>
    public IEnumerable<UnpackInfo> GetTextureUnpackInfos(string textureType, string texturePath, Texture? texture)
    {
        var inputs = shaderDataProvider.GetInputsForTexture(textureType, material).ToList();

        if (inputs.Count == 0)
        {
            yield break;
        }

        var isConstant = material.VectorParams.ContainsKey(inputs[0].Name);

        if (isConstant && IsGeneratedFromConstant(textureType, texturePath))
        {
            yield break;
        }

        var baseName = Path.ChangeExtension(texturePath, null);

        if (texture?.ExtraData?.ContainsKey(VTexExtraData.SHEET) == true)
        {
            yield return new UnpackInfo
            {
                TextureType = inputs[0].Name,
                FileName = baseName + ".mks",
                Channel = inputs[0].Channel,
            };

            yield break;
        }

        var isHdr = texture?.IsHighDynamicRange ?? false;
        var extension = isHdr ? ".exr" : ".png";

        for (var i = 0; i < inputs.Count; i++)
        {
            var (channel, input) = inputs[i];

            if (i > 0 && isHdr)
            {
                break;
            }

            if (material.VectorParams.ContainsKey(input))
            {
                continue;
            }

            var fileName = $"{baseName}_{input}{extension}";

            if (texture != null && TextureExtract.IsSliced(texture))
            {
                fileName = TextureExtract.GetFirstSliceFileName(fileName, texture.Depth);
            }

            yield return new UnpackInfo
            {
                TextureType = input,
                FileName = fileName,
                Channel = channel,
            };
        }
    }

    /// <summary>
    /// Whether the compiler generated the texture from a constant, which names it
    /// <c>{material}_vmat_{parameter}_{hash}</c>.
    /// </summary>
    private bool IsGeneratedFromConstant(string textureType, string texturePath)
    {
        var prefix = $"{Path.GetFileNameWithoutExtension(material.Resource?.FileName)}_vmat_{textureType.ToLowerInvariant()}_";
        var name = Path.GetFileNameWithoutExtension(texturePath);

        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length && name[prefix.Length..].All(char.IsAsciiHexDigit);
    }

    /// <summary>
    /// Converts the material to Valve material format as a string.
    /// </summary>
    public string ToValveMaterial()
    {
        var textureInputs = GetTextureInputs();

        foreach (var texture in textureInputs.Select(static input => input.Texture).Distinct())
        {
            texture?.Dispose();
        }

        return ToValveMaterial(textureInputs);
    }

    private string ToValveMaterial(List<(Resource? Texture, UnpackInfo Image, bool IsPrimary)> textureInputs)
    {
        var root = KVObject.ListCollection();
        root.Add("shader", material.ShaderName);

        foreach (var (key, value) in material.IntParams)
        {
            root.Add(key, value);
        }

        foreach (var (key, value) in material.FloatParams)
        {
            root.Add(key, FormatFloat(value));
        }

        foreach (var (key, value) in material.VectorParams)
        {
            if (!textureInputs.Exists(input => input.Image.TextureType == key))
            {
                root.Add(key, FormatVector(value));
            }
        }

        foreach (var (_, image, _) in textureInputs)
        {
            root.Add(image.TextureType, image.FileName);
        }

        if (material.DynamicExpressions.Count > 0)
        {
            var dynamicExpressions = fileLoader?.LoadShader(material.ShaderName) is { } shader
                ? material.GetDynamicExpressions(shader.SelectMany(program => program.VariableDescriptions).Select(variable => variable.Name))
                : material.DynamicExpressions;

            var dynamicExpressionsNode = KVObject.ListCollection();
            root.Add("DynamicParams", dynamicExpressionsNode);
            foreach (var (key, value) in dynamicExpressions)
            {
                dynamicExpressionsNode.Add(key, value);
            }
        }

        var attributes = new List<KeyValuePair<string, KVObject>>();

        foreach (var (key, value) in material.IntAttributes)
        {
            // not defined by user, so skip it
            if (key.Equals("representativetexturewidth", StringComparison.OrdinalIgnoreCase)
            || key.Equals("representativetextureheight", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            attributes.Add(new(key, value));
        }

        foreach (var (key, value) in material.FloatAttributes)
        {
            // An integer attribute also compiles to a float one of the same name
            if (!material.IntAttributes.ContainsKey(key))
            {
                attributes.Add(new(key, FormatFloat(value)));
            }
        }

        foreach (var (key, value) in material.VectorAttributes)
        {
            attributes.Add(new(key, FormatVector(value)));
        }

        foreach (var (key, value) in material.StringAttributes)
        {
            attributes.Add(new(key, value ?? string.Empty));
        }

        var materialLayers = KVObject.ListCollection();

        foreach (var (key, value) in material.StringAttributes)
        {
            if (key.StartsWith(LayerNamePrefix, StringComparison.OrdinalIgnoreCase) && key.Length > LayerNamePrefix.Length
                && key[LayerNamePrefix.Length..].All(char.IsAsciiDigit))
            {
                attributes.RemoveAll(attribute => attribute.Key == key);

                if (!string.IsNullOrEmpty(value))
                {
                    materialLayers.Add($"MaterialLayerReference_{key[LayerNamePrefix.Length..]}", GetLayerMaterialPath(value));
                }
            }
        }

        if (attributes.Count > 0)
        {
            // Some attributes are actually SystemAttributes
            var systemAttributes = attributes.Where(attribute => SystemAttributeNames.Contains(attribute.Key)).ToList();
            attributes = attributes.Except(systemAttributes).ToList();

            if (attributes.Count > 0)
            {
                root.Add("Attributes", KVObject.ListCollection(attributes));
            }

            if (systemAttributes.Count > 0)
            {
                root.Add("SystemAttributes", KVObject.ListCollection(systemAttributes));
            }
        }

        var subrectDefinition = editInfo?.SearchableUserData
            .FirstOrDefault(x => x.Key.Equals("subrectdefinition", StringComparison.OrdinalIgnoreCase)).Value;

        if (subrectDefinition?.ValueType == KVValueType.String)
        {
            var toolattributes = KVObject.ListCollection();
            toolattributes.Add("SubrectDefinition", (string)subrectDefinition);

            root.Add("ToolAttributes", toolattributes);
        }

        if (materialLayers.Count > 0)
        {
            root.Add("MaterialLayers", materialLayers);
        }

        using var ms = new MemoryStream();
        KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(ms, new KVDocument(new(), "Layer0", root));
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    /// <summary>
    /// Gets the path of a layer material. A bare compiled layer name resolves to the content path of the input
    /// dependency with that file name.
    /// </summary>
    private string GetLayerMaterialPath(string layerName)
    {
        var path = layerName.Replace('\\', '/');

        if (path.Contains('/', StringComparison.Ordinal) || material.Resource?.EditInfo is not { } editInfo)
        {
            return path;
        }

        return editInfo.InputDependencies.Concat(editInfo.AdditionalInputDependencies)
            .Select(static dependency => dependency.ContentRelativeFilename.Replace('\\', '/'))
            .FirstOrDefault(dependency => Path.GetFileName(dependency).Equals(path, StringComparison.OrdinalIgnoreCase))
            ?? path;
    }

    private static string FormatFloat(float value) => value.ToString("0.0#########", CultureInfo.InvariantCulture);

    private static string FormatVector(Vector4 value)
        => $"[{FormatFloat(value.X)} {FormatFloat(value.Y)} {FormatFloat(value.Z)} {FormatFloat(value.W)}]";

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

        public bool Equals(string? layered, string? unlayered)
        {
            if (layered is null || unlayered is null)
            {
                return layered == unlayered;
            }

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
