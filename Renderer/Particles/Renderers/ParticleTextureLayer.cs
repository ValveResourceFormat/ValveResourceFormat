using System.Linq;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>One entry of m_vecTexturesInput: a texture plus how it folds into the layers below it.</summary>
    internal sealed class ParticleTextureLayer(RenderTexture texture)
    {
        /// <summary>The most layers any spritecard renderer composites.</summary>
        public const int MaxLayers = 5;

        // Setting a GLSL array element needs its literal name, and this uploads every draw, so the
        // names are built once rather than formatted per layer per frame.
        private static readonly string[] TextureNames = ["uTexture", "uTextureLayer1", "uTextureLayer2", "uTextureLayer3", "uTextureLayer4"];
        private static readonly string[] ChannelNames = ["uLayerChannels[0]", "uLayerChannels[1]", "uLayerChannels[2]", "uLayerChannels[3]", "uLayerChannels[4]"];
        private static readonly string[] BlendModeNames = ["uLayerBlendMode[0]", "uLayerBlendMode[1]", "uLayerBlendMode[2]", "uLayerBlendMode[3]", "uLayerBlendMode[4]"];
        private static readonly string[] BlendNames = ["uLayerBlend[0]", "uLayerBlend[1]", "uLayerBlend[2]", "uLayerBlend[3]", "uLayerBlend[4]"];
        private static readonly string[] EffectModeNames = ["uLayerEffectMode[0]", "uLayerEffectMode[1]", "uLayerEffectMode[2]", "uLayerEffectMode[3]", "uLayerEffectMode[4]"];
        private static readonly string[] DistortionNames = ["uLayerDistortion[0]", "uLayerDistortion[1]", "uLayerDistortion[2]", "uLayerDistortion[3]", "uLayerDistortion[4]"];
        private static readonly string[] ZoomScaleNames = ["uLayerZoomScale[0]", "uLayerZoomScale[1]", "uLayerZoomScale[2]", "uLayerZoomScale[3]", "uLayerZoomScale[4]"];

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

        /// <summary>m_flFinalTextureUVRotation: radians this layer's texture turns about the card centre.</summary>
        public INumberProvider Rotation { get; init; } = Zero;

        /// <summary>m_bClampUVs: whether this layer stops repeating outside its own range.</summary>
        public bool ClampUVs { get; init; }

        /// <summary>One layer's resolved coordinate transform, in card space.</summary>
        public readonly record struct UvTransform(Vector2 Scale, Vector2 Offset, float Rotation, bool Clamp)
        {
            /// <summary>The transform that leaves a coordinate where it is.</summary>
            public static UvTransform Identity { get; } = new(Vector2.One, Vector2.Zero, 0f, false);
        }

        /// <summary>Binds the chain's textures and describes every layer to the shader.</summary>
        public static void Bind(Shader shader, ParticleTextureLayer[] layers, ParticleSystemState systemState)
        {
            shader.SetUniform1("uLayerCount", layers.Length);

            for (var layer = 0; layer < layers.Length; layer++)
            {
                var source = layers[layer];

                shader.SetTexture(RenderMaterial.TextureUnitStart + layer, TextureNames[layer], source.Texture);
                shader.SetUniform1(ChannelNames[layer], (int)source.Channels);
                shader.SetUniform1(BlendModeNames[layer], (int)source.BlendMode);
                shader.SetUniform1(BlendNames[layer], source.Blend.NextNumber(systemState));
                shader.SetUniform1(EffectModeNames[layer], (int)source.EffectMode);
                shader.SetUniform1(DistortionNames[layer], source.Distortion.NextNumber(systemState));
                shader.SetUniform1(ZoomScaleNames[layer], source.ZoomScale.NextNumber(systemState));
            }
        }

        /// <summary>Resolves every layer's transform for the frame, in layer order.</summary>
        public static void ResolveUvTransforms(ParticleTextureLayer[] layers, ParticleSystemState systemState, Span<UvTransform> transforms)
        {
            for (var layer = 0; layer < layers.Length; layer++)
            {
                transforms[layer] = layers[layer].ResolveUvTransform(systemState);
            }
        }

        /// <summary>Resolves this layer's transform for the frame.</summary>
        public UvTransform ResolveUvTransform(ParticleSystemState systemState) => new(
            new Vector2(ScaleU.NextNumber(systemState), ScaleV.NextNumber(systemState)),
            new Vector2(OffsetU.NextNumber(systemState), OffsetV.NextNumber(systemState)),
            Rotation.NextNumber(systemState),
            ClampUVs);

        /// <summary>
        /// Places one card corner in the layer's texture. The card coordinate turns about the card centre,
        /// is divided by the scale, and lands at the wrapped offset; the result then maps into whichever
        /// sheet frame rect the layer is showing.
        /// </summary>
        public static Vector2 PlaceCorner(Vector2 cardUv, in UvTransform transform, Vector2 rectMin, Vector2 rectMax)
        {
            var centred = cardUv - new Vector2(0.5f);
            var (sin, cos) = MathF.SinCos(transform.Rotation);

            var rotated = new Vector2(
                (centred.X * cos) - (centred.Y * sin),
                (centred.X * sin) + (centred.Y * cos));

            var origin = transform.Offset + new Vector2(0.5f);
            var wrapped = new Vector2(origin.X - MathF.Floor(origin.X), origin.Y - MathF.Floor(origin.Y));

            var placed = (rotated / transform.Scale) + wrapped;

            if (transform.Clamp)
            {
                placed = Vector2.Clamp(placed, Vector2.Zero, Vector2.One);
            }

            return rectMin + (placed * (rectMax - rectMin));
        }

        /// <summary>
        /// Reads m_vecTexturesInput into the layer chain a spritecard renderer composites, falling back to
        /// a single <paramref name="defaultTextureName"/> layer when the definition supplies none.
        /// </summary>
        /// <param name="parse">The renderer's definition.</param>
        /// <param name="rendererContext">Loads the layer textures.</param>
        /// <param name="defaultTextureName">Texture for a layer that names none, and for the fallback layer.</param>
        /// <returns>The layers in composite order, and the name of the first texture loaded.</returns>
        public static (ParticleTextureLayer[] Layers, string? FirstTextureName) Build(
            ParticleDefinitionParser parse, RendererContext rendererContext, string defaultTextureName)
        {
            string? firstTextureName = null;
            var parsed = new List<ParticleTextureLayer>();

            foreach (var textureInput in parse.Array("m_vecTexturesInput"))
            {
                if (!textureInput.Boolean("m_bEnabled", true))
                {
                    continue;
                }

                // Normal maps and motion vector sheets are not colour: compositing them into the chain
                // would tint the card with a tangent-space basis or a flow field.
                var textureType = textureInput.Enum("m_nTextureType", SpriteCardTextureType.SPRITECARD_TEXTURE_DIFFUSE);

                if (textureType is SpriteCardTextureType.SPRITECARD_TEXTURE_NORMALMAP
                    or SpriteCardTextureType.SPRITECARD_TEXTURE_ANIMMOTIONVEC)
                {
                    continue;
                }

                if (parsed.Count == MaxLayers)
                {
                    break;
                }

                RenderTexture layerTexture;

                // A gradient layer synthesizes its ramp from m_Gradient rather than loading a texture.
                if (textureInput.Boolean("m_bReplaceTextureWithGradient", false))
                {
                    layerTexture = MaterialLoader.GenerateGradientTexture(ParseGradientStops(textureInput));
                }
                else
                {
                    var layerTextureName = textureInput.Data.ContainsKey("m_hTexture")
                        ? textureInput.Data.GetStringProperty("m_hTexture")
                        : null;

                    if (string.IsNullOrEmpty(layerTextureName))
                    {
                        layerTextureName = defaultTextureName;
                    }

                    firstTextureName ??= layerTextureName;
                    layerTexture = rendererContext.MaterialLoader.GetTexture(layerTextureName, srgbRead: true);
                }

                var controls = textureInput.Data.GetSubCollection("m_TextureControls");
                var uv = controls == null ? textureInput : textureInput.Nested(controls);

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
                    ClampUVs = controls != null && uv.Boolean("m_bClampUVs", false),
                });
            }

            if (parsed.Count == 0)
            {
                parsed.Add(new ParticleTextureLayer(rendererContext.MaterialLoader.GetTexture(defaultTextureName, srgbRead: true)));
            }

            return ([.. parsed], firstTextureName);
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
                var color = stops[i].Color24("m_Color", Vector3.One) * 255f;
                parsed[i] = (stops[i].Float("m_flPosition", 0f), new Color32((byte)color.X, (byte)color.Y, (byte)color.Z));
            }

            return parsed;
        }
    }
}
