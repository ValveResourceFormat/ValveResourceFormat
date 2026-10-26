using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>One entry of m_vecTexturesInput: a texture plus how it folds into the layers below it.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/TextureGroup_t">TextureGroup_t</seealso>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/TextureControls_t">TextureControls_t</seealso>
    internal sealed class ParticleTextureLayer(RenderTexture texture)
    {
        /// <summary>The most layers any spritecard renderer composites.</summary>
        public const int MaxLayers = 5;

        // Uniform array elements need literal names, so they are built once, not per draw.
        private static readonly string[] TextureNames = ["uTexture", "uTextureLayer1", "uTextureLayer2", "uTextureLayer3", "uTextureLayer4"];
        private static readonly string[] ChannelNames = ["uLayerChannels[0]", "uLayerChannels[1]", "uLayerChannels[2]", "uLayerChannels[3]", "uLayerChannels[4]"];
        private static readonly string[] BlendModeNames = ["uLayerBlendMode[0]", "uLayerBlendMode[1]", "uLayerBlendMode[2]", "uLayerBlendMode[3]", "uLayerBlendMode[4]"];
        private static readonly string[] BlendNames = ["uLayerBlend[0]", "uLayerBlend[1]", "uLayerBlend[2]", "uLayerBlend[3]", "uLayerBlend[4]"];
        private static readonly string[] EffectModeNames = ["uLayerEffectMode[0]", "uLayerEffectMode[1]", "uLayerEffectMode[2]", "uLayerEffectMode[3]", "uLayerEffectMode[4]"];
        private static readonly string[] DistortionNames = ["uLayerDistortion[0]", "uLayerDistortion[1]", "uLayerDistortion[2]", "uLayerDistortion[3]", "uLayerDistortion[4]"];
        private static readonly string[] ZoomScaleNames = ["uLayerZoomScale[0]", "uLayerZoomScale[1]", "uLayerZoomScale[2]", "uLayerZoomScale[3]", "uLayerZoomScale[4]"];
        private static readonly string[] UvScaleOffsetNames = ["uLayerUvScaleOffset[0]", "uLayerUvScaleOffset[1]", "uLayerUvScaleOffset[2]", "uLayerUvScaleOffset[3]", "uLayerUvScaleOffset[4]"];
        private static readonly string[] UvRotationNames = ["uLayerUvRotation[0]", "uLayerUvRotation[1]", "uLayerUvRotation[2]", "uLayerUvRotation[3]", "uLayerUvRotation[4]"];
        private static readonly string[] SheetNames = ["uLayerSheet[0]", "uLayerSheet[1]", "uLayerSheet[2]", "uLayerSheet[3]", "uLayerSheet[4]"];
        private static readonly string[] PerParticleModesNames = ["uLayerPerParticleModes[0]", "uLayerPerParticleModes[1]", "uLayerPerParticleModes[2]", "uLayerPerParticleModes[3]", "uLayerPerParticleModes[4]"];
        private static readonly string[] LookupNames = ["uLayerLookup[0]", "uLayerLookup[1]", "uLayerLookup[2]", "uLayerLookup[3]", "uLayerLookup[4]"];

        /// <summary>The bit of the packed per particle modes that randomizes the layer's offsets.</summary>
        private const int RandomizeOffsetsBit = 28;

        private static readonly INumberProvider One = new LiteralNumberProvider(1f);
        private static readonly INumberProvider Zero = new LiteralNumberProvider(0f);

        public RenderTexture Texture { get; set; } = texture;
        public SpriteCardTextureChannel Channels { get; init; } = SpriteCardTextureChannel.SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA;
        public SpriteCardTextureType EffectMode { get; init; } = SpriteCardTextureType.SPRITECARD_TEXTURE_DIFFUSE;
        public ParticleTextureLayerBlendType BlendMode { get; init; } = ParticleTextureLayerBlendType.SPRITECARD_TEXTURE_BLEND_MULTIPLY;
        public INumberProvider Blend { get; init; } = One;

        /// <summary>m_flFinalTextureScaleU/V: how many times this layer's texture tiles across the card.</summary>
        public INumberProvider ScaleU { get; init; } = One;

        /// <inheritdoc cref="ScaleU"/>
        public INumberProvider ScaleV { get; init; } = One;

        /// <summary>m_flFinalTextureOffsetU/V: where this layer's texture starts within the card.</summary>
        public INumberProvider OffsetU { get; init; } = Zero;

        /// <inheritdoc cref="OffsetU"/>
        public INumberProvider OffsetV { get; init; } = Zero;

        /// <summary>m_flDistortion: how far a distortion layer pushes the layer beneath it.</summary>
        public INumberProvider Distortion { get; init; } = Zero;

        /// <summary>m_flZoomScale: how fast a zoom layer sweeps between its two magnifications.</summary>
        public INumberProvider ZoomScale { get; init; } = Zero;

        /// <summary>
        /// m_flFinalTextureUVRotation: how far this layer's texture turns about the card centre, in
        /// degrees. A texture with a sheet does not turn; the value scales how fast it plays instead.
        /// </summary>
        public INumberProvider Rotation { get; init; } = Zero;

        /// <summary>m_bClampUVs: whether this layer is sampled with its edge texels held outside its range instead of repeating.</summary>
        public bool ClampUVs { get; init; }

        /// <summary>m_bRandomizeOffsets: whether each particle shifts this layer by a random amount of its own.</summary>
        public bool RandomizeOffsets { get; init; }

        /// <summary>m_nPerParticleScale: the per particle value <see cref="ScaleU"/> and <see cref="ScaleV"/> are multiplied by.</summary>
        public SpriteCardPerParticleScale PerParticleScale { get; init; }

        /// <summary>m_nPerParticleOffsetU: the per particle value <see cref="OffsetU"/> is multiplied by.</summary>
        public SpriteCardPerParticleScale PerParticleOffsetU { get; init; }

        /// <summary>m_nPerParticleOffsetV: the per particle value <see cref="OffsetV"/> is multiplied by.</summary>
        public SpriteCardPerParticleScale PerParticleOffsetV { get; init; }

        /// <summary>
        /// m_nPerParticleRotation: the per particle value <see cref="Rotation"/> is multiplied by, which on a
        /// layer with a sheet scales how fast it plays.
        /// </summary>
        public SpriteCardPerParticleScale PerParticleRotation { get; init; }

        /// <summary>m_nPerParticleZoom: the per particle value <see cref="ZoomScale"/> is multiplied by, on zoom layers only.</summary>
        public SpriteCardPerParticleScale PerParticleZoom { get; init; }

        /// <summary>m_nPerParticleDistortion: the per particle value <see cref="Distortion"/> is multiplied by, on distortion layers only.</summary>
        public SpriteCardPerParticleScale PerParticleDistortion { get; init; }

        /// <summary>
        /// m_nPerParticleBlend: the per particle value <see cref="Blend"/> is multiplied by on a layer without
        /// a sheet, and on a distortion layer the distortion strength instead.
        /// </summary>
        public SpriteCardPerParticleScale PerParticleBlend { get; init; }

        /// <summary>
        /// The per particle modes packed four bits each in the order the shader keys them (scale, offset u,
        /// offset v, rotation, zoom, distortion, blend), with the randomized offsets flag above them.
        /// </summary>
        private uint PackPerParticleModes()
        {
            var packed = (uint)PerParticleScale
                | ((uint)PerParticleOffsetU << 4)
                | ((uint)PerParticleOffsetV << 8)
                | ((uint)PerParticleRotation << 12)
                | ((uint)PerParticleZoom << 16)
                | ((uint)PerParticleDistortion << 20)
                | ((uint)PerParticleBlend << 24);

            if (RandomizeOffsets)
            {
                packed |= 1u << RandomizeOffsetsBit;
            }

            return packed;
        }

        /// <summary>
        /// Binds the chain's textures and describes every layer to the shader: how it composites, how a
        /// card coordinate is placed in it, which per particle values scale its controls, and where the
        /// sequences of its sheet sit in <paramref name="sheets"/>, adding them there the first time.
        /// </summary>
        public static void Bind(Shader shader, ParticleTextureLayer[] layers, ParticleSystemState systemState, ParticleSheetCache sheets)
        {
            shader.SetUniform1("uLayerCount", layers.Length);

            var frameZoom = Vector2.One;
            var anyLayerHasSheet = false;

            for (var layer = 0; layer < layers.Length; layer++)
            {
                var source = layers[layer];
                var zoomScale = source.ZoomScale.NextNumber(systemState);

                shader.SetTexture(RenderMaterial.TextureUnitStart + layer, TextureNames[layer], source.Texture);
                shader.SetUniform1(ChannelNames[layer], (int)source.Channels);
                shader.SetUniform1(BlendModeNames[layer], (int)source.BlendMode);
                shader.SetUniform1(BlendNames[layer], source.Blend.NextNumber(systemState));
                shader.SetUniform1(EffectModeNames[layer], (int)source.EffectMode);
                shader.SetUniform1(DistortionNames[layer], source.Distortion.NextNumber(systemState));
                shader.SetUniform1(ZoomScaleNames[layer], MathUtils.Wrap(zoomScale, 0f, 10f));

                // The first two layers zoom towards the card centre as they cross between sheet frames,
                // by the unwrapped zoom scale
                if (layer == 0)
                {
                    frameZoom.X = MathUtils.Saturate(1f / zoomScale);
                }
                else if (layer == 1)
                {
                    frameZoom.Y = MathUtils.Saturate(1f / zoomScale);
                }

                var rows = sheets.GetRows(source.Texture);
                var hasSheet = rows.SequenceCount > 0;
                anyLayerHasSheet |= hasSheet;

                // A layer showing a sheet does not turn; its rotation scales how fast it plays instead
                var rotation = source.Rotation.NextNumber(systemState);
                var uvRotation = hasSheet ? rotation : float.DegreesToRadians(rotation);

                var scaleOffset = new Vector4(
                    source.ScaleU.NextNumber(systemState),
                    source.ScaleV.NextNumber(systemState),
                    source.OffsetU.NextNumber(systemState),
                    source.OffsetV.NextNumber(systemState));

                shader.SetUniform4(UvScaleOffsetNames[layer], scaleOffset);
                shader.SetUniform1(UvRotationNames[layer], uvRotation);
                shader.SetUniform2(SheetNames[layer], new Vector2(rows.FirstRow, rows.SequenceCount));
                shader.SetUniform1(PerParticleModesNames[layer], source.PackPerParticleModes());
                shader.SetUniform1(LookupNames[layer], source.EffectMode == SpriteCardTextureType.SPRITECARD_TEXTURE_1D_COLOR_LOOKUP);
            }

            shader.SetUniform2("uFrameZoom", frameZoom);
            shader.SetUniform1("uAnyLayerHasSheet", anyLayerHasSheet);
        }

        /// <summary>
        /// Binds <paramref name="sampler"/> over the texture units of the layers that clamp their uvs, and
        /// over the motion vectors' unit when the first layer does.
        /// </summary>
        /// <param name="layers">The chain being composited.</param>
        /// <param name="hasMotionVectors">Whether a motion vector sheet is bound after the layers.</param>
        /// <param name="sampler">A clamp-to-edge sampler object.</param>
        public static void BindClampSamplers(ParticleTextureLayer[] layers, bool hasMotionVectors, int sampler)
            => SetClampSamplers(layers, hasMotionVectors, sampler);

        /// <summary>Removes the samplers <see cref="BindClampSamplers"/> bound, so the units fall back to their textures' own state.</summary>
        /// <param name="layers">The chain being composited.</param>
        /// <param name="hasMotionVectors">Whether a motion vector sheet is bound after the layers.</param>
        public static void UnbindClampSamplers(ParticleTextureLayer[] layers, bool hasMotionVectors)
            => SetClampSamplers(layers, hasMotionVectors, 0);

        private static void SetClampSamplers(ParticleTextureLayer[] layers, bool hasMotionVectors, int sampler)
        {
            for (var layer = 0; layer < layers.Length; layer++)
            {
                if (layers[layer].ClampUVs)
                {
                    GL.BindSampler(RenderMaterial.TextureUnitStart + layer, sampler);
                }
            }

            if (hasMotionVectors && layers[0].ClampUVs)
            {
                GL.BindSampler(RenderMaterial.TextureUnitStart + MaxLayers, sampler);
            }
        }

        /// <summary>Whether this layer's texture carries a sheet with any sequences.</summary>
        public bool HasSheet => Texture.SpriteSheetData is { Sequences.Length: > 0 };

        /// <summary>
        /// Reads m_vecTexturesInput into the layer chain a spritecard renderer composites, falling back to
        /// a single <paramref name="defaultTextureName"/> layer when the definition supplies none.
        /// </summary>
        /// <param name="parse">The renderer's definition.</param>
        /// <param name="rendererContext">Loads the layer textures.</param>
        /// <param name="defaultTextureName">Texture for the fallback layer.</param>
        /// <param name="srgbRead">Whether the layers are colour; water effects cards carry data instead.</param>
        /// <param name="baseSrgbRead">Whether the first layer is decoded from sRGB, in place of <paramref name="srgbRead"/>.</param>
        /// <returns>
        /// The colour layers in composite order, the motion vector sheet that warps them or null, and the
        /// name of the first colour texture loaded.
        /// </returns>
        public static (ParticleTextureLayer[] Layers, RenderTexture? MotionVectors, string? FirstTextureName) Build(
            ParticleDefinitionParser parse, RendererContext rendererContext, string defaultTextureName, bool srgbRead, bool baseSrgbRead)
        {
            string? firstTextureName = null;
            RenderTexture? motionVectors = null;
            var parsed = new List<ParticleTextureLayer>();

            foreach (var textureInput in parse.Array("m_vecTexturesInput"))
            {
                if (!textureInput.Boolean("m_bEnabled", true))
                {
                    continue;
                }

                var textureType = textureInput.Enum("m_nTextureType", SpriteCardTextureType.SPRITECARD_TEXTURE_DIFFUSE);

                // The first motion vector sheet warps the colour layers rather than becoming one, and is
                // never replaced by a gradient. The vectors are signed data packed about a half, and a gamma
                // decode would bend the flow field, so it is never read as colour.
                if (textureType == SpriteCardTextureType.SPRITECARD_TEXTURE_ANIMMOTIONVEC)
                {
                    if (motionVectors == null && GetTextureName(textureInput) is { } motionVectorsName)
                    {
                        motionVectors = rendererContext.MaterialLoader.GetTexture(motionVectorsName, srgbRead: false, streaming: true);
                    }

                    continue;
                }

                // Only the colour types composite. Of the rest, the spritecard shader has inputs for motion
                // vectors alone, so normal, lighting and depth textures change nothing.
                if (textureType >= SpriteCardTextureType.SPRITECARD_TEXTURE_NORMALMAP || parsed.Count == MaxLayers)
                {
                    continue;
                }

                var controls = textureInput.Data.GetSubCollection("m_TextureControls");
                var uv = controls == null ? textureInput : textureInput.Nested(controls);
                var clampUVs = controls != null && uv.Boolean("m_bClampUVs", false);

                // Zoom and distortion are only scaled per particle on layers that zoom or distort
                var zooms = textureType is SpriteCardTextureType.SPRITECARD_TEXTURE_ZOOM or SpriteCardTextureType.SPRITECARD_TEXTURE_UVDISTORTION_ZOOM;
                var distorts = textureType is SpriteCardTextureType.SPRITECARD_TEXTURE_UVDISTORTION or SpriteCardTextureType.SPRITECARD_TEXTURE_UVDISTORTION_ZOOM;

                RenderTexture layerTexture;

                // A gradient layer synthesizes its ramp from m_Gradient rather than loading a texture.
                if (textureInput.Boolean("m_bReplaceTextureWithGradient", false))
                {
                    layerTexture = MaterialLoader.GenerateGradientTexture(ParseGradientStops(textureInput), clampUVs);
                }
                else
                {
                    // A group that names no texture adds no layer
                    if (GetTextureName(textureInput) is not { } layerTextureName)
                    {
                        continue;
                    }

                    var layerSrgbRead = parsed.Count == 0 ? baseSrgbRead : srgbRead;

                    firstTextureName ??= layerTextureName;
                    layerTexture = rendererContext.MaterialLoader.GetTexture(layerTextureName, layerSrgbRead, streaming: true);
                }

                parsed.Add(new ParticleTextureLayer(layerTexture)
                {
                    Channels = textureInput.Enum("m_nTextureChannels", SpriteCardTextureChannel.SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA),
                    BlendMode = textureInput.Enum("m_nTextureBlendMode", ParticleTextureLayerBlendType.SPRITECARD_TEXTURE_BLEND_MULTIPLY),
                    Blend = textureInput.NumberProvider("m_flTextureBlend", One),
                    EffectMode = textureType,
                    ScaleU = controls == null ? One : uv.NumberProvider("m_flFinalTextureScaleU", One),
                    ScaleV = controls == null ? One : uv.NumberProvider("m_flFinalTextureScaleV", One),
                    OffsetU = controls == null ? Zero : uv.NumberProvider("m_flFinalTextureOffsetU", Zero),
                    OffsetV = controls == null ? Zero : uv.NumberProvider("m_flFinalTextureOffsetV", Zero),
                    Rotation = controls == null ? Zero : uv.NumberProvider("m_flFinalTextureUVRotation", Zero),
                    Distortion = controls == null ? Zero : uv.NumberProvider("m_flDistortion", Zero),
                    ZoomScale = controls == null ? Zero : uv.NumberProvider("m_flZoomScale", Zero),
                    ClampUVs = clampUVs,
                    RandomizeOffsets = controls != null && uv.Boolean("m_bRandomizeOffsets", false),
                    PerParticleScale = PerParticleMode(controls, uv, "m_nPerParticleScale"),
                    PerParticleOffsetU = PerParticleMode(controls, uv, "m_nPerParticleOffsetU"),
                    PerParticleOffsetV = PerParticleMode(controls, uv, "m_nPerParticleOffsetV"),
                    PerParticleRotation = PerParticleMode(controls, uv, "m_nPerParticleRotation"),
                    PerParticleZoom = zooms ? PerParticleMode(controls, uv, "m_nPerParticleZoom") : default,
                    PerParticleDistortion = distorts ? PerParticleMode(controls, uv, "m_nPerParticleDistortion") : default,
                    PerParticleBlend = PerParticleMode(controls, uv, "m_nPerParticleBlend"),
                });
            }

            if (parsed.Count == 0)
            {
                parsed.Add(new ParticleTextureLayer(rendererContext.MaterialLoader.GetTexture(defaultTextureName, baseSrgbRead, streaming: true)));

                // Without a colour layer the motion vectors have no texture size to scale by, so they push nothing
                motionVectors = null;
            }
            else if (parsed.Count > 3)
            {
                // A chain of more than three colour layers draws without its motion vectors
                motionVectors = null;
            }

            return ([.. parsed], motionVectors, firstTextureName);
        }

        /// <summary>A per particle mode of the layer's controls, or none when the layer has no controls.</summary>
        private static SpriteCardPerParticleScale PerParticleMode(KVObject? controls, ParticleDefinitionParser uv, string key)
            => controls == null
                ? SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_NONE
                : uv.Enum(key, SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_NONE);

        private static string? GetTextureName(ParticleDefinitionParser textureInput)
        {
            var name = textureInput.Data.ContainsKey("m_hTexture")
                ? textureInput.Data.GetStringProperty("m_hTexture")
                : null;

            return string.IsNullOrEmpty(name) ? null : name;
        }

        private static (float Position, Color32 Color)[] ParseGradientStops(ParticleDefinitionParser textureInput)
        {
            var gradient = textureInput.Data.GetSubCollection("m_Gradient");

            if (gradient == null)
            {
                return [];
            }

            var stops = textureInput.Nested(gradient).Array("m_Stops");
            var parsed = new (float Position, Color32 Color)[stops.Length];

            for (var i = 0; i < stops.Length; i++)
            {
                // The colour is stored as bytes, and taken as they are; a round trip through 0-1 floats
                // could land a channel just under its value and truncate it a step down
                var color = stops[i].Data.TryGetValue("m_Color", out var colorValue)
                    ? colorValue.ToVector3()
                    : new Vector3(255f);

                parsed[i] = (stops[i].Float("m_flPosition", 0f), new Color32((byte)color.X, (byte)color.Y, (byte)color.Z));
            }

            return parsed;
        }
    }
}
