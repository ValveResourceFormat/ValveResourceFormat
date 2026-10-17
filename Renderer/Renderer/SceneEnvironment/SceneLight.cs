using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>
///     Scene node representing a light source with type, color, and attenuation.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CLightComponent">CLightComponent</seealso>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CBarnLight">CBarnLight</seealso>
public class SceneLight(Scene scene) : SceneNode(scene)
{
    /// <summary>
    ///     Source 2 light entity class names.
    /// </summary>
    public enum EntityType
    {
        /// <summary>Directional environment light (sun/sky).</summary>
        Environment,
        /// <summary>Omnidirectional point light.</summary>
        Omni,
        /// <summary>Cone-shaped spot light.</summary>
        Spot,
        /// <summary>Orthographic projected light (parallel rays within a box).</summary>
        Ortho,
        /// <summary>Second-generation omnidirectional point light.</summary>
        Omni2,
        /// <summary>Barn-door shaped area light.</summary>
        Barn,
        /// <summary>Rectangular area light.</summary>
        Rect,
        /// <summary>Point light stretched along a line.</summary>
        Capsule,
        /// <summary>Directional light other than the environment light.</summary>
        Directional,
    }

    /// <summary>
    ///     How direct lighting should be represented.
    /// </summary>
    public enum DirectLightType
    {
        /// <summary>No direct lighting.</summary>
        None = 0,
        /// <summary>Fully baked into lightmap (cheapest).</summary>
        Static = 1,
        /// <summary>Fully dynamic (most expensive).</summary>
        Dynamic = 2,
        /// <summary>Dynamic direct light with baked shadows.</summary>
        Stationary = 3,
    }

    /// <summary>Runtime cost of a light.</summary>
    public enum LightCost
    {
        /// <summary>Light is free. The result is stored in lightmaps. Strength cannot be adjusted.</summary>
        Static,

        /// <summary>
        /// Light cannot move. Lightmap stores indirect bounce, and shadow mask. Strength can be adjusted at runtime.
        /// </summary>
        Stationary,

        /// <summary>Light can be moved in the world.</summary>
        Dynamic,
    }

    /// <summary>
    ///     Shader light type for rendering calculations.
    /// </summary>
    public enum LightType
    {
        /// <summary>Infinite directional light (no position, parallel rays).</summary>
        Directional,
        /// <summary>Omnidirectional point light radiating in all directions.</summary>
        Point,
        /// <summary>Cone-shaped spot light with inner and outer angles.</summary>
        Spot,
    }

    /// <summary>GPU data and frustum matrix for one face of a barn or omni2 light.</summary>
    public struct BarnFaceData
    {
        /// <summary>Gets or sets the GPU constant buffer data for this light face.</summary>
        public BarnLightConstants GpuData { get; set; }

        /// <summary>Gets or sets the world-to-frustum projection matrix for shadow rendering.</summary>
        public Matrix4x4 WorldToFrustum { get; set; }

        /// <summary>
        /// Gets or sets the inverse of <see cref="WorldToFrustum"/>. Mapping the clip cube through it
        /// recovers the face's lit volume in world space, which the light cull pass bins to tiles.
        /// </summary>
        public Matrix4x4 FrustumToWorld { get; set; }

        /// <summary>
        /// Gets or sets the map from the cube [-1,1]^3 to the illumination OBB, the second volume the
        /// shader rejects fragments outside of. Default when the face has no OBB.
        /// </summary>
        public Matrix4x4 ObbToWorld { get; set; }
    }

    /// <summary>
    ///     Light index to a baked lightmap.
    ///     Range: 0..255 for LightmapGameVersion 1 and 0..3 for LightmapGameVersion 2.
    /// </summary>
    public int StationaryLightIndex { get; set; } = -1;

    /// <summary>Gets or sets the world-space position of the light.</summary>
    public Vector3 Position { get; set; }

    /// <summary>Gets or sets the normalized direction the light faces.</summary>
    public Vector3 Direction { get; set; }

    /// <summary>
    /// Places the light, setting <see cref="SceneNode.Transform"/>, <see cref="Position"/> and
    /// <see cref="Direction"/> from one transform so a parent transform applies to all three.
    /// </summary>
    /// <param name="transform">The light's world transform; its first row is the direction it faces.</param>
    public void PlaceAt(in Matrix4x4 transform)
    {
        Transform = transform;
        Position = transform.Translation;
        Direction = MathUtils.SafeNormalize(transform.GetRow(0).AsVector3());
    }

    /// <summary>Gets or sets the sRGB color of the light.</summary>
    public Vector3 Color { get; set; } = Vector3.One;

    /// <summary>Gets or sets the brightness (intensity) of the light.</summary>
    public float Brightness { get; set; } = 1.0f;

    /// <summary>Gets or sets the linear intensity a barn, rect or omni2 light reaches <see cref="LinearBrightnessDistance"/>
    /// units in front of its luminaire, which is what their faces are lit with.</summary>
    public float LinearBrightness { get; set; } = 1.0f;

    /// <summary>Gets or sets the color temperature in Kelvin that <see cref="Color"/> comes from, or <see langword="null"/>
    /// when the light uses its color as is.</summary>
    public float? ColorTemperature { get; set; }

    /// <summary>Gets or sets the sRGB color of the sky an environment light fills shadows with.</summary>
    public Vector3 SkyColor { get; set; } = Vector3.One;

    /// <summary>Gets or sets the intensity of <see cref="SkyColor"/>.</summary>
    public float SkyIntensity { get; set; } = 1.0f;

    /// <summary>Gets or sets the sRGB color the ground bounces an environment light's sun and sky back up with.</summary>
    public Vector3 SkyAmbientBounce { get; set; } = Vector3.One;

    /// <summary>Gets or sets the additional brightness scale multiplier.</summary>
    public float BrightnessScale { get; set; } = 1.0f;

    /// <summary>Gets or sets the maximum range of the light in world units.</summary>
    public float Range { get; set; } = 512.0f;

    /// <summary>Gets or sets the distance falloff skirt fraction.</summary>
    public float FallOff { get; set; } = 1.0f;

    /// <summary>Gets or sets the inner cone half-angle for spot lights in degrees.</summary>
    public float SpotInnerAngle { get; set; }

    /// <summary>Gets or sets the outer cone half-angle for spot lights in degrees.</summary>
    public float SpotOuterAngle { get; set; } = 45.0f;

    /// <summary>Gets or sets the linear attenuation coefficient.</summary>
    public float AttenuationLinear { get; set; }

    /// <summary>Gets or sets the quadratic attenuation coefficient.</summary>
    public float AttenuationQuadratic { get; set; }

    /// <summary>Gets or sets the horizontal soft edge width for barn lights.</summary>
    public float SoftX { get; set; } = 0.25f;

    /// <summary>Gets or sets the vertical soft edge width for barn lights.</summary>
    public float SoftY { get; set; } = 0.25f;

    /// <summary>Gets or sets the near skirt distance for perspective barn lights.</summary>
    public float SkirtNear { get; set; }

    /// <summary>Gets or sets the shape blend factor for barn lights.</summary>
    public float Shape { get; set; }

    /// <summary>Gets or sets the 2D shear offset for barn lights.</summary>
    public Vector2 Shear { get; set; }

    /// <summary>Gets or sets the half-width, half-height, and near-plane reciprocal for barn lights.</summary>
    public Vector3 SizeParams { get; set; } = new(16, 16, 0.0625f);

    /// <summary>Gets or sets the luminaire (source) size used for area light calculations.</summary>
    public float LuminaireSize { get; set; } = 4f;

    /// <summary>Gets or sets the luminaire anisotropy for capsule-shaped area lights.</summary>
    public float LuminaireAnisotropy { get; set; }

    /// <summary>Gets or sets the luminaire shape index. Barn lights: 0 = point, 1 = disk, 2 = rectangle.
    /// Omni2 lights: -1 or 3 = point, 0 = sphere, 1 = tube, 2 = tube with end caps.</summary>
    public int LuminaireShape { get; set; } = -1;

    /// <summary>Gets or sets the fraction of the screen below which the light starts fading out, zero to never fade.</summary>
    public float FadeSizeStart { get; set; }

    /// <summary>Gets or sets the fraction of the screen at which the light has faded out.</summary>
    public float FadeSizeEnd { get; set; }

    /// <summary>Gets or sets the fraction of the screen below which the light's shadows start fading out, zero to never fade.</summary>
    public float ShadowFadeSizeStart { get; set; }

    /// <summary>Gets or sets the fraction of the screen at which the light's shadows have faded out.</summary>
    public float ShadowFadeSizeEnd { get; set; }

    /// <summary>The roughness no direct specular highlight is sharper than, whatever a light asks for.</summary>
    public const float MinSpecularRoughness = 0.04f;

    /// <summary>Gets or sets the minimum roughness clamped for specular highlight calculations.</summary>
    public float MinRoughness { get; set; } = MinSpecularRoughness;

    /// <summary>Gets or sets the material path of the cookie texture, or <see langword="null"/> if none.</summary>
    public string? CookieTexturePath { get; set; }

    /// <summary>Gets or sets the shader light type used during rendering.</summary>
    public LightType Type { get; set; }

    /// <summary>Gets or sets the entity class that created this light.</summary>
    public EntityType Entity { get; set; }

    /// <summary>Gets or sets the direct lighting type.</summary>
    public DirectLightType DirectLight { get; set; } = DirectLightType.Dynamic;

    /// <summary>Gets or sets whether this light is enabled at spawn.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets whether this light contributes diffuse lighting.</summary>
    public bool RenderDiffuse { get; set; } = true;

    /// <summary>Gets or sets whether this light contributes specular lighting.</summary>
    public bool RenderSpecular { get; set; } = true;

    /// <summary>Gets or sets whether this light passes through transmissive materials.</summary>
    public bool RenderTransmissive { get; set; } = true;

    /// <summary>Gets or sets whether this light casts shadows.</summary>
    public int CastShadows { get; set; } = 1;

    /// <summary>Gets or sets the shadow map resolution in texels.</summary>
    public int ShadowMapSize { get; set; } = 1024;

    /// <summary>Gets or sets whether the precomputed OBB fields below are valid.</summary>
    public bool PrecomputedFieldsValid { get; set; }

    /// <summary>Gets or sets the center of the precomputed oriented bounding box.</summary>
    public Vector3 PrecomputedObbOrigin { get; set; }

    /// <summary>Gets or sets the half-extents of the precomputed oriented bounding box.</summary>
    public Vector3 PrecomputedObbExtent { get; set; }

    /// <summary>Gets or sets the Euler angles of the precomputed oriented bounding box.</summary>
    public Vector3 PrecomputedObbAngles { get; set; }

    /// <summary>Gets or sets the axis-aligned precomputed world bounds.</summary>
    public AABB PrecomputedBounds { get; set; }

    /// <summary>Gets or sets the number of precomputed sub-frusta for omni lights.</summary>
    public int PrecomputedSubfrusta { get; set; }

    /// <summary>Gets or sets the centers of the precomputed sub-OBBs, one per frustum.</summary>
    public Vector3[]? PrecomputedSubObbOrigins { get; set; }

    /// <summary>Gets or sets the half-extents of the precomputed sub-OBBs, one per frustum.</summary>
    public Vector3[]? PrecomputedSubObbExtents { get; set; }

    /// <summary>Gets or sets the Euler angles of the precomputed sub-OBBs, one per frustum.</summary>
    public Vector3[]? PrecomputedSubObbAngles { get; set; }

    /// <summary>Gets the precomputed face data array (1 face for barn lights, 1-6 for omni lights).</summary>
    public BarnFaceData[] BarnFaces { get; private set; } = [];

    /// <summary>Gets or sets whether this light's face data needs to be recomputed. Setting it recalculates all faces.</summary>
    public bool IsDirty { get; set; } = true;

    internal int AdaptiveShadowSize { get; set; }
    internal bool WasDropped { get; set; }

    /// <summary>
    /// Returns whether this light will produce energy based on its properties.
    /// </summary>
    public bool IsVisible => BarnFaces.Length > 0 && BrightnessScale > 0f && Color != Vector3.Zero && FaceBrightness > 0f;

    /// <summary>
    /// Returns whether this is a barn, rect or omni2 light, which takes its brightness as an exposure value and
    /// is lit through <see cref="BarnFaces"/> when it renders in real time.
    /// </summary>
    public bool IsLight2 => Entity is EntityType.Omni2 or EntityType.Barn or EntityType.Rect;

    /// <summary>Gets whether this light lights the whole map from one direction, as the sun does.</summary>
    public bool IsDirectional => Entity is EntityType.Environment or EntityType.Directional;

    /// <summary>Whether the faces are built around the light like an omni2 light, rather than as one barn frustum.</summary>
    internal bool UsesOmni2Faces => Entity is EntityType.Omni2 or EntityType.Rect or EntityType.Omni or EntityType.Capsule;

    /// <summary>Whether the light has a luminaire that can be drawn as geometry, which rect and omni2 lights have.</summary>
    internal bool HasLuminaire => Entity is EntityType.Omni2 or EntityType.Rect;

    /// <summary>Gets or sets the constant attenuation coefficient of an older point or spot light.</summary>
    public float AttenuationConstant { get; set; }

    /// <summary>
    /// Gets or sets how far an older point or spot light is still bright enough to be seen, when it attenuates to
    /// nothing before its range does; <see langword="null"/> when its range is where it ends.
    /// </summary>
    internal float? VisibleRange { get; set; }

    /// <summary>Gets the distance a barn face reaches, which for an older light ends where it is too dim to see.</summary>
    private float FaceRange => VisibleRange is { } visibleRange ? MathF.Min(Range, visibleRange) : Range;

    private LightStyle? style;
    private float styleTime;
    private float styleStartTime;

    /// <summary>Gets or sets how the color of the light changes over time, or <see langword="null"/> when it keeps it.</summary>
    public LightStyle? Style
    {
        get => style;
        set
        {
            style = value;
            styleTime = 0f;
            styleStartTime = value?.PickStartTime() ?? 0f;
            StyleColorScale = value?.Evaluate(-styleStartTime) ?? Vector3.One;
        }
    }

    /// <summary>Gets the linear color <see cref="Style"/> multiplies the color of the light by at the moment.</summary>
    public Vector3 StyleColorScale { get; private set; } = Vector3.One;

    /// <summary>
    /// Returns whether the given entity classname is a recognized light type, and which <see cref="EntityType"/> it maps to.
    /// </summary>
    public static (bool Accepted, EntityType Type) IsAccepted(string classname)
    {
        EntityType? type = classname.ToLowerInvariant() switch
        {
            "light_environment" => EntityType.Environment,
            "light_directional" => EntityType.Directional,
            "light_omni" => EntityType.Omni,
            "light_capsule" => EntityType.Capsule,
            "light_spot" => EntityType.Spot,
            "light_ortho" => EntityType.Ortho,
            "light_omni2" => EntityType.Omni2,
            "light_barn" => EntityType.Barn,
            "light_rect" => EntityType.Rect,
            _ => null,
        };

        return (type.HasValue, type ?? EntityType.Environment);
    }

    /// <summary>
    /// Creates a <see cref="SceneLight"/> populated from entity key-value properties.
    /// </summary>
    public static SceneLight FromEntityProperties(Scene scene, EntityType type, Entity entity)
    {
        var isLight2 = type is EntityType.Omni2 or EntityType.Barn or EntityType.Rect;

        var light = new SceneLight(scene)
        {
            StationaryLightIndex =
                entity.GetInt32Property("bakedshadowindex", entity.GetInt32Property("bakelightindex", -1)),
            Entity = type,
            Type = type switch
            {
                EntityType.Environment or EntityType.Directional or EntityType.Ortho => LightType.Directional,
                EntityType.Omni or EntityType.Capsule or EntityType.Omni2 => LightType.Point,
                EntityType.Barn or EntityType.Rect or EntityType.Spot => LightType.Spot,
                _ => throw new NotImplementedException()
            },

            Color = entity.GetColor32Property("color"),

            Brightness = isLight2
                ? entity.GetFloatProperty("brightness_lumens", 224.0f)
                : entity.GetFloatProperty("brightness", 1.0f),

            BrightnessScale = entity.GetFloatProperty("brightnessscale", 1.0f),
            SkyColor = entity.GetColor32Property("skycolor"),
            SkyIntensity = entity.GetFloatProperty("skyintensity", 1.0f),
            SkyAmbientBounce = entity.GetColor32Property("skyambientbounce"),
            Range = MathF.Max(entity.GetFloatProperty("range", isLight2 ? 256f : 1024f), 1f),
            FallOff = entity.GetFloatProperty("skirt", 0.1f),
            MinRoughness = entity.GetFloatProperty("minroughness", MinSpecularRoughness),

            Enabled = entity.GetInt32Property("enabled", 1) != 0,
            RenderDiffuse = entity.GetInt32Property("renderdiffuse", 1) != 0,
            RenderSpecular = entity.GetInt32Property("renderspecular", 1) == 1, // 0 = off, 1 = real time, 2 = baked into cubemaps
            RenderTransmissive = entity.GetInt32Property("rendertransmissive", 1) != 0,
        };

        if (isLight2)
        {
            // The engine only reads the exposure value, the other brightness units are converted from it by the tools
            light.LinearBrightness = float.Exp2(entity.GetFloatProperty("brightness"));

            switch (entity.GetInt32Property("colormode"))
            {
                case 0:
                    break;

                case 1:
                    light.ColorTemperature = entity.GetFloatProperty("colortemperature", 6600f);
                    light.Color = ColorSpace.ColorTemperatureToSrgb(light.ColorTemperature.Value);
                    break;

                default:
                    light.Color = Vector3.Zero;
                    break;
            }

            // A luminaire is at least a unit across, and a barn light never narrows away from it
            var sizeParams = entity.GetVector3Property("size_params", new Vector3(16f, 16f, 0.0625f));
            light.SizeParams = new Vector3(MathF.Max(sizeParams.X, 1f), MathF.Max(sizeParams.Y, 1f), MathF.Max(sizeParams.Z, 0f));
            light.ShadowMapSize = entity.GetInt32Property("shadowmapsize", 1024);
        }
        else
        {
            ReadLegacyKeys(light, entity);
        }

        if (light.ShadowMapSize <= 0)
        {
            light.ShadowMapSize = 1024;
        }

        var defaultDirectLight = isLight2
            ? DirectLightType.Dynamic
            : DirectLightType.Static;

        light.DirectLight = (DirectLightType)entity.GetInt32Property("directlight", (int)defaultDirectLight);
        light.CastShadows = entity.GetInt32Property("castshadows", isLight2 ? 1 : 0);

        if (type is EntityType.Barn)
        {
            light.SoftX = entity.GetFloatProperty("soft_x", 0.25f);
            light.SoftY = entity.GetFloatProperty("soft_y", 0.25f);
            light.SkirtNear = entity.GetFloatProperty("skirt_near", 0.05f);
            light.Shape = entity.GetFloatProperty("shape");
            light.LuminaireSize = entity.GetFloatProperty("luminaire_size", 4f);
            light.LuminaireShape = entity.GetInt32Property("luminaire_shape");
            light.LuminaireAnisotropy = entity.GetFloatProperty("luminaire_anisotropy");
            light.CookieTexturePath = GetCookie(entity);
            light.Shear = entity.GetVector2Property("shear");
        }

        if (type is EntityType.Rect)
        {
            // Rasterized like an omni2 point light (no luminaire shape) lighting the hemisphere in front of it,
            // the rectangle or disc only matters to path tracing and to the shaders that shade area lights
            light.SpotOuterAngle = 90f;
            light.Shape = entity.GetFloatProperty("shape"); // 0 = rectangle, 1 = disc
        }

        if (type is EntityType.Omni2)
        {
            light.SpotOuterAngle = Math.Clamp(entity.GetFloatProperty("outer_angle", 180f), 1f, 180f);
            light.SpotInnerAngle = Math.Clamp(entity.GetFloatProperty("inner_angle", 180f), 0f, light.SpotOuterAngle);
            light.CookieTexturePath = GetCookie(entity);
            light.LuminaireShape = entity.GetInt32Property("shape");
            light.LuminaireSize = entity.GetFloatProperty("luminaire_size");
        }

        if (isLight2)
        {
            light.FadeSizeStart = entity.GetFloatProperty("fade_size_start", 0.05f);
            light.FadeSizeEnd = entity.GetFloatProperty("fade_size_end", 0.025f);
            light.ShadowFadeSizeStart = entity.GetFloatProperty("shadowfade_size_start", 0.1f);
            light.ShadowFadeSizeEnd = entity.GetFloatProperty("shadowfade_size_end", 0.05f);

            light.PrecomputedFieldsValid = entity.GetInt32Property("precomputedfieldsvalid") != 0;
            if (light.PrecomputedFieldsValid)
            {
                light.PrecomputedObbOrigin = entity.GetVector3Property("precomputedobborigin");
                light.PrecomputedObbExtent = entity.GetVector3Property("precomputedobbextent");
                light.PrecomputedObbAngles = entity.GetVector3Property("precomputedobbangles");
                var boundsMins = entity.GetVector3Property("precomputedboundsmins");
                var boundsMaxs = entity.GetVector3Property("precomputedboundsmaxs");
                light.PrecomputedBounds = new AABB(boundsMins, boundsMaxs);

                light.PrecomputedSubfrusta = entity.GetInt32Property("precomputedsubfrusta");
                if (light.PrecomputedSubfrusta > 0)
                {
                    light.PrecomputedSubObbOrigins = new Vector3[light.PrecomputedSubfrusta];
                    light.PrecomputedSubObbExtents = new Vector3[light.PrecomputedSubfrusta];
                    light.PrecomputedSubObbAngles = new Vector3[light.PrecomputedSubfrusta];

                    for (var i = 0; i < light.PrecomputedSubfrusta; i++)
                    {
                        light.PrecomputedSubObbOrigins[i] = entity.GetVector3Property($"precomputedobborigin{i}");
                        light.PrecomputedSubObbExtents[i] = entity.GetVector3Property($"precomputedobbextent{i}");
                        light.PrecomputedSubObbAngles[i] = entity.GetVector3Property($"precomputedobbangles{i}");
                    }
                }
            }
        }

        light.Style = LightStyle.FromEntity(entity, isLight2, scene.RendererContext.FileLoader);

        // The caller places the light with PlaceAt, the keyvalues are in the entity's local space
        return light;
    }

    private static string? GetCookie(Entity entity) => entity.GetStringProperty("lightcookie") is { Length: > 0 } cookie ? cookie : null;

    /// <summary>
    /// Reads the keys of an older light, which renders as barn faces where lights render in real time: a point
    /// light all around it, a spot light as one frustum and an orthographic light as a box.
    /// </summary>
    private static void ReadLegacyKeys(SceneLight light, Entity entity)
    {
        if (entity.GetInt32Property("baked_light_indexing", 1) == 0)
        {
            light.StationaryLightIndex = -1;
        }

        var shadowWidth = entity.GetInt32Property("shadowtexturewidth");
        var shadowHeight = entity.GetInt32Property("shadowtextureheight");

        if (shadowWidth > 0 || shadowHeight > 0)
        {
            light.ShadowMapSize = Math.Max(shadowWidth, shadowHeight);
        }

        switch (light.Entity)
        {
            case EntityType.Omni or EntityType.Capsule:
                ReadAttenuation(light, entity);

                // Lights all around it
                light.SpotInnerAngle = 180f;
                light.SpotOuterAngle = 180f;
                break;

            case EntityType.Spot:
                ReadAttenuation(light, entity);

                // One frustum as wide as the outer cone, soft from the inner cone out
                light.SpotInnerAngle = entity.GetFloatProperty("innerconeangle", light.SpotInnerAngle);
                light.SpotOuterAngle = entity.GetFloatProperty("outerconeangle", light.SpotOuterAngle);

                var tanOuter = MathF.Tan(float.DegreesToRadians(Math.Clamp(light.SpotOuterAngle, 0.01f, 89.9f)));
                var hardness = MathF.Tan(float.DegreesToRadians(light.SpotInnerAngle)) / tanOuter;

                light.SizeParams = new Vector3(tanOuter, tanOuter, 1f);
                light.SoftX = 1f - hardness;
                light.SoftY = 1f - hardness;
                light.Shape = 1f;
                light.CookieTexturePath = GetCookie(entity);
                break;

            case EntityType.Ortho:
                var width = entity.GetFloatProperty("ortholightwidth", 512.0f);
                var height = entity.GetFloatProperty("ortholightheight", 512.0f);

                light.SizeParams = new Vector3(width * 0.5f, height * 0.5f, 0f);
                light.FallOff = 0f;
                light.SoftX = 0f;
                light.SoftY = 0f;
                light.CookieTexturePath = GetCookie(entity);

                // As wide as the shadow texture width says, and as high as that keeps it in proportion
                light.ShadowMapSize = shadowWidth > 0 ? shadowWidth : 2048;
                break;
        }

        // Points and spots fade out over the last quarter of their range
        if (light.Entity is EntityType.Omni or EntityType.Capsule or EntityType.Spot)
        {
            light.FallOff = 0.25f;
        }
    }

    /// <summary>
    /// Reads how an older point or spot light attenuates. Coefficients too small to matter are none, a light
    /// without any is constant, and the brightness is the one the light has 100 units away.
    /// </summary>
    private static void ReadAttenuation(SceneLight light, Entity entity)
    {
        static float Coefficient(float value) => value < 0.001f ? 0f : value;

        var constant = Coefficient(entity.GetFloatProperty("attenuation0"));
        var linear = Coefficient(entity.GetFloatProperty("attenuation1"));
        var quadratic = Coefficient(entity.GetFloatProperty("attenuation2", 1f));

        if (constant == 0f && linear == 0f && quadratic == 0f)
        {
            constant = 1f;
        }

        light.AttenuationConstant = constant;
        light.AttenuationLinear = linear;
        light.AttenuationQuadratic = quadratic;

        // A light that keeps some brightness however far it goes ends at its range, any other where it falls
        // below what can be seen
        if (constant != 0f)
        {
            return;
        }

        var color = ColorSpace.SrgbGammaToLinear(light.Color) * (light.Brightness * light.BrightnessScale * light.ReferenceAttenuation);
        var luminance = Vector3.Dot(color, new Vector3(0.2126f, 0.7152f, 0.0722f));
        var visibleRange = 0f;

        if (luminance > 0f && SolveQuadratic(quadratic, linear, -luminance / VisibleLuminance) is { } distance)
        {
            visibleRange = MathF.Max(0f, distance);
        }

        light.VisibleRange = visibleRange;
    }

    /// <summary>The luminance below which an older light is too dim to see.</summary>
    private const float VisibleLuminance = 0.001f;

    // The larger root of a x^2 + b x + c, or none
    private static float? SolveQuadratic(float a, float b, float c)
    {
        if (a == 0f)
        {
            return b != 0f ? -c / b : c == 0f ? 0f : null;
        }

        var discriminant = b * b - 4f * a * c;

        if (discriminant < 0f)
        {
            return null;
        }

        var root = MathF.Sqrt(discriminant);

        return MathF.Max((root - b) / (2f * a), (-b - root) / (2f * a));
    }

    /// <summary>
    /// What the attenuation of an older point or spot light divides its brightness by 100 units away, which
    /// its color is multiplied by so that its brightness is the one it has there.
    /// </summary>
    private float ReferenceAttenuation => Entity is EntityType.Omni or EntityType.Capsule or EntityType.Spot
        ? AttenuationQuadratic * 10000f + AttenuationLinear * 100f + AttenuationConstant
        : 1f;

    /// <summary>
    /// Gets the linear intensity the faces of the light start from, before <see cref="BrightnessScale"/>.
    /// An older point or spot light falls off with the square of the distance as a barn face, matching its own
    /// attenuation at a fraction of its range.
    /// </summary>
    private float FaceBrightness
    {
        get
        {
            if (Entity is EntityType.Ortho)
            {
                return Brightness;
            }

            if (Entity is not (EntityType.Omni or EntityType.Capsule or EntityType.Spot))
            {
                return LinearBrightness;
            }

            var distance = Range * 0.15f;

            if (distance <= 0f)
            {
                distance = LinearBrightnessDistance;
            }

            var distanceSq = distance * distance;
            var attenuation = AttenuationQuadratic * distanceSq + AttenuationLinear * distance + AttenuationConstant;

            return Brightness * ReferenceAttenuation * distanceSq / attenuation;
        }
    }

    /// <inheritdoc/>
    public override void Update(Scene.UpdateContext context)
    {
        if (style == null || context.Phase != Scene.UpdatePhase.Place)
        {
            return;
        }

        styleTime += context.Timestep;
        StyleColorScale = style.Evaluate(styleTime - styleStartTime);

        // The lights kept in the lighting constants are stored with it again, barn faces take it as they are binned
        Scene.LightingInfo.MarkStoredLightsChanged();
    }

    /// <summary>
    /// What this light costs to render, which is <see cref="DirectLight"/> resolved against
    /// <see cref="StationaryLightIndex"/>.
    /// </summary>
    public LightCost Cost => GetCost(DirectLight, StationaryLightIndex);

    private static LightCost GetCost(DirectLightType directLight, int stationaryLightIndex)
    {
        if (directLight is DirectLightType.Stationary)
        {
            return LightCost.Stationary;
        }

        if (directLight is DirectLightType.None)
        {
            return LightCost.Static;
        }

        // A baked shadow index means the lightmap holds this light's shadows, which is what makes it
        // stationary rather than dynamic. Lights authored as stationary reach here as Static: Alyx-era
        // entities say directlight 1 (baked) plus baked_light_indexing, and pre-Alyx light entities
        // carry no directlight key at all so they reach here as Dynamic.
        if (stationaryLightIndex >= 0)
        {
            return LightCost.Stationary;
        }

        return directLight is DirectLightType.Static ? LightCost.Static : LightCost.Dynamic;
    }

    internal static bool IsRealTimeLight(SceneLight light)
    {
        // The sun has lighting constants of its own
        if (light.IsDirectional)
        {
            return false;
        }

        return light.DirectLight switch
        {
            DirectLightType.None => false,
            DirectLightType.Static => light.StationaryLightIndex is >= 0 and <= 3,
            _ => true
        };
    }

    /// <summary>
    /// Recomputes the <see cref="BarnFaces"/> array from the current light properties.
    /// </summary>
    /// <param name="cookiePaths">Map from cookie material path to cookie atlas index.</param>
    public void ComputeBarnFaces(Dictionary<string, int> cookiePaths)
    {
        // Face indices shift under the readbacks still in flight, so their countdowns describe nothing.
        faceVisibilityTtl = ulong.MaxValue;

        if (FaceRange <= 0.0001f)
        {
            BarnFaces = [];
            return;
        }

        if (Entity is EntityType.Barn or EntityType.Spot or EntityType.Ortho)
        {
            if (BarnFaces is not { Length: 1 })
            {
                BarnFaces = new BarnFaceData[1];
            }

            BarnFaces[0] = ComputeBarnLightFace(this, cookiePaths);
        }
        else if (UsesOmni2Faces)
        {
            ComputeOmni2Faces(this, cookiePaths);
        }
    }

    /// <summary>
    /// Returns the shadow map pixel dimensions for this light at the given long-axis resolution, accounting for aspect ratio.
    /// </summary>
    /// <param name="size">Target resolution in texels along the longer axis.</param>
    public (int W, int H) GetShadowFaceDimensions(int size)
    {
        if (UsesOmni2Faces)
        {
            return (size, size);
        }

        var aspect = SizeParams.X / SizeParams.Y;
        return aspect >= 1f
            ? (size, (int)MathF.Round(size / aspect))
            : ((int)MathF.Round(size * aspect), size);
    }

    /// <summary>
    /// The superellipse exponent a barn or rect light's shape value maps to, from a rectangle at zero to an ellipse at one.
    /// Any rounding at all starts at an eighth, and values past one stay an ellipse.
    /// </summary>
    internal static float GetShapeExponent(float shape) => shape > 0f ? MathF.Min(shape, 1f) * 0.875f + 0.125f : 0f;

    /// <summary>Distance, in world units, in front of the luminaire at which a light reaches its <see cref="LinearBrightness"/>.</summary>
    private const float LinearBrightnessDistance = 100f;

    // Mirrored by the BARN_LIGHT_FLAG_ constants in lighting.barn.slang
    private const uint RectLuminaireFlag = 0x1000u;
    private const uint DiscLuminaireFlag = 0x2000u;

    /// <summary>
    /// Linear intensity for the shader, which attenuates it by <paramref name="falloffDistSq"/> over the squared distance from
    /// the light position (zero for a light with no falloff). The luminaire is <paramref name="luminaireDistance"/> in front of that position.
    /// </summary>
    private static float ComputeIntensity(SceneLight light, float falloffDistSq, float luminaireDistance)
    {
        var intensity = light.FaceBrightness * light.BrightnessScale;

        // An older light is as bright as its own attenuation says, wherever its faces start from
        if (falloffDistSq <= 0f || !light.IsLight2)
        {
            return intensity;
        }

        var referenceDistance = luminaireDistance + LinearBrightnessDistance;
        return intensity * referenceDistance * referenceDistance / falloffDistSq;
    }

    private static (Matrix4x4 WorldToFrustum, Vector4 Position, float SkirtNear, float SkirtFar)
        ComputeOrthographicBarnGeometry(SceneLight light, Vector3 forwardDir, Vector3 upDir, Vector3 rightDir)
    {
        var eyePosition = light.Transform.Translation;

        var lightView = Matrix4x4.CreateLookAtLeftHanded(eyePosition,
            eyePosition + forwardDir, upDir);

        var shearMatrix = Matrix4x4.Identity;
        shearMatrix.M31 = -light.Shear.X / light.Range;
        shearMatrix.M32 = -light.Shear.Y / light.Range;

        var lightProj = Matrix4x4.CreateOrthographicOffCenterLeftHanded(
            -light.SizeParams.X, light.SizeParams.X,
            -light.SizeParams.Y, light.SizeParams.Y,
            0f, light.Range);

        var worldToFrustum = lightView * shearMatrix * lightProj;

        var skirtNear = light.SkirtNear > 0 ? 1f / light.SkirtNear : 0f;
        var skirtFar = light.FallOff > 0 ? 1f / light.FallOff : 0f;

        var projectionDir = Vector3.Normalize(
            light.Range * forwardDir + light.Shear.X * rightDir + light.Shear.Y * upDir);
        var position = new Vector4(-projectionDir, 0f);

        return (worldToFrustum, position, skirtNear, skirtFar);
    }

    private static (Matrix4x4 WorldToFrustum, Vector4 Position, float SkirtNear, float SkirtFar)
        ComputePerspectiveBarnGeometry(SceneLight light, Vector3 forwardDir, Vector3 upDir, Vector3 rightDir)
    {
        var nearPlane = 1f / light.SizeParams.Z;
        var farPlane = nearPlane + light.FaceRange;

        var centerX = light.Shear.X / light.FaceRange * nearPlane;
        var centerY = light.Shear.Y / light.FaceRange * nearPlane;

        // A spot light's frustum starts at the light, a barn light's behind its luminaire
        var eyeOffset = light.Entity == EntityType.Spot ? 0f : nearPlane;
        var eyePosition = light.Transform.Translation
                          - eyeOffset * forwardDir - centerX * rightDir - centerY * upDir;

        var lightProj = Matrix4x4.CreatePerspectiveOffCenterLeftHanded(
            centerX - light.SizeParams.X, centerX + light.SizeParams.X,
            centerY - light.SizeParams.Y, centerY + light.SizeParams.Y,
            nearPlane, farPlane);
        var lightView = Matrix4x4.CreateLookAtLeftHanded(eyePosition,
            eyePosition + forwardDir, upDir);

        var worldToFrustum = lightView * lightProj;

        var skirtNear = 0f;
        if (light.SkirtNear > 0)
        {
            var depthNear = nearPlane + light.SkirtNear * light.FaceRange;
            skirtNear = depthNear / (farPlane * light.SkirtNear);
        }

        var skirtFar = 0f;
        if (light.FallOff > 0)
        {
            var depthFar = nearPlane + (1f - light.FallOff) * light.FaceRange;
            skirtFar = depthFar / (nearPlane * light.FallOff);
        }

        var eyeToOriginDistSq = centerX * centerX + centerY * centerY + nearPlane * nearPlane;
        var position = new Vector4(eyePosition, eyeToOriginDistSq);

        return (worldToFrustum, position, skirtNear, skirtFar);
    }

    private static BarnFaceData ComputeBarnLightFace(SceneLight light, Dictionary<string, int> cookiePaths)
    {
        var noTranslation = light.Transform with { Translation = Vector3.Zero };
        var forwardDir = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, noTranslation));
        var upDir = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, noTranslation));
        var rightDir = Vector3.Normalize(Vector3.Cross(upDir, forwardDir));

        var (worldToFrustum, barnLightPosition, skirtNear, skirtFar) = light.SizeParams.Z <= 0
            ? ComputeOrthographicBarnGeometry(light, forwardDir, upDir, rightDir)
            : ComputePerspectiveBarnGeometry(light, forwardDir, upDir, rightDir);

        var colorIntensity = ComputeIntensity(light, barnLightPosition.W, MathF.Sqrt(barnLightPosition.W));
        var linearColor = ColorSpace.SrgbGammaToLinear(light.Color) * colorIntensity;

        var cookieW = 0f;
        Vector4 cookieParams;
        if (light.CookieTexturePath != null && cookiePaths.TryGetValue(light.CookieTexturePath, out var cookieIndex))
        {
            cookieW = cookieIndex + 1f;
            cookieParams = new Vector4(0f, 0f, 1f, 1f);
        }
        else
        {
            cookieParams = new Vector4(1f - light.SoftX, 1f - light.SoftY, GetShapeExponent(light.Shape), 0f);
        }

        var orientationQ = Quaternion.CreateFromRotationMatrix(light.Transform);

        var (illuminationFromWorld, obbToWorld) = GetLightObb(light);

        return new BarnFaceData
        {
            GpuData = new BarnLightConstants
            {
                BarnFrustum = worldToFrustum,
                BarnLightPosition = barnLightPosition,
                BarnLightDistanceFade_vSkirt = new Vector4(1f, 0f, skirtNear, skirtFar),
                BarnLightColor_flCookie = new Vector4(linearColor, cookieW),
                BarnLightOrientationQ = new Vector4(orientationQ.X, orientationQ.Y, orientationQ.Z, orientationQ.W),
                BarnLightAngleFade = new Vector3(1f, 0f, 0f),
                BarnLightShadowOffsetScale = Vector4.Zero,
                BarnLightCookieParameters = cookieParams,
                BarnLightBakedShadowMask = light.BakedShadowMask,
                BarnLightMinRoughness = MathF.Max(MinSpecularRoughness, light.MinRoughness),
                BarnLightShadowScale = 0f,
                PathTraceIndex_BarnLightFlags = 0xFFFF0000u,
                BarnIlluminationFromWorld = illuminationFromWorld
            },
            WorldToFrustum = worldToFrustum,
            FrustumToWorld = InvertFrustum(worldToFrustum),
            ObbToWorld = obbToWorld,
        };
    }

    private static Matrix4x4 InvertFrustum(Matrix4x4 worldToFrustum)
    {
        return Matrix4x4.Invert(worldToFrustum, out var frustumToWorld) ? frustumToWorld : Matrix4x4.Identity;
    }

    private static void ComputeOmni2Faces(SceneLight light, Dictionary<string, int> cookiePaths)
    {
        var noTranslation = light.Transform with { Translation = Vector3.Zero };
        var forward = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, noTranslation));
        var right = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, noTranslation));
        var up = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, noTranslation));

        var angleRad = float.DegreesToRadians(Math.Clamp(light.SpotOuterAngle, 1f, 180f));
        var heightOffset = light.LuminaireShape is 1 or 2 ? light.SizeParams.Y : 0f;
        var sideRange = angleRad < MathF.PI / 2f
            ? MathF.Max(light.FaceRange - heightOffset, 0f) * MathF.Sin(angleRad) + heightOffset
            : light.FaceRange;
        // Only a light with shadows is split into faces to render them from, without shadows a single
        // box around everything the light can reach is enough
        var singleBox = light.CastShadows != 1;
        var faceCount = singleBox ? 1 : GetOmni2FaceCount(angleRad, heightOffset > 0f);

        if (light.BarnFaces is not { Length: var currentLen } || currentLen != faceCount)
        {
            light.BarnFaces = new BarnFaceData[faceCount];
        }

        var origin = light.Transform.Translation;
        var nearPlane = 1f;
        var orientationQ = Quaternion.CreateFromRotationMatrix(light.Transform);
        var linearColor = light.ComputeOmni2Color();

        // custom point light hack
        const uint LitInsideNearPlaneFlag = 0x8000u;
        const float ApexCullNearPlane = 0.001f;
        var faceFlags = light.LuminaireShape == -1 ? 0xFFFF0000u | LitInsideNearPlaneFlag : 0xFFFF0000u;

        // For the shaders that shade a rect light as the rectangle or disc it is
        var luminaireHalfSize = Vector2.Zero;
        var luminaireFlags = 0u;
        if (light.Entity == EntityType.Rect)
        {
            luminaireHalfSize = new Vector2(light.SizeParams.X, light.SizeParams.Y);
            luminaireFlags = light.Shape == 1f ? DiscLuminaireFlag : RectLuminaireFlag;
        }

        var cookieW = 0f;
        var cookieParams = new Vector4(1f, 1f, 0f, 0f);
        if (light.CookieTexturePath != null && cookiePaths.TryGetValue(light.CookieTexturePath, out var cookieIndex))
        {
            cookieW = -(cookieIndex + 1f);
            cookieParams = new Vector4(0f, 0f, 1f, 1f);
        }

        var (distFadeOffset, distFadeRate) = ComputeDistanceFade(light.FaceRange, light.FallOff);
        var (angleBias, angleScale) = ComputeAngleFade(light.SpotOuterAngle, light.SpotInnerAngle);
        var angleFadeZ = light.LuminaireShape switch
        {
            1 or 2 => light.SizeParams.Y,
            0 => light.LuminaireSize,
            _ => 0f
        };

        BarnFaceData CreateFace(Matrix4x4 worldToFrustum, Matrix4x4 worldToCullFrustum,
            OpenTK.Mathematics.Matrix3x4 illuminationFromWorld, Matrix4x4 obbToWorld, uint flags)
        {
            return new BarnFaceData
            {
                GpuData = new BarnLightConstants
                {
                    BarnFrustum = worldToFrustum,
                    BarnLightPosition = new Vector4(origin, 1f),
                    BarnLightDistanceFade_vSkirt = new Vector4(distFadeOffset, distFadeRate, 0f, 0f),
                    BarnLightColor_flCookie = new Vector4(linearColor, cookieW),
                    BarnLightOrientationQ = new Vector4(orientationQ.X, orientationQ.Y, orientationQ.Z, orientationQ.W),
                    BarnLightAngleFade = new Vector3(angleBias, angleScale, angleFadeZ),
                    BarnLightLuminaireHalfWidth = luminaireHalfSize.X,
                    BarnLightShadowOffsetScale = Vector4.Zero,
                    BarnLightCookieParameters = cookieParams,
                    BarnLightBakedShadowMask = light.BakedShadowMask,
                    BarnLightMinRoughness = MathF.Max(MinSpecularRoughness, light.MinRoughness),
                    BarnLightShadowScale = 0f,
                    PathTraceIndex_BarnLightFlags = flags | luminaireFlags,
                    BarnLightLuminaireHalfHeight = luminaireHalfSize.Y,
                    BarnIlluminationFromWorld = illuminationFromWorld,
                },
                WorldToFrustum = worldToFrustum,
                FrustumToWorld = InvertFrustum(worldToCullFrustum),
                ObbToWorld = obbToWorld,
            };
        }

        if (singleBox)
        {
            // Reaches behind the light when the cone opens past a hemisphere
            var back = angleRad > MathF.PI / 2f ? -MathF.Cos(angleRad) * light.FaceRange : 0f;
            var boxView = Matrix4x4.CreateLookAtLeftHanded(origin, origin + forward, up);
            var boxProj = Matrix4x4.CreateOrthographicOffCenterLeftHanded(-sideRange, sideRange, -sideRange, sideRange, -back, light.FaceRange);
            var (boxIllumination, boxObbToWorld) = GetLightObb(light);

            light.BarnFaces[0] = CreateFace(boxView * boxProj, boxView * boxProj, boxIllumination, boxObbToWorld, 0xFFFF0000u);
            return;
        }

        BarnFaceData MakeFace(Vector3 faceForward, Vector3 faceUp, float range, int faceIndex)
        {
            var lightView = Matrix4x4.CreateLookAtLeftHanded(origin, origin + faceForward, faceUp);
            var lightProj = Matrix4x4.CreatePerspectiveLeftHanded(2f * nearPlane, 2f * nearPlane, nearPlane, nearPlane + range);
            var cullProj = (faceFlags & LitInsideNearPlaneFlag) != 0u
                ? Matrix4x4.CreatePerspectiveLeftHanded(2f * ApexCullNearPlane, 2f * ApexCullNearPlane, ApexCullNearPlane, nearPlane + range)
                : lightProj;
            var (illuminationFromWorld, obbToWorld) = GetOmni2FaceOBB(light, faceIndex);

            return CreateFace(lightView * lightProj, lightView * cullProj, illuminationFromWorld, obbToWorld, faceFlags);
        }

        light.BarnFaces[0] = MakeFace(forward, up, light.FaceRange, 0);

        if (faceCount >= 5)
        {
            light.BarnFaces[1] = MakeFace(-up, forward, sideRange, 1);
            light.BarnFaces[2] = MakeFace(up, -forward, sideRange, 2);
            light.BarnFaces[3] = MakeFace(right, up, sideRange, 3);
            light.BarnFaces[4] = MakeFace(-right, up, sideRange, 4);
        }

        if (faceCount >= 6)
        {
            light.BarnFaces[5] = MakeFace(-forward, up, MathF.Abs(MathF.Cos(angleRad)) * light.FaceRange, 5);
        }
    }

    /// <summary>
    /// Calculates the effective solid angle of a spotlight cone, clamped to a maximum of 4pi steradians (full sphere).
    /// </summary>
    public float ComputeConeSolidAngle()
    {
        var outerRad = float.DegreesToRadians(Math.Clamp(SpotOuterAngle, 1f, 180f));
        var innerRad = float.DegreesToRadians(Math.Clamp(SpotInnerAngle, 0f, SpotOuterAngle));

        var avgCos = (MathF.Cos(outerRad) + MathF.Cos(innerRad)) * 0.5f;
        return MathF.Min(MathF.Tau * (1f - avgCos), 4f * MathF.PI);
    }

    /// <summary>
    /// How much of the light, and of its shadows, is left after fading out with the fraction of the screen its bounds cover.
    /// </summary>
    /// <param name="cameraPosition">Position of the camera the light is seen from.</param>
    /// <param name="tanHalfHorizontalFov">Tangent of half the camera's horizontal field of view.</param>
    internal (float Light, float Shadow) ComputeScreenSizeFades(Vector3 cameraPosition, float tanHalfHorizontalFov)
    {
        var bounds = PrecomputedFieldsValid ? PrecomputedBounds : new AABB(Position, Range);
        var diagonal = Vector3.Distance(bounds.Min, bounds.Max);
        var distanceSq = Vector3.DistanceSquared(cameraPosition, bounds.Center);

        // Covers the whole screen with the camera close enough to be inside the bounds
        var screenSize = diagonal * diagonal > distanceSq
            ? 1f
            : MathUtils.Saturate(diagonal / (MathF.Sqrt(distanceSq) * tanHalfHorizontalFov));

        float Fade(float start, float end) => 1f - MathUtils.Saturate((start - screenSize) / MathF.Max(1e-4f, start - end));

        return (Fade(FadeSizeStart, FadeSizeEnd), Fade(ShadowFadeSizeStart, ShadowFadeSizeEnd));
    }

    /// <summary>Linear color an omni2 or rect light is lit with, which is also the radiance its visible geometry starts from.</summary>
    internal Vector3 ComputeOmni2Color()
        => ColorSpace.SrgbGammaToLinear(Color) * ComputeIntensity(this, 1f, 0f);

    private static (float Offset, float Rate) ComputeDistanceFade(float range, float skirt)
    {
        if (skirt <= 0f)
        {
            return (1f, 0f);
        }

        if (skirt >= 1f)
        {
            return (1f, -1f / range);
        }

        return (1f / skirt, -1f / (range * skirt));
    }

    private static (float Bias, float Scale) ComputeAngleFade(float outerAngleDeg, float innerAngleDeg)
    {
        if (outerAngleDeg >= 180f)
        {
            return (1f, 0f);
        }

        if (outerAngleDeg <= 0f)
        {
            return (0f, 0f);
        }

        var cosOuter = MathF.Cos(float.DegreesToRadians(outerAngleDeg));
        var cosInner = innerAngleDeg > 0f
            ? MathF.Cos(float.DegreesToRadians(Math.Clamp(innerAngleDeg, 0f, outerAngleDeg - 0.001f)))
            : 1f;

        cosOuter = MathF.Min(cosOuter, 1f - 1e-7f);

        var denom = cosOuter - cosInner;
        if (denom >= 0f)
        {
            denom = -1e-7f;
        }

        return (cosOuter / denom, 1f / denom);
    }

    /// <summary>
    /// Returns the baked shadow mask for this light based on its stationary light index.
    /// </summary>
    public Vector4 BakedShadowMask => StationaryLightIndex switch
    {
        0 => new Vector4(1, 0, 0, 0),
        1 => new Vector4(0, 1, 0, 0),
        2 => new Vector4(0, 0, 1, 0),
        3 => new Vector4(0, 0, 0, 1),
        _ => Vector4.Zero
    };

    /// <summary>
    /// Both matrices of an illumination OBB: the one the shader rejects fragments with, and its inverse,
    /// which recovers the box in world space. Returned together because the cull pass has to bin exactly
    /// the box the shader lights inside of, and the shrink factor makes that easy to get wrong.
    /// </summary>
    private static (OpenTK.Mathematics.Matrix3x4 IlluminationFromWorld, Matrix4x4 ObbToWorld)
        ComputeObbMatrices(Vector3 center, Vector3 extent, Vector3 angles)
    {
        var rotMatrix = EntityTransformHelper.EulerAnglesToRotationMatrix(angles);

        var axis0 = rotMatrix.GetRow(0).AsVector3();
        var axis1 = rotMatrix.GetRow(1).AsVector3();
        var axis2 = rotMatrix.GetRow(2).AsVector3();

        const float Shrink = 0.999f;

        var inv = new Vector3(Shrink) / extent;

        // Built before the axes are scaled, from the half extent the shrink actually leaves. This is the
        // box the shader lights inside of, so it is the one the cull pass has to bin.
        var halfExtent = extent / Shrink;

        var obbToWorld = new Matrix4x4(
            axis0.X * halfExtent.X, axis0.Y * halfExtent.X, axis0.Z * halfExtent.X, 0f,
            axis1.X * halfExtent.Y, axis1.Y * halfExtent.Y, axis1.Z * halfExtent.Y, 0f,
            axis2.X * halfExtent.Z, axis2.Y * halfExtent.Z, axis2.Z * halfExtent.Z, 0f,
            center.X, center.Y, center.Z, 1f);

        var translation = new Vector3(
            -Vector3.Dot(center, axis0),
            -Vector3.Dot(center, axis1),
            -Vector3.Dot(center, axis2)
        ) * inv;

        axis0 *= inv.X;
        axis1 *= inv.Y;
        axis2 *= inv.Z;

        var illuminationFromWorld = new OpenTK.Mathematics.Matrix3x4(
            axis0.X, axis0.Y, axis0.Z, translation.X,
            axis1.X, axis1.Y, axis1.Z, translation.Y,
            axis2.X, axis2.Y, axis2.Z, translation.Z
        );

        return (illuminationFromWorld, obbToWorld);
    }

    private static (OpenTK.Mathematics.Matrix3x4 IlluminationFromWorld, Matrix4x4 ObbToWorld)
        GetOmni2FaceOBB(SceneLight light, int faceIndex)
    {
        if (light.PrecomputedSubObbOrigins != null
            && faceIndex < light.PrecomputedSubObbOrigins.Length)
        {
            return ComputeObbMatrices(
                light.PrecomputedSubObbOrigins[faceIndex],
                light.PrecomputedSubObbExtents![faceIndex],
                light.PrecomputedSubObbAngles![faceIndex]);
        }

        return default;
    }

    private static (OpenTK.Mathematics.Matrix3x4 IlluminationFromWorld, Matrix4x4 ObbToWorld) GetLightObb(SceneLight light)
        => ShouldEnableOBB(light)
            ? ComputeObbMatrices(light.PrecomputedObbOrigin, light.PrecomputedObbExtent, light.PrecomputedObbAngles)
            : default;

    private static bool ShouldEnableOBB(SceneLight light)
    {
        if (!light.PrecomputedFieldsValid)
        {
            return false;
        }

        return light.StationaryLightIndex is >= 0 and <= 3
               || light.CastShadows > 0;
    }

    private static int GetOmni2FaceCount(float angleRad, bool hasHeightOffset)
    {
        if (hasHeightOffset)
        {
            angleRad = MathF.Max(MathF.PI / 2f, angleRad);
        }

        return angleRad switch
        {
            <= MathF.PI / 4f => 1,
            <= 3f * MathF.PI / 4f => 5,
            _ => 6
        };
    }

    #region Gpu face visibility

    // Per face verdicts from LightBinner's cull mask readback, kept here because the light reference is
    // the only identity that survives between frames: the binned slot order is rebuilt every one.

    /// <summary>Disagreeing readbacks before a face loses its shadow. One agreeing readback restores it.</summary>
    private const int GpuVisibilityTtl = 4;

    /// <summary>Faces the countdowns pack into, one byte each. Above the 6 an omni light has.</summary>
    private const int GpuVisibilityFaces = 8;

    /// <summary>Starts saturated, so a face nothing has measured is lit.</summary>
    private ulong faceVisibilityTtl = ulong.MaxValue;

    /// <summary>Gets the readback sequence <see cref="faceVisibilityTtl"/> was last stamped with.</summary>
    internal int GpuVisibilitySequence { get; private set; }

    /// <summary>Folds one readback's verdict into the per face countdowns.</summary>
    /// <param name="visibleFaces">Bit per face the readback found reaching a visible tile.</param>
    /// <param name="sequence">Sequence number of that readback.</param>
    internal void ApplyGpuFaceVisibility(uint visibleFaces, int sequence)
    {
        var next = 0ul;

        for (var face = 0; face < GpuVisibilityFaces; face++)
        {
            var shift = face * 8;
            var ttl = (int)((faceVisibilityTtl >> shift) & 0xFFul);

            ttl = (visibleFaces & (1u << face)) != 0u ? GpuVisibilityTtl : Math.Max(ttl - 1, 0);

            next |= (ulong)ttl << shift;
        }

        faceVisibilityTtl = next;
        GpuVisibilitySequence = sequence;
    }

    /// <summary>Whether a face still wants a shadow map. A sequence this light was not stamped for is no
    /// verdict at all, which reads as lit.</summary>
    /// <param name="faceIndex">Face to test.</param>
    /// <param name="sequence">Sequence number of the readback the caller is asking about.</param>
    internal bool IsFaceGpuVisible(int faceIndex, int sequence)
        => GpuVisibilitySequence != sequence
            || faceIndex >= GpuVisibilityFaces
            || ((faceVisibilityTtl >> (faceIndex * 8)) & 0xFFul) != 0ul;

    #endregion
}
