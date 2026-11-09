using System.Collections.Frozen;
using ValveResourceFormat.IO;

namespace ValveResourceFormat.Source1.Material;

/// <summary>How the input texture names of a shader carry the layer.</summary>
internal enum LayerNaming
{
    /// <summary>TextureColor</summary>
    Plain,

    /// <summary>TextureColor1, TextureColor2</summary>
    Number1,

    /// <summary>TextureColor0, TextureColor1</summary>
    Number0,

    /// <summary>TextureColorA, TextureColorB</summary>
    Letter,

    /// <summary>TextureLayer1Color, TextureLayer2Color</summary>
    LayerPrefix,
}

/// <summary>The texture coordinate parameters of a shader.</summary>
internal enum TextureTransform
{
    /// <summary>The shader has none.</summary>
    None,

    /// <summary>One scale, offset and rotation.</summary>
    Single,

    /// <summary>A scale, offset and rotation for each layer and its normal map, and the shader scales around the center of the texture.</summary>
    PerLayer,
}

/// <summary>The channels of a material that a shader can take.</summary>
[Flags]
internal enum MaterialSlots
{
    None = 0,
    Color = 1 << 0,
    Normal = 1 << 1,
    Roughness = 1 << 2,
    Metalness = 1 << 3,
    AmbientOcclusion = 1 << 4,
    TintMask = 1 << 5,
    Translucency = 1 << 6,
    SelfIllum = 1 << 7,
    Detail = 1 << 8,
    BlendMask = 1 << 9,
    Flow = 1 << 10,
    FlowNoise = 1 << 11,
    LayerDetail = 1 << 20,
    CubeMap = 1 << 21,
}

/// <summary>The parameters a water or refracting shader takes.</summary>
internal sealed class WaterNames
{
    public required string RefractTint { get; init; }

    public string? RefractAmount { get; init; }

    public string? FogColor { get; init; }

    public string? FogStart { get; init; }

    public string? FogDepth { get; init; }

    public string? FlowFeature { get; init; }

    public string? FlowColorFeature { get; init; }
}

/// <summary>The names a shader of a game uses for what a <see cref="MaterialDescription"/> says.</summary>
internal sealed class ShaderProfile
{
    public required string Shader { get; init; }

    public LayerNaming Naming { get; init; }

    public int Layers { get; init; } = 1;

    public MaterialSlots Slots { get; init; }

    public (string Name, int Value)[] FixedFeatures { get; init; } = [];

    /// <summary>The feature a texture in a slot turns on.</summary>
    public Dictionary<MaterialSlots, string> SlotFeatures { get; init; } = [];

    /// <summary>Whether the shader blends with the features F_ALPHA_TEST, F_TRANSLUCENT and F_ADDITIVE_BLEND.</summary>
    public bool BlendFlags { get; init; }

    public string? Mod2xFeature { get; init; }

    /// <summary>A feature that selects the blend mode by number, with the number of each <see cref="MaterialBlendMode"/>.</summary>
    public string? BlendModeFeature { get; init; }

    public int[] BlendModeValues { get; init; } = [];

    public string? BackfaceFeature { get; init; } = "F_RENDER_BACKFACES";

    /// <summary>The feature that reads the textures other than the color from the second set of texture coordinates.</summary>
    public string? SecondTexCoordFeature { get; init; }

    /// <summary>Whether the shader has the features F_SPECULAR_DIRECT and F_SPECULAR_INDIRECT.</summary>
    public bool Specular { get; init; }

    public string? VertexColorFeature { get; init; }

    public string BlendMaskName { get; init; } = "Mask";

    public string? ColorInput { get; init; }

    /// <summary>The feature that stores the cube texture of the shader uncompressed in low dynamic range instead of as BC6H.</summary>
    public string? LdrCubeFeature { get; init; }

    public string[] TintParams { get; init; } = ["g_vColorTint"];

    public string[] MetalnessParams { get; init; } = [];

    public string? AlphaTestParam { get; init; } = "g_flAlphaTestReference";

    public string? OpacityParam { get; init; } = "g_flOpacityScale";

    public string? SelfIllumScaleParam { get; init; } = "g_flSelfIllumScale";

    public string? SelfIllumTintParam { get; init; } = "g_vSelfIllumTint";

    public TextureTransform TextureTransform { get; init; }

    public WaterNames? Water { get; init; }

    /// <summary>Whether colors are written as they are, and not in the gamma space that most shaders convert to linear.</summary>
    public bool LinearColors { get; init; }

    /// <summary>Whether the shader takes the tints, blending, detail textures and surface properties of the layers of a world surface.</summary>
    public bool LayerBlending { get; init; }

    /// <summary>The feature that draws without testing and writing the depth.</summary>
    public string? DepthFeature { get; init; }

    /// <summary>The feature that modulates the color with a gradient, with its three color stops.</summary>
    public string? GradientFeature { get; init; }

    /// <summary>The parameter of how far the cube map shifts with the view.</summary>
    public string? CubeParallaxParam { get; init; }
}

/// <summary>The shaders of a game that materials of each role are written with.</summary>
internal sealed class MaterialTarget
{
    private const MaterialSlots LitSlots = MaterialSlots.Color | MaterialSlots.Normal | MaterialSlots.Roughness | MaterialSlots.Metalness
        | MaterialSlots.AmbientOcclusion | MaterialSlots.TintMask | MaterialSlots.Translucency | MaterialSlots.SelfIllum | MaterialSlots.Detail;

    private const MaterialSlots LayerSlots = MaterialSlots.Color | MaterialSlots.Normal | MaterialSlots.Roughness | MaterialSlots.AmbientOcclusion;

    private const MaterialSlots DecalSlots = MaterialSlots.Color | MaterialSlots.Normal | MaterialSlots.Roughness
        | MaterialSlots.AmbientOcclusion | MaterialSlots.Metalness | MaterialSlots.Translucency;

    private const MaterialSlots UnlitOverlaySlots = MaterialSlots.Color | MaterialSlots.Translucency;

    private const MaterialSlots LitOverlaySlots = UnlitOverlaySlots | MaterialSlots.Normal | MaterialSlots.Roughness
        | MaterialSlots.AmbientOcclusion;

    private static readonly int[] DecalBlendModes = [0, 0, 0, 2, 1, 1];

    private static readonly int[] OverlayBlendModes = [0, 2, 1, 4, 3, 5];

    private readonly FrozenDictionary<MaterialRole, ShaderProfile> roles;

    private MaterialTarget(ShaderProfile lit, ShaderProfile unlit, Dictionary<MaterialRole, ShaderProfile> roles)
    {
        Lit = lit;
        Unlit = unlit;
        this.roles = roles.ToFrozenDictionary();
    }

    /// <summary>Gets the shader for materials that are lit and have no shader for their role.</summary>
    public ShaderProfile Lit { get; }

    /// <summary>Gets the shader for materials that are not lit and have no shader for their role.</summary>
    public ShaderProfile Unlit { get; }

    public static MaterialTarget? Get(Game game) => game switch
    {
        Game.Csgo => Cs2,
        Game.Hlvr => HalfLifeAlyx,
        Game.Dota => Dota2,
        Game.Citadel => Deadlock,
        _ => null,
    };

    /// <summary>Gets the shaders of the special effects of surfaces, which replace the shader of the role.</summary>
    public Dictionary<MaterialEffect, ShaderProfile> Effects { get; init; } = [];

    public ShaderProfile Find(MaterialDescription description)
    {
        if (Effects.TryGetValue(description.Effect, out var effectProfile))
        {
            return effectProfile;
        }

        if (description.Role == MaterialRole.Cable)
        {
            return Cable;
        }

        if (roles.TryGetValue(description.Role, out var profile))
        {
            return profile;
        }

        var layer = description.Layers[0];
        var isLit = layer.Normal.IsPresent || layer.Roughness.IsPresent || layer.Metalness.IsPresent;

        return isLit ? Lit : Unlit;
    }

    // Every game has the cable shader of the particle renderer that draws ropes.
    private static readonly ShaderProfile Cable = new()
    {
        Shader = "cables",
        Slots = MaterialSlots.Color,
        BackfaceFeature = null,
    };

    private static readonly MaterialTarget Cs2 = CreateCs2();

    private static readonly MaterialTarget HalfLifeAlyx = CreateHalfLifeAlyx();

    private static readonly MaterialTarget Dota2 = CreateDota2();

    private static readonly MaterialTarget Deadlock = CreateDeadlock();

    private static MaterialTarget CreateCs2()
    {
        var lit = new ShaderProfile
        {
            Shader = "csgo_vertexlitgeneric",
            Slots = LitSlots,
            BlendFlags = true,
            Specular = true,
            SecondTexCoordFeature = "F_FORCE_UV2",
            TextureTransform = TextureTransform.Single,
            SlotFeatures =
            {
                [MaterialSlots.TintMask] = "F_TINT_MASK",
                [MaterialSlots.SelfIllum] = "F_SELF_ILLUM",
                [MaterialSlots.Detail] = "F_DETAIL_TEXTURE",
            },
        };

        var character = new ShaderProfile
        {
            Shader = "csgo_character",
            Slots = LitSlots & ~(MaterialSlots.SelfIllum | MaterialSlots.Detail),
            BlendFlags = true,
            TextureTransform = TextureTransform.Single,
            SlotFeatures = { [MaterialSlots.TintMask] = "F_TINT_MASK" },
        };

        var world = new ShaderProfile
        {
            Shader = "csgo_lightmappedgeneric",
            Naming = LayerNaming.LayerPrefix,
            Slots = LayerSlots | MaterialSlots.Translucency | MaterialSlots.LayerDetail,
            FixedFeatures = [("F_LAYERS", 0)],
            BlendFlags = true,
            Specular = true,
            TintParams = [],
            LayerBlending = true,
            TextureTransform = TextureTransform.PerLayer,
        };

        var blend2 = new ShaderProfile
        {
            Shader = "csgo_lightmappedgeneric",
            Naming = LayerNaming.LayerPrefix,
            Layers = 2,
            Slots = LayerSlots | MaterialSlots.Translucency | MaterialSlots.BlendMask | MaterialSlots.LayerDetail,
            FixedFeatures = [("F_LAYERS", 1)],
            BlendFlags = true,
            Specular = true,
            BlendMaskName = "BlendModulation",
            TintParams = [],
            LayerBlending = true,
            TextureTransform = TextureTransform.PerLayer,
            SlotFeatures = { [MaterialSlots.BlendMask] = "F_FANCY_BLENDING" },
        };

        var blend4 = new ShaderProfile
        {
            Shader = "csgo_lightmapped_4wayblend",
            Naming = LayerNaming.LayerPrefix,
            Layers = 4,
            Slots = LayerSlots | MaterialSlots.Detail,
            AlphaTestParam = null,
            OpacityParam = null,
            SlotFeatures = { [MaterialSlots.Detail] = "F_DETAILTEXTURE" },
        };

        var unlit = new ShaderProfile
        {
            Shader = "csgo_unlitgeneric",
            Slots = MaterialSlots.Color | MaterialSlots.Translucency,
            BlendModeFeature = "F_BLEND_MODE",
            BlendModeValues = [0, 2, 1, 4, 3, 5],
            VertexColorFeature = "F_VERTEX_COLOR",
            OpacityParam = null,
            TextureTransform = TextureTransform.Single,
            DepthFeature = "F_DISABLE_Z_BUFFERING",
            GradientFeature = "F_GRADIENTMODULATION",
        };

        var beachFoam = new ShaderProfile
        {
            Shader = "csgo_beachfoam",
            Slots = MaterialSlots.Color,
            TextureTransform = TextureTransform.Single,
        };

        var moonDome = new ShaderProfile
        {
            Shader = "csgo_moondome",
            Slots = MaterialSlots.Color | MaterialSlots.CubeMap,
            TextureTransform = TextureTransform.Single,
            CubeParallaxParam = "g_flCubeParallax",
        };

        var water = new ShaderProfile
        {
            Shader = "csgo_water",
            Slots = MaterialSlots.Color | MaterialSlots.Normal | MaterialSlots.Flow | MaterialSlots.FlowNoise,
            AlphaTestParam = null,
            OpacityParam = null,
            TextureTransform = TextureTransform.Single,
            Water = new WaterNames
            {
                RefractTint = "g_vRefractionTint",
                FogColor = "g_vWaterFogColor",
                FogStart = "g_flWaterStart",
                FogDepth = "g_flWaterDepth",
                FlowFeature = "F_FLOW_NORMALS",
                FlowColorFeature = "F_FLOW_COLOR",
            },
        };

        var refract = new ShaderProfile
        {
            Shader = "csgo_refract",
            Slots = MaterialSlots.Normal,
            BackfaceFeature = null,
            TintParams = [],
            LinearColors = true,
            Water = new WaterNames { RefractTint = "g_vColorTint", RefractAmount = "g_flRefractScale" },
        };

        var decal = new ShaderProfile
        {
            Shader = "csgo_projected_decals",
            Slots = DecalSlots,
            BlendModeFeature = "F_BLEND_MODE",
            BlendModeValues = DecalBlendModes,
            BackfaceFeature = null,
            TintParams = [],
            AlphaTestParam = null,
            OpacityParam = null,
            SlotFeatures = { [MaterialSlots.Normal] = "F_NORMAL_MAP" },
        };

        var overlay = new ShaderProfile
        {
            Shader = "csgo_static_overlay",
            Slots = UnlitOverlaySlots,
            BlendModeFeature = "F_BLEND_MODE",
            BlendModeValues = OverlayBlendModes,
            TextureTransform = TextureTransform.Single,
        };

        var litOverlay = new ShaderProfile
        {
            Shader = "csgo_static_overlay",
            Slots = LitOverlaySlots,
            FixedFeatures = [("F_LIT", 1)],
            BlendModeFeature = "F_BLEND_MODE",
            BlendModeValues = OverlayBlendModes,
            TextureTransform = TextureTransform.Single,
        };

        var sky = new ShaderProfile
        {
            Shader = "sky",
            Slots = MaterialSlots.Color,
            ColorInput = "SkyTexture",
            LdrCubeFeature = "F_TEXTURE_FORMAT2",
            BackfaceFeature = null,
        };

        return new MaterialTarget(lit, unlit, new()
        {
            [MaterialRole.LitModel] = lit,
            [MaterialRole.Character] = character,
            [MaterialRole.LitWorld] = world,
            [MaterialRole.Blend2] = blend2,
            [MaterialRole.Blend4] = blend4,
            [MaterialRole.Unlit] = unlit,
            [MaterialRole.Water] = water,
            [MaterialRole.Refract] = refract,
            [MaterialRole.Decal] = decal,
            [MaterialRole.Overlay] = overlay,
            [MaterialRole.LitOverlay] = litOverlay,
            [MaterialRole.Sky] = sky,
        })
        {
            Effects =
            {
                [MaterialEffect.BeachFoam] = beachFoam,
                [MaterialEffect.MoonDome] = moonDome,
            },
        };
    }

    private static MaterialTarget CreateHalfLifeAlyx()
    {
        var lit = new ShaderProfile
        {
            Shader = "vr_complex",
            Slots = LitSlots,
            BlendFlags = true,
            MetalnessParams = ["g_flMetalness"],
            SlotFeatures =
            {
                [MaterialSlots.Metalness] = "F_METALNESS_TEXTURE",
                [MaterialSlots.TintMask] = "F_TINT_MASK",
                [MaterialSlots.SelfIllum] = "F_SELF_ILLUM",
                [MaterialSlots.Detail] = "F_DETAIL_TEXTURE",
            },
        };

        var unlit = new ShaderProfile
        {
            Shader = "vr_complex",
            Slots = MaterialSlots.Color | MaterialSlots.Translucency,
            FixedFeatures = [("F_UNLIT", 1)],
            BlendFlags = true,
        };

        var blend2 = new ShaderProfile
        {
            Shader = "vr_simple_2way_blend",
            Naming = LayerNaming.Letter,
            Layers = 2,
            Slots = LayerSlots | MaterialSlots.TintMask | MaterialSlots.BlendMask,
            MetalnessParams = ["g_flMetalnessA", "g_flMetalnessB"],
            AlphaTestParam = null,
            OpacityParam = null,
            SlotFeatures =
            {
                [MaterialSlots.AmbientOcclusion] = "F_ENABLE_AMBIENT_OCCLUSION",
                [MaterialSlots.TintMask] = "F_ENABLE_TINT_MASKS",
            },
        };

        var decal = new ShaderProfile
        {
            Shader = "vr_projected_decals",
            Slots = DecalSlots,
            BlendModeFeature = "F_BLEND_MODE",
            BlendModeValues = DecalBlendModes,
            BackfaceFeature = null,
            TintParams = [],
            AlphaTestParam = null,
            OpacityParam = null,
            SlotFeatures = { [MaterialSlots.Normal] = "F_NORMAL_MAP" },
        };

        var overlay = new ShaderProfile
        {
            Shader = "vr_static_overlay",
            Slots = UnlitOverlaySlots,
            BlendModeFeature = "F_BLEND_MODE",
            BlendModeValues = OverlayBlendModes,
            TextureTransform = TextureTransform.Single,
        };

        var litOverlay = new ShaderProfile
        {
            Shader = "vr_static_overlay",
            Slots = LitOverlaySlots,
            FixedFeatures = [("F_LIT", 1)],
            BlendModeFeature = "F_BLEND_MODE",
            BlendModeValues = OverlayBlendModes,
            TextureTransform = TextureTransform.Single,
        };

        var sky = new ShaderProfile
        {
            Shader = "sky",
            Slots = MaterialSlots.Color,
            ColorInput = "SkyTexture",
            BackfaceFeature = null,
        };

        return new MaterialTarget(lit, unlit, new()
        {
            [MaterialRole.LitModel] = lit,
            [MaterialRole.LitWorld] = lit,
            [MaterialRole.Blend2] = blend2,
            [MaterialRole.Unlit] = unlit,
            [MaterialRole.Decal] = decal,
            [MaterialRole.Overlay] = overlay,
            [MaterialRole.LitOverlay] = litOverlay,
            [MaterialRole.Sky] = sky,
        });
    }

    private static MaterialTarget CreateDota2()
    {
        var lit = new ShaderProfile
        {
            Shader = "global_lit_simple",
            Slots = MaterialSlots.Color | MaterialSlots.Normal | MaterialSlots.TintMask | MaterialSlots.Translucency | MaterialSlots.SelfIllum,
            BlendFlags = true,
            Mod2xFeature = "F_MOD_2X",
            SelfIllumScaleParam = null,
            SelfIllumTintParam = null,
            SlotFeatures =
            {
                [MaterialSlots.Normal] = "F_NORMAL_MAP",
                [MaterialSlots.TintMask] = "F_TINT_MASK",
            },
        };

        var unlit = new ShaderProfile
        {
            Shader = "global_lit_simple",
            Slots = MaterialSlots.Color | MaterialSlots.Translucency,
            FixedFeatures = [("F_FULLBRIGHT", 1)],
            BlendFlags = true,
            Mod2xFeature = "F_MOD_2X",
        };

        var blend2 = new ShaderProfile
        {
            Shader = "multiblend",
            Naming = LayerNaming.Number0,
            Layers = 2,
            Slots = MaterialSlots.Color | MaterialSlots.Normal | MaterialSlots.TintMask | MaterialSlots.BlendMask,
            BlendMaskName = "RevealMask1",
            TintParams = ["g_vColorTint0", "g_vColorTint1"],
            AlphaTestParam = null,
            OpacityParam = null,
            SlotFeatures =
            {
                [MaterialSlots.Normal] = "F_NORMAL_MAP",
                [MaterialSlots.TintMask] = "F_TINT_MASK",
            },
        };

        var blend4 = new ShaderProfile
        {
            Shader = "multiblend",
            Naming = LayerNaming.Number0,
            Layers = 4,
            Slots = MaterialSlots.Color | MaterialSlots.Normal | MaterialSlots.TintMask | MaterialSlots.BlendMask,
            BlendMaskName = "RevealMask1",
            TintParams = ["g_vColorTint0", "g_vColorTint1", "g_vColorTint2", "g_vColorTint3"],
            AlphaTestParam = null,
            OpacityParam = null,
            SlotFeatures =
            {
                [MaterialSlots.Normal] = "F_NORMAL_MAP",
                [MaterialSlots.TintMask] = "F_TINT_MASK",
            },
        };

        return new MaterialTarget(lit, unlit, new()
        {
            [MaterialRole.LitModel] = lit,
            [MaterialRole.LitWorld] = lit,
            [MaterialRole.Blend2] = blend2,
            [MaterialRole.Blend4] = blend4,
            [MaterialRole.Unlit] = unlit,
        });
    }

    private static MaterialTarget CreateDeadlock()
    {
        var lit = new ShaderProfile
        {
            Shader = "pbr",
            Naming = LayerNaming.Number1,
            Slots = LitSlots,
            BlendFlags = true,
            TintParams = ["g_vColorTint1"],
            AlphaTestParam = "g_flAlphaTestReference1",
            OpacityParam = "g_flOpacityScale1",
            SelfIllumScaleParam = "g_flSelfIllumScale1",
            SelfIllumTintParam = "g_vSelfIllumTint1",
            SlotFeatures =
            {
                [MaterialSlots.SelfIllum] = "F_SELF_ILLUM",
                [MaterialSlots.Detail] = "F_DETAIL",
            },
        };

        var unlit = new ShaderProfile
        {
            Shader = "pbr",
            Naming = LayerNaming.Number1,
            Slots = MaterialSlots.Color | MaterialSlots.Translucency,
            FixedFeatures = [("F_UNLIT", 1)],
            BlendFlags = true,
            TintParams = ["g_vColorTint1"],
            AlphaTestParam = "g_flAlphaTestReference1",
            OpacityParam = "g_flOpacityScale1",
        };

        var blend2 = new ShaderProfile
        {
            Shader = "environment_blend",
            Naming = LayerNaming.Number1,
            Layers = 2,
            Slots = MaterialSlots.Color | MaterialSlots.Normal | MaterialSlots.Roughness | MaterialSlots.Metalness | MaterialSlots.TintMask | MaterialSlots.BlendMask,
            BackfaceFeature = null,
            BlendMaskName = "RevealMask2",
            TintParams = ["g_vColorTint1", "g_vColorTint2"],
            AlphaTestParam = null,
            OpacityParam = null,
        };

        var decal = new ShaderProfile
        {
            Shader = "projected_decals",
            Slots = MaterialSlots.Color | MaterialSlots.Normal | MaterialSlots.Roughness | MaterialSlots.Translucency,
            BackfaceFeature = null,
            AlphaTestParam = null,
        };

        var overlay = new ShaderProfile
        {
            Shader = "citadel_overlay",
            Slots = MaterialSlots.Color | MaterialSlots.Normal | MaterialSlots.Roughness | MaterialSlots.Translucency,
            BackfaceFeature = null,
            AlphaTestParam = null,
            OpacityParam = null,
        };

        var sky = new ShaderProfile
        {
            Shader = "sky",
            Slots = MaterialSlots.Color,
            ColorInput = "SkyTexture",
            BackfaceFeature = null,
        };

        return new MaterialTarget(lit, unlit, new()
        {
            [MaterialRole.LitModel] = lit,
            [MaterialRole.LitWorld] = lit,
            [MaterialRole.Blend2] = blend2,
            [MaterialRole.Unlit] = unlit,
            [MaterialRole.Decal] = decal,
            [MaterialRole.Overlay] = overlay,
            [MaterialRole.LitOverlay] = overlay,
            [MaterialRole.Sky] = sky,
        });
    }
}
