using System.Globalization;
using System.IO;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Source1.Material;

/// <summary>
/// A material written for a target game: the shader, its parameters, and the images its textures need.
/// </summary>
internal sealed class MaterialTranslation(string shader)
{
    public readonly record struct Texture(string InputName, string FileName, MaterialInput Source);

    /// <summary>Gets the shader file name.</summary>
    public string Shader { get; } = shader;

    /// <summary>Gets what could not be written for the target and was left out or changed.</summary>
    public List<string> Warnings { get; } = [];

    public Dictionary<string, long> Features { get; } = [];

    public Dictionary<string, float> Floats { get; } = [];

    public Dictionary<string, Vector4> Vectors { get; } = [];

    public List<Texture> Textures { get; } = [];

    public string? SurfaceProperty { get; set; }

    /// <summary>Gets the physics surface properties of the layers after the first.</summary>
    public List<string> LayerSurfaceProperties { get; } = [];

    /// <summary>
    /// Gets the material in Valve material format.
    /// </summary>
    public string ToValveMaterial()
    {
        var root = KVObject.ListCollection();
        root.Add("shader", Shader);

        foreach (var (key, value) in Features)
        {
            root.Add(key, value);
        }

        foreach (var (key, value) in Floats)
        {
            root.Add(key, value);
        }

        foreach (var (key, value) in Vectors)
        {
            root.Add(key, string.Create(CultureInfo.InvariantCulture, $"[{value.X:F6} {value.Y:F6} {value.Z:F6} {value.W:F6}]"));
        }

        foreach (var texture in Textures)
        {
            root.Add(texture.InputName, texture.FileName);
        }

        if (SurfaceProperty is not null)
        {
            var systemAttributes = KVObject.ListCollection();
            systemAttributes.Add("PhysicsSurfaceProperties", SurfaceProperty);

            for (var i = 0; i < LayerSurfaceProperties.Count; i++)
            {
                var number = (i + 2).ToString(CultureInfo.InvariantCulture);
                systemAttributes.Add("PhysicsSurfaceProperties" + number, LayerSurfaceProperties[i]);
            }

            root.Add("SystemAttributes", systemAttributes);
        }

        using var ms = new MemoryStream();
        KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(ms, new KVDocument(new(), "Layer0", root));
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }
}

/// <summary>
/// Writes a <see cref="MaterialDescription"/> with the shader and input names of a target game.
/// </summary>
internal sealed partial class MaterialWriter
{
    private readonly MaterialDescription description;
    private readonly ShaderProfile profile;
    private readonly Func<string, string?, string?, string> getFileName;
    private readonly MaterialTranslation translation;

    private MaterialWriter(MaterialDescription description, ShaderProfile profile, Func<string, string?, string?, string> getFileName)
    {
        this.description = description;
        this.profile = profile;
        this.getFileName = getFileName;
        translation = new MaterialTranslation(profile.Shader + ".vfx");
    }

    /// <summary>
    /// Writes the material for a target.
    /// </summary>
    /// <param name="description">The material to write.</param>
    /// <param name="target">The shaders of the target game.</param>
    /// <param name="getFileName">Gets the image file name for a compiled texture path, an optional file name suffix, and the input name of an image computed from the texture, or null.</param>
    public static MaterialTranslation Write(MaterialDescription description, MaterialTarget target, Func<string, string?, string?, string> getFileName)
    {
        var writer = new MaterialWriter(description, target.Find(description), getFileName);
        writer.WriteAll();
        return writer.translation;
    }

    private void WriteAll()
    {
        foreach (var (name, value) in profile.FixedFeatures)
        {
            SetFeature(name, value);
        }

        WriteBlend();

        if (description.RenderBackfaces)
        {
            SetFeature(profile.BackfaceFeature, 1);
        }

        if (description.UseSecondTexCoord)
        {
            SetFeature(profile.SecondTexCoordFeature, 1);
        }

        if (profile.Specular)
        {
            if (description.SpecularDirect)
            {
                SetFeature("F_SPECULAR_DIRECT", 1);
            }

            if (description.SpecularIndirect)
            {
                SetFeature("F_SPECULAR_INDIRECT", 1);
            }
        }

        if (description.UseVertexColor)
        {
            SetFeature(profile.VertexColorFeature, 1);
        }

        if (description.Opacity != 1f)
        {
            SetFloat(profile.OpacityParam, "opacity", description.Opacity);
        }

        WriteTextureTransform();

        if (description.Layers.Count > profile.Layers)
        {
            translation.Warnings.Add($"{profile.Shader} has {profile.Layers} layer(s), {description.Layers.Count - profile.Layers} layer(s) were left out.");
        }

        var layerCount = Math.Min(description.Layers.Count, profile.Layers);
        for (var i = 0; i < layerCount; i++)
        {
            WriteLayer(description.Layers[i], i);
        }

        WriteShared();
        WriteWater();
        WriteLayerBlending();
        WriteEffects();
        translation.SurfaceProperty = description.SurfaceProperty;
    }

    private void WriteTextureTransform()
    {
        var scale = description.UvScale;
        var offset = description.UvOffset;
        var rotation = description.UvRotation;

        if (scale == Vector2.One && offset == Vector2.Zero && rotation == 0f)
        {
            return;
        }

        if (profile.TextureTransform == TextureTransform.None)
        {
            translation.Warnings.Add($"{profile.Shader} has no texture transform, it is left out.");
            return;
        }

        // The description scales around the origin and rotates around the middle of the texture
        if (profile.TextureTransform == TextureTransform.PerLayer)
        {
            var radians = float.DegreesToRadians(rotation);
            var middle = (new Vector2(0.5f) * scale) - new Vector2(0.5f);
            offset += Vector2.Transform(middle, Matrix3x2.CreateRotation(radians));
            SetFeature("F_TEXTURETRANSFORMS", 1);
        }

        foreach (var uv in GetTextureTransformNames())
        {
            if (scale != Vector2.One)
            {
                SetVector(uv.Scale, new Vector4(scale, 0f, 0f));
            }

            if (offset != Vector2.Zero)
            {
                SetVector(uv.Offset, new Vector4(offset, 0f, 0f));
            }

            if (rotation != 0f)
            {
                SetFloat(uv.Rotation, "texture rotation", rotation);
            }
        }
    }

    private readonly record struct UvNames(string Scale, string Offset, string Rotation);

    private List<UvNames> GetTextureTransformNames()
    {
        if (profile.TextureTransform == TextureTransform.Single)
        {
            return [new UvNames("g_vTexCoordScale", "g_vTexCoordOffset", "g_flTexCoordRotation")];
        }

        var names = new List<UvNames>();

        for (var layer = 1; layer <= profile.Layers; layer++)
        {
            var number = layer.ToString(CultureInfo.InvariantCulture);

            names.Add(new UvNames($"g_vLayer{number}TexCoordScale", $"g_vLayer{number}TexCoordOffset", $"g_flLayer{number}TexCoordRotation"));
            names.Add(new UvNames($"g_vLayer{number}NormalTexCoordScale", $"g_vLayer{number}NormalTexCoordOffset", $"g_flLayer{number}NormalTexCoordRotation"));
        }

        return names;
    }

    private void WriteWater()
    {
        var water = profile.Water;

        if (description.RefractTint != Vector3.One)
        {
            SetColor(water?.RefractTint, "refract tint", description.RefractTint);
        }

        if (description.WaterFogColor is { } fogColor)
        {
            SetColor(water?.FogColor, "water fog color", fogColor);
        }

        if (description.WaterFogEnd > description.WaterFogStart)
        {
            SetFloat(water?.FogStart, "water fog start", description.WaterFogStart);
            SetFloat(water?.FogDepth, "water fog depth", description.WaterFogEnd - description.WaterFogStart);
        }

        if (description.RefractAmount != 0f)
        {
            SetFloat(water?.RefractAmount, "refract amount", description.RefractAmount);
        }

        if (AddTexture(MaterialSlots.Flow, "TextureFlow", description.Flow, "_flow"))
        {
            SetFeature(water?.FlowFeature, 1);

            if (description.FlowColor)
            {
                SetFeature(water?.FlowColorFeature, 1);
            }
        }

        AddTexture(MaterialSlots.FlowNoise, "TextureNoise", description.FlowNoise, "_noise");
    }

    private void WriteBlend()
    {
        var mode = description.BlendMode;

        if (profile.BlendModeFeature is not null)
        {
            SetFeature(profile.BlendModeFeature, profile.BlendModeValues[(int)mode]);

            if (mode == MaterialBlendMode.AlphaTest)
            {
                SetFloat(profile.AlphaTestParam, "alpha test reference", description.AlphaTestReference);
            }

            return;
        }

        if (mode == MaterialBlendMode.Opaque)
        {
            return;
        }

        if (!profile.BlendFlags)
        {
            translation.Warnings.Add($"{profile.Shader} cannot blend as {mode}, it is opaque.");
            return;
        }

        switch (mode)
        {
            case MaterialBlendMode.AlphaTest:
                SetFeature("F_ALPHA_TEST", 1);
                SetFloat(profile.AlphaTestParam, "alpha test reference", description.AlphaTestReference);
                return;
            case MaterialBlendMode.Additive:
                SetFeature("F_ADDITIVE_BLEND", 1);
                return;
            case MaterialBlendMode.Mod2x when profile.Mod2xFeature is not null:
                SetFeature(profile.Mod2xFeature, 1);
                return;
        }

        if (mode != MaterialBlendMode.Translucent)
        {
            translation.Warnings.Add($"{profile.Shader} cannot blend as {mode}, it blends as Translucent.");
        }

        SetFeature("F_TRANSLUCENT", 1);
    }

    private void WriteLayer(MaterialLayer layer, int index)
    {
        if (index < profile.TintParams.Length && layer.ColorTint != Vector3.One)
        {
            SetColor(profile.TintParams[index], "color tint", layer.ColorTint);
        }

        var colorName = index == 0 && profile.ColorInput is not null ? profile.ColorInput : GetInputName("Color", index);
        AddTexture(MaterialSlots.Color, colorName, layer.Color, null);

        if (AddTexture(MaterialSlots.Normal, GetInputName("Normal", index), layer.Normal, "_normal"))
        {
            SetSlotFeature(MaterialSlots.Normal);
        }

        AddTexture(MaterialSlots.Roughness, GetInputName("Roughness", index), layer.Roughness, "_rough");

        if (AddTexture(MaterialSlots.Metalness, GetInputName("Metalness", index), layer.Metalness, "_metal"))
        {
            SetSlotFeature(MaterialSlots.Metalness);
        }
        else if (layer.Metalness.Constant is { } metalness && index < profile.MetalnessParams.Length)
        {
            SetFloat(profile.MetalnessParams[index], "metalness", metalness);
        }

        if (AddTexture(MaterialSlots.AmbientOcclusion, GetInputName("AmbientOcclusion", index), layer.AmbientOcclusion, "_ao"))
        {
            SetSlotFeature(MaterialSlots.AmbientOcclusion);
        }

        if (AddTexture(MaterialSlots.TintMask, GetInputName("TintMask", index), layer.TintMask, "_mask"))
        {
            SetSlotFeature(MaterialSlots.TintMask);
        }
    }

    private void WriteShared()
    {
        var translucencyAdded = AddTexture(MaterialSlots.Translucency, GetInputName("Translucency", 0), description.Translucency, "_trans");
        if (!translucencyAdded && description.Translucency.IsPresent)
        {
            translation.Warnings.Add($"{profile.Shader} has no translucency input, the alpha is left out.");
        }

        if (AddTexture(MaterialSlots.SelfIllum, GetInputName("SelfIllumMask", 0), description.SelfIllumMask, "_selfillum"))
        {
            SetSlotFeature(MaterialSlots.SelfIllum);

            if (description.SelfIllumScale != 1f)
            {
                SetFloat(profile.SelfIllumScaleParam, "self illumination scale", description.SelfIllumScale);
            }

            if (description.SelfIllumTint != Vector3.One)
            {
                SetColor(profile.SelfIllumTintParam, "self illumination tint", description.SelfIllumTint);
            }
        }

        if (AddTexture(MaterialSlots.Detail, "TextureDetail", description.Detail, "_detail"))
        {
            SetSlotFeature(MaterialSlots.Detail);
        }

        if (description.Layers.Count > 1
            && AddTexture(MaterialSlots.BlendMask, "Texture" + profile.BlendMaskName, description.BlendMask, "_blend"))
        {
            SetSlotFeature(MaterialSlots.BlendMask);
        }
    }

    private string GetInputName(string slot, int layer) => profile.Naming switch
    {
        LayerNaming.Plain => "Texture" + slot,
        LayerNaming.Number1 => "Texture" + slot + (layer + 1).ToString(CultureInfo.InvariantCulture),
        LayerNaming.Number0 => "Texture" + slot + layer.ToString(CultureInfo.InvariantCulture),
        LayerNaming.Letter => "Texture" + slot + (char)('A' + layer),
        _ => "TextureLayer" + (layer + 1).ToString(CultureInfo.InvariantCulture) + slot,
    };

    private bool AddTexture(MaterialSlots slot, string inputName, MaterialInput source, string? suffix)
    {
        if (source.Texture is null)
        {
            return false;
        }

        if (!profile.Slots.HasFlag(slot))
        {
            translation.Warnings.Add($"{profile.Shader} has no {slot} input, {inputName} is left out.");
            return false;
        }

        if (translation.Textures.Exists(texture => texture.InputName == inputName))
        {
            return false;
        }

        var isGenerated = source.Function is not null || source.PairFunction is not null;
        var fileName = getFileName(source.Texture, suffix, isGenerated ? inputName : null);
        translation.Textures.Add(new MaterialTranslation.Texture(inputName, fileName, source));
        return true;
    }

    private void SetSlotFeature(MaterialSlots slot)
    {
        if (profile.SlotFeatures.TryGetValue(slot, out var feature))
        {
            SetFeature(feature, 1);
        }
    }

    private void SetFeature(string? name, int value)
    {
        if (name is not null)
        {
            translation.Features[name] = value;
        }
    }

    private void SetFloat(string? name, string what, float value)
    {
        if (name is null)
        {
            translation.Warnings.Add($"{profile.Shader} has no {what}, it is left out.");
            return;
        }

        translation.Floats[name] = value;
    }

    private void SetVector(string name, Vector4 value) => translation.Vectors[name] = value;

    // The shaders convert colors from gamma to linear space, the description holds linear colors
    private void SetColor(string? name, string what, Vector3 linear)
    {
        if (name is null)
        {
            translation.Warnings.Add($"{profile.Shader} has no {what}, it is left out.");
            return;
        }

        var color = profile.LinearColors ? linear : ColorSpace.SrgbLinearToGamma(linear);
        SetVector(name, new Vector4(color, 0f));
    }
}
