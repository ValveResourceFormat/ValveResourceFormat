using System.Collections.Frozen;
using System.Linq;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Decals
{
    /// <summary>How a projected decal is blended over what is under it.</summary>
    public enum ProjectedDecalBlendMode
    {
        /// <summary>Lit and alpha blended.</summary>
        Translucent,
        /// <summary>Multiplies by twice its color, so mid grey leaves the surface unchanged.</summary>
        Mod2x,
        /// <summary>Adds its own light.</summary>
        Emissive,
        /// <summary>Multiplies by its color.</summary>
        Liquid,
    }

    /// <summary>A decal placed with <see cref="ProjectedDecalSystem"/>, for removing it again.</summary>
    /// <param name="Id">Identifies the decal, zero for none.</param>
    public readonly record struct ProjectedDecalHandle(uint Id)
    {
        /// <summary>Gets whether a decal was placed.</summary>
        public bool IsValid => Id != 0;
    }

    /// <summary>
    /// What a projected decal looks like and how large it is. Register one with
    /// <see cref="ProjectedDecalSystem.Register"/>. Whether its textures are files of their own or parts of
    /// an atlas only changes <see cref="TextureRect"/>.
    /// </summary>
    public sealed record ProjectedDecalDefinition
    {
        private const float DefaultSize = 8f;
        private const float DefaultDepth = 12f;

        // A decal picked out of a sprite sheet takes these when its sequence does not say
        private const float DefaultSequenceDepth = 16f;
        private const float DefaultSequenceScaleVariation = 0.25f;
        private const float DefaultSequenceFadeStartTime = 10f;

        /// <summary>Gets the color texture, with opacity in alpha.</summary>
        public required string ColorTexture { get; init; }

        /// <summary>Gets the normal texture: a hemi-octahedral normal in red and green, roughness in blue.</summary>
        public string? NormalTexture { get; init; }

        /// <summary>Gets the texture with ambient occlusion in red and metalness in green.</summary>
        public string? OcclusionTexture { get; init; }

        /// <summary>Gets the height texture, which turns on parallax occlusion mapping.</summary>
        public string? HeightTexture { get; init; }

        /// <summary>
        /// Gets the part of every texture the decal uses: the minimum corner in xy and the maximum in zw,
        /// each from 0 to 1. The whole texture by default, a smaller rectangle for a decal kept in an atlas.
        /// </summary>
        public Vector4 TextureRect { get; init; } = new(0f, 0f, 1f, 1f);

        /// <summary>Gets how the decal is blended.</summary>
        public ProjectedDecalBlendMode BlendMode { get; init; }

        /// <summary>
        /// Gets the shader features that are on, by the names decal materials give them, such as
        /// <c>F_TRIPLANAR_MAPPING</c>. The decal shader decides what each does, and ignores those it lacks.
        /// </summary>
        public IReadOnlySet<string> Features { get; init; } = FrozenSet<string>.Empty;

        /// <summary>Gets the alpha range the cutoff edge is softened over, for a decal whose alpha is a cutoff.</summary>
        public float AlphaCutoffSoftness { get; init; } = 0.1f;

        /// <summary>
        /// Gets the angle in degrees between the surface and the projection past which the decal is cut off,
        /// or <see langword="null"/> to fade it along the box depth instead.
        /// </summary>
        public float? CutoffAngle { get; init; }

        /// <summary>Gets the angle in degrees the cutoff fades over.</summary>
        public float CutoffAngleSoftness { get; init; } = 5f;

        /// <summary>Gets the fraction of the box depth the decal fades in over.</summary>
        public float DepthFade { get; init; } = 0.01f;

        /// <summary>Gets how far the height texture displaces, in texture space.</summary>
        public float HeightScale { get; init; } = 0.02f;

        /// <summary>Gets the parallax steps taken where the surface is viewed head on.</summary>
        public int ParallaxMinSamples { get; init; } = 8;

        /// <summary>Gets the parallax steps taken where the surface is viewed at a grazing angle.</summary>
        public int ParallaxMaxSamples { get; init; } = 32;

        /// <summary>Gets the mip level past which parallax is skipped.</summary>
        public int ParallaxLodThreshold { get; init; } = 4;

        /// <summary>Gets the linear color an <see cref="ProjectedDecalBlendMode.Emissive"/> decal is multiplied by.</summary>
        public Vector3 EmissiveColor { get; init; } = Vector3.One;

        /// <summary>Gets how much of the surface an emissive decal leaves visible under it, from 0 to 1.</summary>
        public float AdditiveAmount { get; init; } = 1f;

        /// <summary>Gets the linear color the color texture is multiplied by.</summary>
        public Vector3 ColorTint { get; init; } = Vector3.One;

        /// <summary>Gets what the opacity is multiplied by.</summary>
        public float OpacityScale { get; init; } = 1f;

        /// <summary>Gets the texture whose green channel masks where the decal glows, see <see cref="SelfIllumScale"/>.</summary>
        public string? SelfIllumTexture { get; init; }

        /// <summary>
        /// Gets how brightly a lit decal glows in its own color where <see cref="SelfIllumTexture"/> is set, as a
        /// multiple of that color up to ten. Zero for no glow.
        /// </summary>
        public float SelfIllumScale { get; init; }

        /// <summary>Gets how much the decal fades where the surface turns away from the projection, from 0 to 1.</summary>
        public float SlopeFade { get; init; }

        /// <summary>Gets the power the surface facing is raised to for <see cref="SlopeFade"/>, so higher fades on gentler slopes.</summary>
        public float SlopeFadeSharpness { get; init; } = 1f;

        /// <summary>Gets the distance from the box faces the decal fades in over, as a fraction of the box. Zero for none.</summary>
        public float EdgeWidth { get; init; }

        /// <summary>Gets the roughness used without a normal texture.</summary>
        public float Roughness { get; init; } = 0.5f;

        /// <summary>Gets the width of the decal in world units.</summary>
        public float Width { get; init; } = DefaultSize;

        /// <summary>Gets the height of the decal in world units.</summary>
        public float Height { get; init; } = DefaultSize;

        /// <summary>Gets how far the decal box reaches through the surface, in world units.</summary>
        public float Depth { get; init; } = DefaultDepth;

        /// <summary>Gets how far the box centre sits out of the surface, in world units.</summary>
        public float DepthOffset { get; init; }

        /// <summary>Gets the most the width and height vary by together, in world units.</summary>
        public float SizeVariance { get; init; }

        /// <summary>Gets the most the height varies by on its own, in world units.</summary>
        public float HeightVariance { get; init; }

        /// <summary>Gets the most the depth varies by, in world units.</summary>
        public float DepthVariance { get; init; }

        /// <summary>Gets the seconds a decal stays before it starts to fade.</summary>
        public float FadeStartTime { get; init; } = 30f;

        /// <summary>Gets the seconds the fade takes.</summary>
        public float FadeDuration { get; init; } = 3f;

        /// <summary>
        /// Reads a definition from a projected decal material.
        /// </summary>
        /// <param name="fileLoader">Loads the material, and the color texture when a sequence is named.</param>
        /// <param name="materialPath">The material.</param>
        /// <param name="sequenceName">
        /// A sequence of the color texture's sprite sheet. The decal then uses that part of the texture, at
        /// the size and with the fade times the sequence gives.
        /// </param>
        /// <returns>
        /// The definition, or <see langword="null"/> when the material or the sequence is missing, or the
        /// material is not on a supported decal shader.
        /// </returns>
        public static ProjectedDecalDefinition? FromMaterial(GameFileLoader fileLoader, string materialPath, string? sequenceName = null)
        {
            ArgumentNullException.ThrowIfNull(fileLoader);

            using var resource = fileLoader.LoadFileCompiled(materialPath);

            if (resource?.DataBlock is not Material data
                || !data.TextureParams.TryGetValue("g_tColor", out var colorTexture))
            {
                return null;
            }

            if (data.ShaderName == "projected_decals.vfx")
            {
                var deferredDecal = FromDeferredMaterial(data, colorTexture);

                return string.IsNullOrEmpty(sequenceName)
                    ? deferredDecal
                    : deferredDecal.WithSheetSequence(fileLoader, sequenceName);
            }

            if (data.ShaderName is not ("csgo_projected_decals.vfx" or "vr_projected_decals.vfx"))
            {
                return null;
            }

            var intParams = data.IntParams;
            var floatParams = data.FloatParams;
            var attributes = data.FloatAttributes;

            // Triplanar decals carry no decal size, only how far their texture spans
            var worldMapping = data.IntAttributes;
            var defaultHeight = (float)worldMapping.GetValueOrDefault("WorldMappingHeight", worldMapping.GetValueOrDefault("WorldMappingWidth", (long)DefaultSize));
            var defaultWidth = (float)worldMapping.GetValueOrDefault("WorldMappingWidth", (long)defaultHeight);

            var isTriplanar = intParams.GetValueOrDefault("F_TRIPLANAR_MAPPING") == 1;
            var height = attributes.GetValueOrDefault("DecalWorldHeight", attributes.GetValueOrDefault("DecalWorldWidth", defaultHeight));
            var width = attributes.GetValueOrDefault("DecalWorldWidth", attributes.ContainsKey("DecalWorldHeight") ? height : defaultWidth);

            var emissiveTint = data.VectorParams.GetValueOrDefault("g_vEmissiveTint", Vector4.One).AsVector3();
            var emissiveScale = MathF.Pow(2f, floatParams.GetValueOrDefault("g_flEmissiveBrightness"));

            var definition = new ProjectedDecalDefinition { ColorTexture = colorTexture };

            definition = definition with
            {
                NormalTexture = intParams.GetValueOrDefault("F_NORMAL_MAP") == 1 ? data.TextureParams.GetValueOrDefault("g_tNormal") : null,
                OcclusionTexture = data.TextureParams.GetValueOrDefault("g_tAmbientOcclusion"),
                HeightTexture = intParams.GetValueOrDefault("F_PARALLAX") == 1 ? data.TextureParams.GetValueOrDefault("g_tHeight") : null,
                BlendMode = (ProjectedDecalBlendMode)Math.Clamp(intParams.GetValueOrDefault("F_BLEND_MODE"), 0L, 3L),
                Features = intParams.Where(p => p.Value != 0 && p.Key.StartsWith("F_", StringComparison.Ordinal))
                    .Select(p => p.Key).ToFrozenSet(StringComparer.Ordinal),
                AlphaCutoffSoftness = floatParams.GetValueOrDefault("g_flAlphaCutoffSoftness", definition.AlphaCutoffSoftness),
                CutoffAngle = intParams.GetValueOrDefault("F_CUTOFF_ANGLE") == 1 ? floatParams.GetValueOrDefault("g_flCutoffAngle", 60f) : null,
                CutoffAngleSoftness = floatParams.GetValueOrDefault("g_flCutoffAngleSoftness", definition.CutoffAngleSoftness),
                DepthFade = floatParams.GetValueOrDefault("g_flDecalZAlphaScale", definition.DepthFade),
                HeightScale = floatParams.GetValueOrDefault("g_flHeightMapScale", definition.HeightScale),
                ParallaxMinSamples = (int)intParams.GetValueOrDefault("g_nMinSamples", definition.ParallaxMinSamples),
                ParallaxMaxSamples = (int)intParams.GetValueOrDefault("g_nMaxSamples", definition.ParallaxMaxSamples),
                ParallaxLodThreshold = (int)intParams.GetValueOrDefault("g_nLODThreshold", definition.ParallaxLodThreshold),
                EmissiveColor = ColorSpace.SrgbGammaToLinear(emissiveTint) * emissiveScale,
                AdditiveAmount = floatParams.GetValueOrDefault("g_flAdditiveAmount", definition.AdditiveAmount),
                Roughness = MathF.Max(0.01f, 1f - floatParams.GetValueOrDefault("g_flGlossiness", 0.5f)),
                Width = width,
                Height = height,
                // A triplanar decal wraps around what is inside its box, so it reaches as far out of the surface as along it
                Depth = attributes.GetValueOrDefault("DecalDepth", isTriplanar ? width : DefaultDepth),
                DepthOffset = attributes.GetValueOrDefault("DecalDepthOffset"),
                SizeVariance = attributes.GetValueOrDefault("DecalSizeVariance"),
                HeightVariance = attributes.GetValueOrDefault("DecalHeightVariance"),
                DepthVariance = attributes.GetValueOrDefault("DecalDepthVariance"),
                FadeStartTime = attributes.GetValueOrDefault("DecalFadeStartTime", definition.FadeStartTime),
                FadeDuration = attributes.GetValueOrDefault("DecalFadeDuration", definition.FadeDuration),
            };

            return string.IsNullOrEmpty(sequenceName)
                ? definition
                : definition.WithSheetSequence(fileLoader, sequenceName);
        }

        /// <summary>
        /// Reads a decal from a material on the shader that writes decals into a deferred g-buffer. Its color
        /// has opacity in alpha and its normal texture is hemi-octahedral with roughness in blue, as this
        /// renderer reads them. It carries no size, the particle or entity placing it gives one.
        /// </summary>
        private static ProjectedDecalDefinition FromDeferredMaterial(Material data, string colorTexture)
        {
            var intParams = data.IntParams;
            var floatParams = data.FloatParams;

            // The g-buffer lights it like any other surface, so it has a dielectric specular
            List<string> features = ["F_SPECULAR"];

            // Its cutoff matches the alpha mode: particle alpha erodes the color alpha over the cutoff width
            if (intParams.GetValueOrDefault("g_bCutoff") == 1)
            {
                features.Add("F_ALPHA_MODE");
            }

            // Biplanar projection picks the two box axes the surface faces most, triplanar mapping is the nearest
            if (intParams.GetValueOrDefault("F_BIPLANAR_PROJECTION") == 1)
            {
                features.Add("F_TRIPLANAR_MAPPING");
            }

            var multiplyAlbedo = intParams.GetValueOrDefault("F_MULTIPLY_ALBEDO") == 1;
            var writesNormal = floatParams.GetValueOrDefault("g_flNormalOpacity", 1f) > 0f;
            var colorTint = data.VectorParams.GetValueOrDefault("g_vColorTint", Vector4.One).AsVector3();

            return new ProjectedDecalDefinition
            {
                ColorTexture = colorTexture,
                NormalTexture = writesNormal ? data.TextureParams.GetValueOrDefault("g_tNormalRoughness") : null,
                SelfIllumTexture = data.TextureParams.GetValueOrDefault("g_tSelfIllum"),
                BlendMode = multiplyAlbedo ? ProjectedDecalBlendMode.Liquid : ProjectedDecalBlendMode.Translucent,
                Features = features.ToFrozenSet(StringComparer.Ordinal),
                AlphaCutoffSoftness = MathF.Max(floatParams.GetValueOrDefault("g_flCutoffWidth", 0.1f), 0.001f),
                // It fades by slope and toward its box faces, not along the box depth
                DepthFade = 0f,
                ColorTint = ColorSpace.SrgbGammaToLinear(colorTint),
                OpacityScale = floatParams.GetValueOrDefault("g_flOpacityScale", 1f),
                // A scale set at run time keeps the value the material was saved with
                SelfIllumScale = floatParams.GetValueOrDefault("g_flSelfIllumScale", 1f),
                SlopeFade = floatParams.GetValueOrDefault("g_flSlopeFade"),
                SlopeFadeSharpness = floatParams.GetValueOrDefault("g_flSlopeFadeSharpness", 1f),
                EdgeWidth = floatParams.GetValueOrDefault("g_flEdgeWidth"),
            };
        }

        /// <summary>
        /// Narrows the decal to one sequence of the color texture's sprite sheet, taking the part of the
        /// texture its first frame covers, and the size and fade times its parameters give.
        /// </summary>
        /// <param name="fileLoader">Loads the color texture.</param>
        /// <param name="sequenceName">The name of the sequence.</param>
        /// <returns>The narrowed definition, or <see langword="null"/> when the texture has no such sequence.</returns>
        public ProjectedDecalDefinition? WithSheetSequence(GameFileLoader fileLoader, string sequenceName)
        {
            ArgumentNullException.ThrowIfNull(fileLoader);

            // Not disposed: a caching loader may share this texture with a material still streaming its mips
            var resource = fileLoader.LoadFileCompiled(ColorTexture);

            if (resource?.DataBlock is not Texture texture || texture.GetSpriteSheetData() is not { } sheet)
            {
                return null;
            }

            var sequence = Array.Find(sheet.Sequences, s => s.Name.Equals(sequenceName, StringComparison.OrdinalIgnoreCase));

            if (sequence is not { Frames: [{ Images: [var image, ..] }, ..] })
            {
                return null;
            }

            var pixels = image.GetUncroppedRect(texture.Width, texture.Height);

            if (pixels.IsEmpty)
            {
                return null;
            }

            var parameters = sequence.FloatParams;
            var scale = parameters.GetValueOrDefault("decalScale", 1f);
            var width = pixels.Width * scale;

            return this with
            {
                TextureRect = new Vector4(
                    (float)pixels.Left / texture.Width, (float)pixels.Top / texture.Height,
                    (float)pixels.Right / texture.Width, (float)pixels.Bottom / texture.Height),
                // The frame is sized in texels, which the scale turns into world units
                Width = width,
                Height = pixels.Height * scale,
                Depth = parameters.GetValueOrDefault("decalDepth", DefaultSequenceDepth),
                DepthOffset = 0f,
                SizeVariance = width * parameters.GetValueOrDefault("decalScaleVariation", DefaultSequenceScaleVariation),
                HeightVariance = 0f,
                DepthVariance = 0f,
                FadeStartTime = parameters.GetValueOrDefault("decalStartFadeTime", DefaultSequenceFadeStartTime),
                FadeDuration = parameters.GetValueOrDefault("decalFadeDuration", FadeDuration),
            };
        }
    }
}
