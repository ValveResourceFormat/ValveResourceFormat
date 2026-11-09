using Channel = ValveResourceFormat.CompiledShader.ChannelMapping;

namespace ValveResourceFormat.Source1.Material;

/// <summary>
/// What a material is used for, which decides the shader it is written with in a target game.
/// </summary>
internal enum MaterialRole
{
    /// <summary>A lit model surface.</summary>
    LitModel,

    /// <summary>A lit world surface, lightmapped where the game has lightmaps.</summary>
    LitWorld,

    /// <summary>Two world layers blended by a mask or vertex data.</summary>
    Blend2,

    /// <summary>Four world layers blended by vertex data.</summary>
    Blend4,

    /// <summary>A surface that ignores lighting.</summary>
    Unlit,

    /// <summary>A water surface.</summary>
    Water,

    /// <summary>A surface that refracts what is behind it.</summary>
    Refract,

    /// <summary>A decal projected or laid over other surfaces.</summary>
    Decal,

    /// <summary>A mesh laid over the surface it covers that ignores lighting.</summary>
    Overlay,

    /// <summary>A mesh laid over the surface it covers that is lit.</summary>
    LitOverlay,

    /// <summary>A lit model surface of a character, whose metalness comes from a packed mask texture.</summary>
    Character,

    /// <summary>The cable that a particle effect draws.</summary>
    Cable,

    /// <summary>The sky.</summary>
    Sky,
}

/// <summary>
/// How a material combines with what is behind it.
/// </summary>
internal enum MaterialBlendMode
{
    /// <summary>No blending.</summary>
    Opaque,

    /// <summary>Pixels below the alpha test reference are discarded.</summary>
    AlphaTest,

    /// <summary>Alpha blended.</summary>
    Translucent,

    /// <summary>Added to what is behind.</summary>
    Additive,

    /// <summary>Multiplied with what is behind and doubled.</summary>
    Mod2x,

    /// <summary>Multiplied with what is behind.</summary>
    Multiply,
}

/// <summary>
/// A value of a material channel: a channel of a texture, a value computed from the channels of a texture, a constant, or nothing.
/// </summary>
internal readonly struct MaterialInput
{
    /// <summary>Gets the compiled texture path, as in <see cref="ResourceTypes.Material.TextureParams"/>, or null when the input has no texture.</summary>
    public string? Texture { get; init; }

    /// <summary>Gets the channels of <see cref="Texture"/> that hold the value.</summary>
    public Channel? Channel { get; init; }

    /// <summary>
    /// Gets a function that computes the value from the normalized RGBA of a texel of <see cref="Texture"/>.
    /// When set, <see cref="Channel"/> is not used.
    /// </summary>
    public Func<Vector4, float>? Function { get; init; }

    /// <summary>Gets the compiled texture path of a second texture that <see cref="PairFunction"/> reads.</summary>
    public string? SecondTexture { get; init; }

    /// <summary>
    /// Gets a function that computes the value from the normalized RGBA of a texel of <see cref="Texture"/> and of <see cref="SecondTexture"/>.
    /// When set, <see cref="Channel"/> and <see cref="Function"/> are not used.
    /// </summary>
    public Func<Vector4, Vector4, float>? PairFunction { get; init; }

    /// <summary>Gets whether the green channel of the texture is inverted, for a normal map of the other convention.</summary>
    public bool InvertGreen { get; init; }

    /// <summary>Gets the constant value when the input has no texture.</summary>
    public float? Constant { get; init; }

    /// <summary>Gets whether the input has a texture or a constant.</summary>
    public bool IsPresent => Texture is not null || Constant is not null;

    /// <summary>Creates an input that reads channels of a texture.</summary>
    public static MaterialInput FromTexture(string texture, Channel channel) => new() { Texture = texture, Channel = channel };

    /// <summary>Creates an input that computes its value from the texels of a texture.</summary>
    public static MaterialInput FromFunction(string texture, Func<Vector4, float> function) => new() { Texture = texture, Function = function };

    /// <summary>Creates an input that computes its value from the texels of two textures.</summary>
    public static MaterialInput FromPair(string texture, string secondTexture, Func<Vector4, Vector4, float> function)
        => new() { Texture = texture, SecondTexture = secondTexture, PairFunction = function };

    /// <summary>Creates an input with a constant value.</summary>
    public static MaterialInput FromConstant(float value) => new() { Constant = value };
}

/// <summary>
/// The channels of one layer of a material.
/// </summary>
internal sealed class MaterialLayer
{
    /// <summary>Gets or sets the base color.</summary>
    public MaterialInput Color { get; set; }

    /// <summary>Gets or sets the tangent space normal.</summary>
    public MaterialInput Normal { get; set; }

    /// <summary>Gets or sets the roughness, 0 being smooth.</summary>
    public MaterialInput Roughness { get; set; }

    /// <summary>Gets or sets the metalness.</summary>
    public MaterialInput Metalness { get; set; }

    /// <summary>Gets or sets the ambient occlusion.</summary>
    public MaterialInput AmbientOcclusion { get; set; }

    /// <summary>Gets or sets the mask of where the color tint applies.</summary>
    public MaterialInput TintMask { get; set; }

    /// <summary>Gets or sets the color tint.</summary>
    public Vector3 ColorTint { get; set; } = Vector3.One;
}

/// <summary>
/// A material described independently of the game it comes from, to be written for another game.
/// Colors are in linear space.
/// </summary>
internal sealed partial class MaterialDescription
{
    /// <summary>Gets or sets what the material is used for.</summary>
    public MaterialRole Role { get; set; }

    /// <summary>Gets or sets how the material blends with what is behind it.</summary>
    public MaterialBlendMode BlendMode { get; set; }

    /// <summary>Gets or sets the alpha test reference used by <see cref="MaterialBlendMode.AlphaTest"/>.</summary>
    public float AlphaTestReference { get; set; } = 0.5f;

    /// <summary>Gets or sets the opacity scale.</summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>Gets or sets whether both faces of the geometry are drawn.</summary>
    public bool RenderBackfaces { get; set; }

    /// <summary>Gets or sets whether the textures that are not the color are read from the second set of texture coordinates of the mesh.</summary>
    public bool UseSecondTexCoord { get; set; }

    /// <summary>Gets or sets the translucency, which is the alpha of the color when the material blends.</summary>
    public MaterialInput Translucency { get; set; }

    /// <summary>Gets or sets the mask of the self illumination.</summary>
    public MaterialInput SelfIllumMask { get; set; }

    /// <summary>Gets or sets the self illumination tint.</summary>
    public Vector3 SelfIllumTint { get; set; } = Vector3.One;

    /// <summary>Gets or sets the self illumination scale.</summary>
    public float SelfIllumScale { get; set; } = 1f;

    /// <summary>Gets or sets the detail texture, which modulates the color.</summary>
    public MaterialInput Detail { get; set; }

    /// <summary>Gets or sets the mask that blends the layers.</summary>
    public MaterialInput BlendMask { get; set; }

    /// <summary>Gets or sets the tint of what shows through a refracting surface.</summary>
    public Vector3 RefractTint { get; set; } = Vector3.One;

    /// <summary>Gets or sets the color that deep water fades to, or null when the material has none.</summary>
    public Vector3? WaterFogColor { get; set; }

    /// <summary>Gets or sets the depth in units where the water starts to fade to <see cref="WaterFogColor"/>.</summary>
    public float WaterFogStart { get; set; }

    /// <summary>Gets or sets the depth in units where the water has faded completely to <see cref="WaterFogColor"/>.</summary>
    public float WaterFogEnd { get; set; }

    /// <summary>Gets or sets how far a refracting surface bends what is behind it.</summary>
    public float RefractAmount { get; set; }

    /// <summary>Gets or sets the map of the direction that water flows in.</summary>
    public MaterialInput Flow { get; set; }

    /// <summary>Gets or sets the noise that varies the flow of water over time.</summary>
    public MaterialInput FlowNoise { get; set; }

    /// <summary>Gets or sets whether the color of water follows the flow too.</summary>
    public bool FlowColor { get; set; }

    /// <summary>Gets or sets whether lights make a highlight on the surface.</summary>
    public bool SpecularDirect { get; set; }

    /// <summary>Gets or sets whether the surface reflects its surroundings.</summary>
    public bool SpecularIndirect { get; set; }

    /// <summary>Gets or sets whether the colors of the vertices tint the surface.</summary>
    public bool UseVertexColor { get; set; }

    /// <summary>Gets or sets the texture coordinate scale.</summary>
    public Vector2 UvScale { get; set; } = Vector2.One;

    /// <summary>Gets or sets the texture coordinate offset.</summary>
    public Vector2 UvOffset { get; set; }

    /// <summary>Gets or sets the texture coordinate rotation in degrees.</summary>
    public float UvRotation { get; set; }

    /// <summary>Gets or sets the name of the physics surface property.</summary>
    public string? SurfaceProperty { get; set; }

    /// <summary>Gets the layers of the material, at most four. The first layer is always present.</summary>
    public List<MaterialLayer> Layers { get; } = [new MaterialLayer()];
}
