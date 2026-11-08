namespace ValveResourceFormat.Source1.Material;

/// <summary>
/// A special effect of a surface that only some shaders of a target draw.
/// </summary>
internal enum MaterialEffect
{
    /// <summary>No effect.</summary>
    None,

    /// <summary>Waves washing over a beach.</summary>
    BeachFoam,

    /// <summary>A dome that reflects a cube map with parallax.</summary>
    MoonDome,
}

/// <summary>
/// A detail texture laid over a layer.
/// </summary>
internal sealed class MaterialLayerDetail
{
    /// <summary>Gets the compiled texture path.</summary>
    public required string Texture { get; init; }

    /// <summary>Gets how often the texture repeats over the layer.</summary>
    public Vector2 Scale { get; init; } = new(4f);

    /// <summary>Gets the tint of the detail, as written in the material.</summary>
    public Vector3 Tint { get; init; } = Vector3.One;

    /// <summary>Gets how strongly the detail shows.</summary>
    public float BlendFactor { get; init; } = 1f;
}

/// <summary>
/// How the layers of a world surface are blended by the mask, with a colored border where one layer meets the other.
/// </summary>
internal sealed class MaterialBorderBlend
{
    /// <summary>Gets the color of the border, as written in the material.</summary>
    public Vector3 Tint { get; init; } = Vector3.One;

    /// <summary>Gets how soft the transition between the layers is.</summary>
    public float Softness { get; init; } = 0.5f;

    /// <summary>Gets how strongly the border shows.</summary>
    public float BorderStrength { get; init; } = 0.5f;

    /// <summary>Gets how far the border lies from the edge of the layer.</summary>
    public float BorderOffset { get; init; }

    /// <summary>Gets how soft the edge of the border is.</summary>
    public float BorderSoftness { get; init; } = 0.5f;
}

/// <summary>
/// The tints, blending and detail textures of the layers of a world surface, in the units of the Source 1 material.
/// </summary>
internal sealed class MaterialLayerBlending
{
    /// <summary>Gets the tint of each layer, as written in the material.</summary>
    public List<Vector3> Tints { get; } = [];

    /// <summary>Gets the border blending, or null when the layers blend by the mask alone.</summary>
    public MaterialBorderBlend? Border { get; set; }

    /// <summary>Gets the detail texture of each layer, at most two.</summary>
    public List<MaterialLayerDetail> Details { get; } = [];

    /// <summary>Gets whether the second detail replaces the first by the pattern instead of both multiplying.</summary>
    public bool SelectDetailPattern { get; set; }

    /// <summary>Gets or sets the scale of the detail of the normal map of each layer, or null when the normal maps have none.</summary>
    public Vector2? BumpDetailScale { get; set; }
}

internal sealed partial class MaterialDescription
{
    /// <summary>Gets or sets the blending of the layers of a world surface, or null for other materials.</summary>
    public MaterialLayerBlending? LayerBlending { get; set; }

    /// <summary>Gets the physics surface properties of the second, third and fourth layer, in that order.</summary>
    public List<string> LayerSurfaceProperties { get; } = [];

    /// <summary>Gets or sets whether the surface is drawn without testing and writing the depth.</summary>
    public bool DisableDepth { get; set; }

    /// <summary>Gets or sets the special effect of the surface.</summary>
    public MaterialEffect Effect { get; set; }

    /// <summary>Gets or sets the cube map that <see cref="MaterialEffect.MoonDome"/> reflects.</summary>
    public MaterialInput CubeMap { get; set; }

    /// <summary>Gets or sets how far the cube map of <see cref="MaterialEffect.MoonDome"/> shifts with the view.</summary>
    public float CubeParallax { get; set; }

    /// <summary>Gets the three color stops of a gradient that modulates the color, each with its position in the last component, or null.</summary>
    public Vector4[]? GradientStops { get; set; }
}
