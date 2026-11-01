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

    /// <summary>Gets or sets how much of the sky colour <see cref="SkyAmbientBounce"/> mixes in, from its alpha.</summary>
    public float SkyAmbientBounceAlpha { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets whether the sky of an environment light lights only the upper hemisphere,
    /// or <see langword="null"/> if the light has no such setting.
    /// </summary>
    public bool? LowerHemisphereIsBlack { get; set; }

    /// <summary>
    /// Gets or sets the sRGB color the ambient lighting has facing down, for an environment light that fades from
    /// <see cref="SkyColor"/> to it over the hemisphere instead of bouncing the sun and sky back up.
    /// </summary>
    public Vector3? GroundColor { get; set; }

    /// <summary>Gets or sets the sRGB color that lights surfaces facing <see cref="AmbientDirection"/>, for an environment light.</summary>
    public Vector3 AmbientDirectionColor { get; set; }

    /// <summary>Gets or sets the intensity of <see cref="AmbientDirectionColor"/>, zero when the light has no directional ambient.</summary>
    public float AmbientDirectionIntensity { get; set; }

    /// <summary>Gets or sets the direction the directional ambient lighting is facing.</summary>
    public Vector3 AmbientDirection { get; set; }

    /// <summary>Gets or sets how much the sun's shadow also darkens the directional ambient lighting, from none (0) to fully (1).</summary>
    public float AmbientShadowAmount { get; set; }

    /// <summary>Gets or sets the direction the specular light of an environment light travels, which places its highlight.</summary>
    public Vector3 SpecularDirection { get; set; }

    /// <summary>Gets or sets the sRGB color of the specular light of an environment light, black when it has none.</summary>
    public Vector3 SpecularColor { get; set; }

    /// <summary>Gets or sets the exponent that sharpens the highlight of <see cref="SpecularColor"/>.</summary>
    public float SpecularPower { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets how much of the highlight of <see cref="SpecularColor"/> is added after lighting where the sun reaches,
    /// from none (0), where all of it is lit with the albedo, to all (1).
    /// </summary>
    public float SpecularIndependence { get; set; }

    /// <summary>Gets or sets the additional brightness scale multiplier.</summary>
    public float BrightnessScale { get; set; } = 1.0f;

    /// <summary>Gets or sets the brightness multiplier of the current light style.</summary>
    public float StyleBrightness { get; set; } = 1.0f;

    /// <summary>Gets or sets the maximum range of the light in world units.</summary>
    public float Range { get; set; } = 512.0f;

    /// <summary>Gets or sets the distance falloff skirt fraction.</summary>
    public float FallOff { get; set; } = 1.0f;

    /// <summary>Gets or sets the inner cone half-angle for spot lights in degrees.</summary>
    public float SpotInnerAngle { get; set; }

    /// <summary>Gets or sets the outer cone half-angle for spot lights in degrees.</summary>
    public float SpotOuterAngle { get; set; } = 45.0f;

    /// <summary>
    /// Gets or sets whether an omni2 light skips fading toward the edge of its cone, which the outer
    /// angle still limits.
    /// </summary>
    internal bool NoAngleFade { get; set; }

    /// <summary>Gets or sets the constant attenuation coefficient.</summary>
    public float AttenuationConstant { get; set; } = 1f;

    /// <summary>Gets or sets the linear attenuation coefficient.</summary>
    public float AttenuationLinear { get; set; }

    /// <summary>Gets or sets the quadratic attenuation coefficient.</summary>
    public float AttenuationQuadratic { get; set; }

    /// <summary>
    /// Gets the factor a point or spot light's color is brightened by, so that its brightness is the one
    /// 100 units from the light: <c>q*10000 + l*100 + c</c>.
    /// </summary>
    public float AttenuationColorScale => Type is LightType.Point or LightType.Spot
        ? (AttenuationQuadratic * 10000f) + (AttenuationLinear * 100f) + AttenuationConstant
        : 1f;

    /// <summary>
    /// Gets or sets the attenuation that a baked light strength of 1 stands for, or <see langword="null"/>
    /// when the lightmap stores the attenuation itself.
    /// </summary>
    public float? BakedLightIndexScale { get; set; }

    /// <summary>
    /// Gets the factor that turns this light's baked strengths into its intensity. The strengths hold the
    /// attenuation divided by <see cref="BakedLightIndexScale"/>, and the brightness is stated 100 units
    /// from the light, which the attenuation terms brighten by <c>l*100 + q*10000</c>.
    /// </summary>
    public float BakedStrengthScale
    {
        get
        {
            if (BakedLightIndexScale is not { } indexScale)
            {
                return 1f;
            }

            var referenceScale = (AttenuationLinear * 100f) + (AttenuationQuadratic * 10000f);

            // Without attenuation terms the strength is the attenuation itself
            return referenceScale > 0f ? indexScale * referenceScale : 1f;
        }
    }

    /// <summary>Gets or sets the horizontal soft edge width for barn lights.</summary>
    public float SoftX { get; set; } = 0.25f;

    /// <summary>Gets or sets the vertical soft edge width for barn lights.</summary>
    public float SoftY { get; set; } = 0.25f;

    /// <summary>Gets or sets the near skirt distance for perspective barn lights.</summary>
    public float SkirtNear { get; set; }

    /// <summary>
    /// Gets or sets the fraction of <see cref="Range"/> over which a perspective barn light fades out with
    /// distance from its frustum's apex, or null for no such fade.
    /// </summary>
    internal float? BarnDistanceSkirt { get; set; }

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

    /// <summary>Gets or sets the name a spot light asks its cookie by, to be looked up in the game's light cookie table, or <see langword="null"/> if none.</summary>
    public string? CookieName { get; set; }

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

    /// <summary>Gets or sets how this light lights volumetric fog: 0 none, 1 baked, 2 dynamic, 3 baked without shadows, 4 dynamic without shadows.</summary>
    public int FogLighting { get; set; }

    /// <summary>Gets or sets how strongly this light lights volumetric fog.</summary>
    public float FogContributionStrength { get; set; } = 1f;

    /// <summary>
    /// Gets or sets whether a barn, rect or omni2 light lights volumetric fog, as set by the <c>fog</c> key.
    /// These lights do not use <see cref="FogLighting"/>.
    /// </summary>
    public bool BarnFog { get; set; }

    /// <summary>
    /// Gets or sets how the fog is shadowed from a light that has <see cref="BarnFog"/>, as set by the <c>fogshadows</c> key:
    /// negative follows <see cref="CastShadows"/>, 0 is unshadowed.
    /// </summary>
    public int BarnFogShadowMode { get; set; } = -1;

    /// <summary>Gets or sets the base 2 logarithm of how much brighter the fog sees a barn, rect or omni2 light, as set by the <c>fogstrength</c> key.</summary>
    public float BarnFogStrength { get; set; }

    /// <summary>Gets or sets how much brighter the fog sees a barn, rect or omni2 light, as set by the <c>fogscale</c> key.</summary>
    public float BarnFogScale { get; set; } = 1f;

    /// <summary>Gets the factor the fog multiplies the color of a barn, rect or omni2 light by.</summary>
    public float BarnFogIntensity => float.Exp2(BarnFogStrength) * BarnFogScale;

    /// <summary>Gets whether the fog is shadowed from a light that has <see cref="BarnFog"/>.</summary>
    public bool BarnFogHasShadows => BarnFogShadowMode < 0 ? CastShadows != 0 : BarnFogShadowMode != 0;

    /// <summary>
    /// Gets or sets the range the light was precomputed to, as set by the <c>precomputedmaxrange</c> key,
    /// or zero if the key is not set.
    /// </summary>
    public float PrecomputedMaxRange { get; set; }

    /// <summary>
    /// Gets the distance up to which the light lights volumetric fog: <see cref="PrecomputedMaxRange"/> when set,
    /// otherwise the range the light shades to.
    /// </summary>
    public float FogRange => PrecomputedMaxRange > 0f ? PrecomputedMaxRange : GetAttenuatedRange();

    /// <summary>Gets or sets whether this light casts shadows.</summary>
    public int CastShadows { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether the stationary light also casts a real-time shadow over its baked one,
    /// as set by the <c>mixedshadows</c> key.
    /// </summary>
    public bool MixedShadows { get; set; }

    /// <summary>
    /// Gets or sets the distance from which the light starts to fade out, as set by the <c>fademindist</c> key.
    /// A negative value is an offset from <see cref="FadeMaxDist"/>.
    /// </summary>
    public float FadeMinDist { get; set; }

    /// <summary>
    /// Gets or sets the distance at which the light is fully faded out, as set by the <c>fademaxdist</c> key.
    /// Zero or less disables the fade.
    /// </summary>
    public float FadeMaxDist { get; set; }

    /// <summary>Gets or sets the priority of this light among the lights of the VR light binner, as set by the <c>priority</c> key.</summary>
    public int Priority { get; set; }

    /// <summary>
    /// Gets how visible this light is from a position: 1 when it is closer than the fade start, 0 when it is
    /// beyond the fade end. The distance is measured from the position to the box around the light's range.
    /// </summary>
    /// <param name="viewPosition">The position of the viewer.</param>
    internal float GetDistanceFade(Vector3 viewPosition)
    {
        if (!FadesWithDistance)
        {
            return 1f;
        }

        var bounds = new AABB(Position - new Vector3(Range), Position + new Vector3(Range));

        return GetFadeToBounds(bounds, viewPosition);
    }

    // Lights baked into the lightmaps never fade, nor do the ones without a fade distance
    private bool FadesWithDistance => DirectLight != DirectLightType.Static && FadeMaxDist > 0f;

    /// <summary>
    /// Gets how visible this light is to the fog from a position. Like <see cref="GetDistanceFade(Vector3)"/> it never
    /// fades lights baked into the lightmaps, but it measures the distance to a given box.
    /// </summary>
    /// <param name="bounds">The box around the volume the light reaches.</param>
    /// <param name="viewPosition">The position of the viewer.</param>
    internal float GetFogDistanceFade(in AABB bounds, Vector3 viewPosition)
    {
        if (!FadesWithDistance)
        {
            return 1f;
        }

        return GetFadeToBounds(bounds, viewPosition);
    }

    private float GetFadeToBounds(in AABB bounds, Vector3 viewPosition)
    {
        var fadeStart = GetFadeStart();
        var fadeStartSquared = fadeStart >= MaxFadeDistance ? float.MaxValue : fadeStart * fadeStart;
        var fadeEndSquared = FadeMaxDist >= MaxFadeDistance ? float.MaxValue : FadeMaxDist * FadeMaxDist;

        var distanceSquared = bounds.DistanceSquared(viewPosition);

        if (distanceSquared > fadeEndSquared)
        {
            return 0f;
        }

        if (fadeStartSquared == fadeEndSquared)
        {
            return distanceSquared < fadeStartSquared ? 0f : 1f;
        }

        var fade = (distanceSquared - fadeEndSquared) / (fadeStartSquared - fadeEndSquared);

        return Math.Clamp(fade, 0f, 1f);
    }

    // Fade distances this large mean the light does not fade
    private const float MaxFadeDistance = 1e10f;

    private float GetFadeStart()
    {
        if (FadeMinDist >= 0f)
        {
            return FadeMinDist;
        }

        return MathF.Max(FadeMinDist + FadeMaxDist, 0f);
    }

    /// <summary>
    /// Gets whether the VR light binner gives this stationary light a real-time shadow map: a spot light
    /// that casts shadows, bakes a light index and has mixed shadows.
    /// </summary>
    internal bool HasVrMixedLocalShadow
        => MixedShadows && CastShadows == 1 && StationaryLightIndex != -1 && Entity == EntityType.Spot;

    /// <summary>
    /// Gets whether the VR light binner shades this light as a cheap diffuse-only point light: a point light with
    /// no shadows, no specular and no quadratic attenuation.
    /// </summary>
    internal bool IsFastPathFxLight
        => Entity == EntityType.Omni && CastShadows == 0 && !RenderSpecular && AttenuationQuadratic <= 0f;

    /// <summary>
    /// Gets whether the VR light binner can pick this light as its one fully shaded light: a spot or ortho
    /// light with shadows.
    /// </summary>
    internal bool IsFastPathSingleLightCandidate
        => CastShadows == 1 && Entity is EntityType.Spot or EntityType.Ortho;

    /// <summary>
    /// Gets how much the VR light binner favors this light when seen from a position: the nearer, the more.
    /// An ortho light always outranks the others, and the distance fade scales the importance down.
    /// </summary>
    /// <param name="viewPosition">The position of the viewer.</param>
    /// <param name="distanceFade">How visible the light is from the position, see <see cref="GetDistanceFade"/>.</param>
    internal float GetFastPathImportance(Vector3 viewPosition, float distanceFade)
    {
        if (Entity == EntityType.Ortho)
        {
            return distanceFade;
        }

        var distance = MathF.Max(Vector3.Distance(Position, viewPosition), 1f);

        var importance = Type == LightType.Point
            ? 0.5f / (distance * distance)
            : 0.5f / distance;

        return importance * distanceFade;
    }

    /// <summary>Gets or sets the shadow map resolution in texels.</summary>
    public int ShadowMapSize { get; set; } = 1024;

    /// <summary>
    /// Gets or sets the last sun cascade that static objects render into when the shadows are mixed,
    /// -1 for none.
    /// </summary>
    public int CascadeRenderStaticObjects { get; set; } = -1;

    /// <summary>
    /// Gets whether the baked shadows of this light are mixed with dynamic shadow maps, which only
    /// static objects without a baked shadow need to cast into.
    /// </summary>
    public bool HasMixedShadows => CastShadows == 1 && StationaryLightIndex != -1;

    /// <summary>Gets or sets whether the precomputed OBB fields below are valid.</summary>
    public bool PrecomputedFieldsValid { get; set; }

    /// <summary>Gets or sets the center of the precomputed oriented bounding box.</summary>
    public Vector3 PrecomputedObbOrigin { get; set; }

    /// <summary>Gets or sets the half-extents of the precomputed oriented bounding box.</summary>
    public Vector3 PrecomputedObbExtent { get; set; }

    /// <summary>Gets or sets the Euler angles of the precomputed oriented bounding box.</summary>
    public Vector3 PrecomputedObbAngles { get; set; }

    /// <summary>Gets or sets the axis-aligned precomputed world bounds.</summary>
    public AABB PrecomputedBounds
    {
        get;
        set
        {
            field = value;
            PrecomputedBoundsDiagonal = Vector3.Distance(value.Min, value.Max);
        }
    }

    private float PrecomputedBoundsDiagonal;

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
    public bool IsVisible => BarnFaces.Length > 0 && BrightnessScale > 0f && Color != Vector3.Zero && LinearBrightness > 0f;

    /// <summary>
    /// Returns whether this is a barn, rect or omni2 light, which takes its brightness as an exposure value and
    /// is lit through <see cref="BarnFaces"/> when it renders in real time.
    /// </summary>
    public bool IsLight2 => Entity is EntityType.Omni2 or EntityType.Barn or EntityType.Rect;

    /// <summary>Whether the faces are built around the light like an omni2 light, rather than as one barn frustum.</summary>
    internal bool UsesOmni2Faces => Entity is EntityType.Omni2 or EntityType.Rect;

    /// <summary>
    /// Returns whether the given entity classname is a recognized light type, and which <see cref="EntityType"/> it maps to.
    /// </summary>
    public static (bool Accepted, EntityType Type) IsAccepted(string classname)
    {
        if (!classname.StartsWith("light_", StringComparison.OrdinalIgnoreCase))
        {
            return (false, EntityType.Environment);
        }

        var accepted = Enum.TryParse(classname[6..], true, out EntityType entityType);
        return (accepted, entityType);
    }

    /// <summary>
    /// Creates a <see cref="SceneLight"/> populated from entity key-value properties.
    /// </summary>
    /// <param name="scene">The scene the light belongs to.</param>
    /// <param name="type">The light entity class.</param>
    /// <param name="entity">The entity key-values.</param>
    /// <param name="name">The name to give the light node, or <see langword="null"/> for none.</param>
    public static SceneLight FromEntityProperties(Scene scene, EntityType type, Entity entity, string? name = null)
    {
        var light = new SceneLight(scene)
        {
            Name = name,
            StationaryLightIndex =
                entity.GetInt32Property("bakedshadowindex", entity.GetInt32Property("bakelightindex", -1)),
            Entity = type,
            Type = type switch
            {
                EntityType.Environment => LightType.Directional,
                EntityType.Ortho => LightType.Directional,

                EntityType.Omni => LightType.Point,
                EntityType.Omni2 => LightType.Point,

                EntityType.Barn => LightType.Spot,
                EntityType.Rect => LightType.Spot,
                EntityType.Spot => LightType.Spot,
                _ => throw new NotImplementedException()
            },

            Color = entity.GetColor32Property("color"),

            Brightness = type switch
            {
                EntityType.Environment or EntityType.Omni or EntityType.Spot or EntityType.Ortho
                    => entity.GetFloatProperty("brightness", 1.0f),
                _ => entity.GetFloatProperty("brightness_lumens", 224.0f)
            },

            BrightnessScale = entity.GetFloatProperty("brightnessscale", 1.0f),
            SkyColor = entity.GetColor32Property("skycolor"),
            SkyIntensity = entity.GetFloatProperty("skyintensity", 1.0f),
            SkyAmbientBounce = entity.GetColor32Property("skyambientbounce"),
            SkyAmbientBounceAlpha = entity.GetColor32Alpha("skyambientbounce"),
            Range = entity.GetFloatProperty("range", 512.0f),
            FallOff = entity.GetFloatProperty("skirt", 0.1f),
            MinRoughness = entity.GetFloatProperty("minroughness", MinSpecularRoughness),

            Enabled = entity.GetInt32Property("enabled", 1) != 0,
            RenderDiffuse = entity.GetInt32Property("renderdiffuse", 1) != 0,
            RenderSpecular = entity.GetInt32Property("renderspecular", 1) == 1, // 0 = off, 1 = real time, 2 = baked into cubemaps
            RenderTransmissive = entity.GetInt32Property("rendertransmissive", 1) != 0,
            FogContributionStrength = entity.GetFloatProperty("fogcontributionstrength", 1.0f),
            PrecomputedMaxRange = entity.GetFloatProperty("precomputedmaxrange", 0f),
        };

        if (entity.TryGetValue("lower_hemisphere_is_black", out _))
        {
            light.LowerHemisphereIsBlack = entity.GetInt32Property("lower_hemisphere_is_black", 1) != 0;
        }

        // The sun and ortho lights light the fog unless told not to, the other lights only when told to
        var defaultFogLighting = type is EntityType.Environment or EntityType.Ortho ? 1 : 0;

        light.FogLighting = entity.GetInt32Property("fog_lighting", defaultFogLighting);

        if (light.IsLight2)
        {
            light.BarnFog = entity.GetInt32Property("fog") != 0;
            light.BarnFogShadowMode = entity.GetInt32Property("fogshadows", -1);
            light.BarnFogStrength = entity.GetFloatProperty("fogstrength");
            light.BarnFogScale = entity.GetFloatProperty("fogscale", 1f);

            // The engine only reads the exposure value, the other brightness units are converted from it by the tools
            light.LinearBrightness = float.Exp2(entity.GetFloatProperty("brightness"));

            if (entity.GetInt32Property("colormode") != 0)
            {
                light.ColorTemperature = entity.GetFloatProperty("colortemperature", 6600f);
                light.Color = ColorSpace.ColorTemperatureToSrgb(light.ColorTemperature.Value);
            }
        }
        else
        {
            SetAttenuation(light, entity.GetFloatProperty("attenuation0"), entity.GetFloatProperty("attenuation1"), entity.GetFloatProperty("attenuation2"));

            if (entity.ContainsKey("bakelightindexscale"))
            {
                light.BakedLightIndexScale = entity.GetFloatProperty("bakelightindexscale");
            }

            if (entity.GetInt32Property("baked_light_indexing", 1) == 0)
            {
                light.StationaryLightIndex = -1;
            }
        }

        var defaultDirectLight = light.IsLight2
            ? DirectLightType.Dynamic
            : DirectLightType.Static;

        light.DirectLight = (DirectLightType)entity.GetInt32Property("directlight", (int)defaultDirectLight);
        light.CastShadows = entity.GetInt32Property("castshadows", 1);
        light.MixedShadows = entity.GetInt32Property("mixedshadows", 0) != 0;
        light.FadeMinDist = entity.GetFloatProperty("fademindist", 0f);
        light.FadeMaxDist = entity.GetFloatProperty("fademaxdist", 0f);
        light.Priority = entity.GetInt32Property("priority", 0);
        light.CascadeRenderStaticObjects = entity.GetInt32Property("cascaderenderstaticobj", -1);
        light.ShadowMapSize = entity.GetInt32Property("shadowmapsize", 1024);
        if (light.ShadowMapSize <= 0)
        {
            light.ShadowMapSize = 1024;
        }

        if (type is EntityType.Spot or EntityType.Barn)
        {
            light.SpotInnerAngle = entity.GetFloatProperty("innerconeangle", light.SpotInnerAngle);
            light.SpotOuterAngle = entity.GetFloatProperty("outerconeangle", light.SpotOuterAngle);
        }

        if (type is EntityType.Spot)
        {
            light.CookieName = GetCookieProperty(entity);
        }

        if (type is EntityType.Ortho)
        {
            light.SizeParams = new Vector3(
                entity.GetFloatProperty("ortholightwidth", 512.0f) * 0.5f,
                entity.GetFloatProperty("ortholightheight", 512.0f) * 0.5f,
                0f);
        }

        if (type is EntityType.Barn)
        {
            light.SoftX = entity.GetFloatProperty("soft_x", 0.25f);
            light.SoftY = entity.GetFloatProperty("soft_y", 0.25f);
            light.SkirtNear = entity.GetFloatProperty("skirt_near", 0.05f);
            light.Shape = entity.GetFloatProperty("shape");
            light.LuminaireSize = entity.GetFloatProperty("luminaire_size", 4f);
            light.LuminaireShape = entity.GetInt32Property("luminaire_shape");
            light.LuminaireAnisotropy = entity.GetFloatProperty("luminaire_anisotropy");
            light.SizeParams = entity.GetVector3Property("size_params");
            light.CookieTexturePath = GetCookieProperty(entity);

            light.Shear = entity.GetVector2Property("shear");
        }

        if (type is EntityType.Rect)
        {
            // Rasterized like an omni2 point light (no luminaire shape) lighting the hemisphere in front of it,
            // the rectangle or disc only matters to path tracing and to the shaders that shade area lights
            light.SpotOuterAngle = 90f;
            light.SizeParams = entity.GetVector3Property("size_params");
            light.Shape = entity.GetFloatProperty("shape"); // 0 = rectangle, 1 = disc
        }

        if (type is EntityType.Omni2)
        {
            light.SpotInnerAngle = entity.GetFloatProperty("inner_angle");
            light.SpotOuterAngle = entity.GetFloatProperty("outer_angle", 180f);
            light.SizeParams = entity.GetVector3Property("size_params");
            light.CookieTexturePath = GetCookieProperty(entity);
            light.LuminaireShape = entity.GetInt32Property("shape");
            light.LuminaireSize = entity.GetFloatProperty("luminaire_size");
        }

        if (light.IsLight2)
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

        // The caller places the light with PlaceAt, the keyvalues are in the entity's local space
        return light;
    }

    private static string? GetCookieProperty(Entity entity)
    {
        var cookie = entity.GetStringProperty("lightcookie");

        return string.IsNullOrEmpty(cookie) ? null : cookie;
    }

    /// <summary>The unit direction of a transform's axis in world space, ignoring its translation.</summary>
    private static Vector3 GetAxisDirection(in Matrix4x4 transform, Vector3 axis)
        => Vector3.Normalize(Vector3.TransformNormal(axis, transform));

    /// <summary>
    /// Sets the attenuation terms as the engine keeps them: a term below 0.001 is zero, and a light
    /// left with no terms at all does not fade.
    /// </summary>
    private static void SetAttenuation(SceneLight light, float constant, float linear, float quadratic)
    {
        const float MinTerm = 0.001f;

        light.AttenuationConstant = constant < MinTerm ? 0f : constant;
        light.AttenuationLinear = linear < MinTerm ? 0f : linear;
        light.AttenuationQuadratic = quadratic < MinTerm ? 0f : quadratic;

        var hasNoTerms = light.AttenuationConstant == 0f && light.AttenuationLinear == 0f && light.AttenuationQuadratic == 0f;

        if (hasNoTerms)
        {
            light.AttenuationConstant = 1f;
        }
    }

    /// <summary>
    /// Gets how far a point or spot light shades per pixel. Without a constant attenuation term the light
    /// ends where its intensity falls to 0.001, or at <see cref="Range"/> when that is nearer.
    /// </summary>
    public float GetAttenuatedRange()
    {
        const float MinIntensity = 0.001f;

        if (AttenuationConstant > 0f || Type is not (LightType.Point or LightType.Spot))
        {
            return Range;
        }

        var color = ColorSpace.SrgbGammaToLinear(Color) * Brightness * BrightnessScale * AttenuationColorScale;
        var luminance = MathUtils.Luminance(color);
        var reach = 0f;

        if (luminance > 0f)
        {
            // Solves luminance / (q * d^2 + l * d) = MinIntensity for d
            var scale = MinIntensity / luminance;
            var a = scale * AttenuationQuadratic;
            var b = scale * AttenuationLinear;

            if (a > 0f)
            {
                reach = (MathF.Sqrt((b * b) + (4f * a)) - b) / (2f * a);
            }
            else if (b > 0f)
            {
                reach = 1f / b;
            }
        }

        return Range > 0f ? MathF.Min(Range, reach) : reach;
    }

    /// <summary>
    /// What this light costs to render, which is <see cref="DirectLight"/> resolved against
    /// <see cref="StationaryLightIndex"/>.
    /// </summary>
    public LightCost Cost => GetCost(DirectLight, StationaryLightIndex);

    /// <inheritdoc cref="Cost"/>
    public static LightCost GetCost(DirectLightType directLight, int stationaryLightIndex)
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

    /// <inheritdoc cref="Cost"/>
    /// <param name="entity">Light entity to read the direct lighting key values from.</param>
    public static LightCost GetCost(Entity entity)
    {
        return GetCost(
            (DirectLightType)entity.GetInt32Property("directlight", (int)DirectLightType.Dynamic),
            entity.GetInt32Property("bakedshadowindex", entity.GetInt32Property("bakelightindex", -1)));
    }

    /// <summary>
    /// Editor model tint for a light entity, keyed on how expensive it is: teal for static, amber for
    /// stationary, red for dynamic.
    /// </summary>
    /// <param name="entity">Light entity to read the direct lighting key values from.</param>
    public static Vector3 GetEditorTint(Entity entity) => GetCost(entity) switch
    {
        LightCost.Stationary => new Vector3(255f, 196f, 64f) / 255f,
        LightCost.Dynamic => new Vector3(255f, 64f, 64f) / 255f,
        _ => new Vector3(0f, 255f, 192f) / 255f,
    };

    internal static bool IsRealTimeLight(SceneLight light)
    {
        if (!light.IsLight2)
        {
            return false;
        }

        // A baked light stays out of the real-time lights even when it has a baked shadow index
        return light.DirectLight is not (DirectLightType.None or DirectLightType.Static);
    }

    /// <summary>
    /// Recomputes the <see cref="BarnFaces"/> array from the current light properties.
    /// </summary>
    /// <param name="cookiePaths">Map from cookie material path to cookie atlas index.</param>
    public void ComputeBarnFaces(Dictionary<string, int> cookiePaths)
    {
        // Face indices shift under the readbacks still in flight, so their countdowns describe nothing.
        faceVisibilityTtl = ulong.MaxValue;

        if (Range <= 0.0001f)
        {
            BarnFaces = [];
            return;
        }

        if (Entity == EntityType.Barn)
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
        else if (Entity == EntityType.Spot && CastShadows == 1)
        {
            if (BarnFaces is not { Length: 1 })
            {
                BarnFaces = new BarnFaceData[1];
            }

            BarnFaces[0] = ComputeSpotLightShadowFace(this);
        }
    }

    // TODO: the engine's near plane for these shadows is not known
    internal const float SpotShadowNearPlane = 1f;

    /// <summary>
    /// The transform that maps the world to the square frustum a spot light renders its shadow from. Its field of view
    /// is twice the outer cone angle.
    /// </summary>
    internal static Matrix4x4 CreateSpotShadowMatrix(Vector3 position, Vector3 forward, Vector3 up, float outerAngle, float far)
    {
        var fieldOfView = float.DegreesToRadians(Math.Clamp(outerAngle, 1f, 89f) * 2f);
        var view = Matrix4x4.CreateLookAtLeftHanded(position, position + forward, up);
        var projection = Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(fieldOfView, 1f, SpotShadowNearPlane, far);

        return view * projection;
    }

    /// <summary>
    /// The one face a shadowed spot light renders its shadow from: a square frustum from the light position
    /// whose field of view is twice the outer cone angle, out to the light's range.
    /// </summary>
    private static BarnFaceData ComputeSpotLightShadowFace(SceneLight light)
    {
        var forward = GetAxisDirection(light.Transform, Vector3.UnitX);
        var up = GetAxisDirection(light.Transform, Vector3.UnitZ);
        var far = MathF.Max(light.Range, SpotShadowNearPlane + 1f);
        var worldToFrustum = CreateSpotShadowMatrix(light.Transform.Translation, forward, up, light.SpotOuterAngle, far);

        return new BarnFaceData
        {
            WorldToFrustum = worldToFrustum,
            FrustumToWorld = InvertFrustum(worldToFrustum),
            ObbToWorld = Matrix4x4.Identity,
        };
    }

    /// <summary>
    /// Returns the shadow map pixel dimensions for this light at the given long-axis resolution, accounting for aspect ratio.
    /// </summary>
    /// <param name="size">Target resolution in texels along the longer axis.</param>
    public (int W, int H) GetShadowFaceDimensions(int size)
    {
        if (UsesOmni2Faces || Entity == EntityType.Spot)
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
    internal const float LinearBrightnessDistance = 100f;

    /// <summary>The brightness one candela gives at the luminaire, before the falloff to <see cref="LinearBrightnessDistance"/>.</summary>
    private const float BrightnessPerCandela = 40f * MathF.PI;

    /// <summary>The <see cref="LinearBrightness"/> of a light with an intensity of <paramref name="candelas"/>.</summary>
    internal static float LinearBrightnessFromCandelas(float candelas)
        => candelas * BrightnessPerCandela / (LinearBrightnessDistance * LinearBrightnessDistance);

    // Mirrored by the BARN_LIGHT_FLAG_ constants in lighting_barn.slang
    private const uint RectLuminaireFlag = 0x1000u;
    private const uint DiscLuminaireFlag = 0x2000u;

    /// <summary>
    /// Linear intensity for the shader, which attenuates it by <paramref name="falloffDistSq"/> over the squared distance from
    /// the light position (zero for a light with no falloff). The luminaire is <paramref name="luminaireDistance"/> in front of that position.
    /// </summary>
    private static float ComputeIntensity(SceneLight light, float falloffDistSq, float luminaireDistance)
    {
        var intensity = light.LinearBrightness * light.BrightnessScale;

        if (falloffDistSq <= 0f)
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
        var farPlane = nearPlane + light.Range;

        var centerX = light.Shear.X / light.Range * nearPlane;
        var centerY = light.Shear.Y / light.Range * nearPlane;

        var eyePosition = light.Transform.Translation
                          - nearPlane * forwardDir - centerX * rightDir - centerY * upDir;

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
            var depthNear = nearPlane + light.SkirtNear * light.Range;
            skirtNear = depthNear / (farPlane * light.SkirtNear);
        }

        var skirtFar = 0f;
        if (light.FallOff > 0)
        {
            var depthFar = nearPlane + (1f - light.FallOff) * light.Range;
            skirtFar = depthFar / (nearPlane * light.FallOff);
        }

        var eyeToOriginDistSq = centerX * centerX + centerY * centerY + nearPlane * nearPlane;
        var position = new Vector4(eyePosition, eyeToOriginDistSq);

        return (worldToFrustum, position, skirtNear, skirtFar);
    }

    private static BarnFaceData ComputeBarnLightFace(SceneLight light, Dictionary<string, int> cookiePaths)
    {
        var forwardDir = GetAxisDirection(light.Transform, Vector3.UnitX);
        var upDir = GetAxisDirection(light.Transform, Vector3.UnitZ);
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

        var (distFadeOffset, distFadeRate) = light.BarnDistanceSkirt is { } distanceSkirt
            ? ComputeDistanceFade(light.Range, distanceSkirt)
            : (1f, 0f);

        return new BarnFaceData
        {
            GpuData = new BarnLightConstants
            {
                BarnFrustum = worldToFrustum,
                BarnLightPosition = barnLightPosition,
                BarnLightDistanceFade_vSkirt = new Vector4(distFadeOffset, distFadeRate, skirtNear, skirtFar),
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
        var forward = GetAxisDirection(light.Transform, Vector3.UnitX);
        var right = GetAxisDirection(light.Transform, Vector3.UnitY);
        var up = GetAxisDirection(light.Transform, Vector3.UnitZ);

        var angleRad = float.DegreesToRadians(Math.Clamp(light.SpotOuterAngle, 1f, 180f));
        var heightOffset = light.LuminaireShape is 1 or 2 ? light.SizeParams.Y : 0f;
        var sideRange = angleRad < MathF.PI / 2f
            ? MathF.Max(light.Range - heightOffset, 0f) * MathF.Sin(angleRad) + heightOffset
            : light.Range;
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

        var (distFadeOffset, distFadeRate) = ComputeDistanceFade(light.Range, light.FallOff);
        var (angleBias, angleScale) = light.NoAngleFade ? (1f, 0f) : ComputeAngleFade(light.SpotOuterAngle, light.SpotInnerAngle);
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
            var back = angleRad > MathF.PI / 2f ? -MathF.Cos(angleRad) * light.Range : 0f;
            var boxView = Matrix4x4.CreateLookAtLeftHanded(origin, origin + forward, up);
            var boxProj = Matrix4x4.CreateOrthographicOffCenterLeftHanded(-sideRange, sideRange, -sideRange, sideRange, -back, light.Range);
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

        light.BarnFaces[0] = MakeFace(forward, up, light.Range, 0);

        if (faceCount >= 5)
        {
            light.BarnFaces[1] = MakeFace(-up, forward, sideRange, 1);
            light.BarnFaces[2] = MakeFace(up, -forward, sideRange, 2);
            light.BarnFaces[3] = MakeFace(right, up, sideRange, 3);
            light.BarnFaces[4] = MakeFace(-right, up, sideRange, 4);
        }

        if (faceCount >= 6)
        {
            light.BarnFaces[5] = MakeFace(-forward, up, MathF.Abs(MathF.Cos(angleRad)) * light.Range, 5);
        }
    }

    /// <summary>
    /// Calculates the effective solid angle of a spotlight cone, clamped to a maximum of 4pi steradians (full sphere).
    /// </summary>
    public float ComputeConeSolidAngle() => ComputeConeSolidAngle(SpotOuterAngle, SpotInnerAngle);

    /// <inheritdoc cref="ComputeConeSolidAngle()"/>
    /// <param name="spotOuterAngle">The outer cone angle, in degrees.</param>
    /// <param name="spotInnerAngle">The inner cone angle, in degrees.</param>
    public static float ComputeConeSolidAngle(float spotOuterAngle, float spotInnerAngle)
    {
        var outerRad = float.DegreesToRadians(Math.Clamp(spotOuterAngle, 1f, 180f));
        var innerRad = float.DegreesToRadians(Math.Clamp(spotInnerAngle, 0f, spotOuterAngle));

        var avgCos = (MathF.Cos(outerRad) + MathF.Cos(innerRad)) * 0.5f;
        return MathF.Min(MathF.Tau * (1f - avgCos), 4f * MathF.PI);
    }

    /// <summary>
    /// The fraction of the screen the light's bounds cover, from 0 to 1.
    /// </summary>
    /// <param name="cameraPosition">Position of the camera the light is seen from.</param>
    /// <param name="tanHalfHorizontalFov">Tangent of half the camera's horizontal field of view.</param>
    internal float ComputeScreenSize(Vector3 cameraPosition, float tanHalfHorizontalFov)
    {
        var bounds = PrecomputedFieldsValid ? PrecomputedBounds : new AABB(Position, Range);
        var diagonal = PrecomputedFieldsValid ? PrecomputedBoundsDiagonal : Vector3.Distance(bounds.Min, bounds.Max);
        var distanceSq = Vector3.DistanceSquared(cameraPosition, bounds.Center);

        // Covers the whole screen with the camera close enough to be inside the bounds
        return diagonal * diagonal > distanceSq
            ? 1f
            : MathUtils.Saturate(diagonal / (MathF.Sqrt(distanceSq) * tanHalfHorizontalFov));
    }

    /// <summary>The center of the light's bounds, which its distance to the camera is measured to.</summary>
    internal Vector3 BoundsCenter => PrecomputedFieldsValid ? PrecomputedBounds.Center : Position;

    /// <summary>
    /// How much of the light, and of its shadows, is left after fading out with the fraction of the screen its bounds cover.
    /// </summary>
    /// <param name="screenSize">Result of <see cref="ComputeScreenSize"/>.</param>
    internal (float Light, float Shadow) ComputeScreenSizeFades(float screenSize)
    {
        float Fade(float start, float end) => 1f - MathUtils.Saturate((start - screenSize) / MathF.Max(1e-4f, start - end));

        return (Fade(FadeSizeStart, FadeSizeEnd), Fade(ShadowFadeSizeStart, ShadowFadeSizeEnd));
    }

    /// <summary>Linear color an omni2 or rect light is lit with, which is also the radiance its visible geometry starts from.</summary>
    internal Vector3 ComputeOmni2Color()
        => ColorSpace.SrgbGammaToLinear(Color) * ComputeIntensity(this, 1f, 0f);

    /// <summary>
    /// The fade from full at <c>(1 - skirt) * range</c> to none at <paramref name="range"/>, as an offset and a
    /// rate per unit of distance. A skirt that is not positive makes it a sharp cut at <c>(1 - skirt) * range</c>.
    /// </summary>
    private static (float Offset, float Rate) ComputeDistanceFade(float range, float skirt)
    {
        var fadeStart = (1f - skirt) * range;
        var fadeEnd = MathF.Max(range, MathF.BitIncrement(fadeStart));
        var fadeLength = fadeEnd - fadeStart;

        return (fadeEnd / fadeLength, -1f / fadeLength);
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
