using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// A renderer that draws with the spritecard shader: textured cards, trails and ropes that can be
    /// lit, feathered against the scene, alpha tested, refract the scene behind them and animate
    /// through a sprite sheet.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CBaseRendererSource2">CBaseRendererSource2</seealso>
    internal abstract class ParticleSpritecardRenderer : ParticleFunctionRenderer
    {
        private const string ShaderName = "particle_spritecard";

        /// <summary>The lifetime an animation fitted to it divides by at least, so a particle without one stays finite.</summary>
        private const float MinimumAnimationLifetime = 0.0001f;

        /// <summary>The largest share of a ribbon's direction that may lie along world Z for Z to serve as its fallback side axis.</summary>
        private const float MaxRibbonVerticalShare = 0.999f;

        /// <summary>Multiplies the luminance-weighted blend the output alpha is scaled by.</summary>
        protected INumberProvider OverbrightFactor { get; } = new LiteralNumberProvider(1f);

        /// <summary>Scales the scene light a particle is lit by.</summary>
        protected INumberProvider DiffuseAmount { get; } = new LiteralNumberProvider(1f);

        /// <summary>Added on top of the scene light, unlit.</summary>
        protected INumberProvider SelfIllumAmount { get; } = new LiteralNumberProvider(0f);

        /// <summary>Fraction by which the sampled colour is pulled toward its own luminance.</summary>
        protected INumberProvider Desaturation { get; } = new LiteralNumberProvider(0f);

        /// <summary>
        /// The source alpha values that map to 0 and 1. Both are always passed to the shader, which
        /// remaps whenever the first is below the second, so the remap is live by default.
        /// </summary>
        protected INumberProvider SourceAlphaValueToMapToZero { get; } = new LiteralNumberProvider(0f);

        /// <inheritdoc cref="SourceAlphaValueToMapToZero"/>
        protected INumberProvider SourceAlphaValueToMapToOne { get; } = new LiteralNumberProvider(1f);

        /// <summary>Whether the vertex colour is decoded from gamma space in the shader.</summary>
        protected bool GammaCorrectVertexColors { get; } = true;

        /// <summary>
        /// Whether sheet frame blending keeps the brighter of the two frames per channel. Shaders that
        /// switch features at run time never take this path.
        /// </summary>
        protected bool MaxLuminanceFrameBlend { get; }

        /// <summary>
        /// Whether the lit colour is clamped to 1 before it blends. Shaders that switch features at run
        /// time never clamp it.
        /// </summary>
        private bool SaturateColorPreAlphaBlend { get; } = true;

        /// <summary>
        /// Whether the blend output takes the magnitude of the blend mode's alpha factor and leaves the
        /// colour of the blend-add modes unpremultiplied, as shaders that switch features at run time do.
        /// </summary>
        private bool UnsignedBlendAlpha { get; }

        /// <summary>Additive self-colour laid over the alpha blend; uploaded as 1 + amount.</summary>
        protected INumberProvider AddSelfAmount { get; } = new LiteralNumberProvider(0f);

        /// <summary>Shifts the drawn quad within its own plane, in radii.</summary>
        protected INumberProvider CenterXOffset { get; } = new LiteralNumberProvider(0f);

        /// <inheritdoc cref="CenterXOffset"/>
        protected INumberProvider CenterYOffset { get; } = new LiteralNumberProvider(0f);

        /// <summary>
        /// Distance along the view direction the depth is re-taken from, in world units, leaving the
        /// drawn position alone. Pulls a card in front of geometry it would otherwise intersect.
        /// </summary>
        protected INumberProvider DepthBias { get; } = new LiteralNumberProvider(0f);

        // ON_OPTIONAL and ON_REQUIRED currently treated the same
        private ParticleDepthFeatheringMode FeatheringMode { get; }

        /// <summary>How far a fragment stands off the opaque scene before it draws at full strength.</summary>
        private INumberProvider FeatheringMinDist { get; } = new LiteralNumberProvider(0f);

        /// <inheritdoc cref="FeatheringMinDist"/>
        private INumberProvider FeatheringMaxDist { get; } = new LiteralNumberProvider(15f);

        /// <summary>Widens the fade by how transparent and how off-centre a fragment already is.</summary>
        private INumberProvider FeatheringFilter { get; } = new LiteralNumberProvider(0f);

        /// <inheritdoc/>
        public override bool WantsSceneDepth => FeatheringMode != ParticleDepthFeatheringMode.PARTICLE_DEPTH_FEATHERING_OFF;

        /// <summary>
        /// Whether this renderer draws the opaque scene behind it, displaced by the red and green of its
        /// texture, in place of the texture's colour.
        /// </summary>
        private bool Refract { get; }

        /// <summary>How far the texture displaces the scene, in screen coordinates at full texture alpha.</summary>
        private INumberProvider RefractAmount { get; } = new LiteralNumberProvider(1f);

        /// <summary>The radius of the blur over the displaced scene, in pixels, at most 4.</summary>
        private int RefractBlurRadius { get; }

        private BlurFilterType RefractBlurType { get; }

        /// <summary>Whether the refraction draws opaque wherever the texture has any alpha.</summary>
        private bool RefractSolid { get; }

        /// <inheritdoc/>
        public override bool WantsSceneColor => Refract && OutputIsColor;

        /// <summary>Whether the textures this renderer draws are colour, rather than data such as a refraction offset.</summary>
        protected bool TexturesAreColor => OutputIsColor && !Refract;

        /// <summary>Whether the particles draw over everything, without a depth test.</summary>
        private bool DisableZBuffering { get; }

        /// <summary>Whether the particles draw only where geometry is in front of them.</summary>
        private bool ReverseZBuffering { get; }

        /// <summary>Whether the particles are fogged.</summary>
        private bool Fogged { get; }

        /// <summary>The per particle value whose complement is the alpha test threshold.</summary>
        private SpriteCardPerParticleScale PerParticleAlphaReference { get; }

        /// <summary>The per particle value giving the width of the alpha band that passes the test.</summary>
        private SpriteCardPerParticleScale PerParticleAlphaRefWindow { get; }

        private ParticleAlphaReferenceType AlphaReferenceType { get; }

        /// <summary>How far past the threshold the alpha test fades in, in texture alpha.</summary>
        private INumberProvider AlphaReferenceSoftness { get; } = new LiteralNumberProvider(0f);

        /// <summary>Whether the particles are alpha tested against a per particle threshold.</summary>
        private bool AlphaTested => PerParticleAlphaReference != SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_NONE;

        /// <summary>Where scene lighting is sampled.</summary>
        protected ParticleLightingQuality LightingMode { get; } = ParticleLightingQuality.PARTICLE_LIGHTING_PER_PARTICLE;

        /// <summary>How the drawn colour composites over what is behind it.</summary>
        protected ParticleBlendMode BlendMode { get; } = ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_ALPHA;

        /// <summary>Whether consecutive sheet frames cross-fade rather than step from one to the next.</summary>
        private bool BlendFrames { get; } = true;

        /// <summary>How fast the sheet animates: passes through the sequence per second, or frames per second when animating in FPS.</summary>
        private float AnimationRate { get; } = 0.1f;

        /// <summary>What the sheet animation is timed by.</summary>
        protected ParticleAnimationType AnimationType { get; init; } = ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;

        /// <summary>Whether <see cref="AnimationRate"/> counts frames per second rather than passes through the sequence.</summary>
        private bool AnimateInFps { get; }

        /// <summary>
        /// The absolute cosine between the facing axis and the view direction at which the view angle
        /// fade starts and ends. The defaults lie past 1, which no cosine reaches, so nothing fades.
        /// </summary>
        private float StartFadeDot { get; } = 1f;

        /// <inheritdoc cref="StartFadeDot"/>
        private float EndFadeDot { get; } = 2f;

        /// <summary>The program every draw of this renderer uses.</summary>
        protected Shader Shader { get; }

        /// <summary>The texture layers, in composite order; the first is the base texture.</summary>
        protected ParticleTextureLayer[] Layers { get; }

        /// <summary>The vertex array the draws read through.</summary>
        protected int VertexArrayHandle { get; private set; }

        /// <summary>How many quads the vertex buffer holds since the last <see cref="UpdateBuffers"/>.</summary>
        protected int QuadCount { get; private set; }

        private readonly string bufferLabel;
        private int vertexBufferHandle;
        private int vertexStride;

        /// <summary>Reads the shared settings, loads the layer textures and the shader.</summary>
        /// <param name="parse">The renderer's definition.</param>
        /// <param name="rendererContext">Loads the shader and the layer textures.</param>
        /// <param name="scene">The scene the renderer is lit by.</param>
        /// <param name="defaultTextureName">The texture of a layer that names none.</param>
        /// <param name="instancing">The <c>S_PARTICLE_INSTANCED</c> combo: 0 for indexed quads, otherwise the shape each instance expands to.</param>
        protected ParticleSpritecardRenderer(ParticleDefinitionParser parse, RendererContext rendererContext, Scene scene, string defaultTextureName, byte instancing)
            : base(parse, scene)
        {
            LightingMode = parse.Enum("m_nLightingMode", LightingMode);
            OverbrightFactor = parse.NumberProvider("m_flOverbrightFactor", OverbrightFactor);
            AddSelfAmount = parse.NumberProvider("m_flAddSelfAmount", AddSelfAmount);
            DepthBias = parse.NumberProvider("m_flDepthBias", DepthBias);
            DiffuseAmount = parse.NumberProvider("m_flDiffuseAmount", DiffuseAmount);
            SelfIllumAmount = parse.NumberProvider("m_flSelfIllumAmount", SelfIllumAmount);
            Desaturation = parse.NumberProvider("m_flDesaturation", Desaturation);
            SourceAlphaValueToMapToZero = parse.NumberProvider("m_flSourceAlphaValueToMapToZero", SourceAlphaValueToMapToZero);
            SourceAlphaValueToMapToOne = parse.NumberProvider("m_flSourceAlphaValueToMapToOne", SourceAlphaValueToMapToOne);
            GammaCorrectVertexColors = parse.Boolean("m_bGammaCorrectVertexColors", GammaCorrectVertexColors);
            MaxLuminanceFrameBlend = parse.Boolean("m_bMaxLuminanceBlendingSequence0", MaxLuminanceFrameBlend);
            CenterXOffset = parse.NumberProvider("m_flCenterXOffset", CenterXOffset);
            CenterYOffset = parse.NumberProvider("m_flCenterYOffset", CenterYOffset);
            FeatheringMode = parse.Enum("m_nFeatheringMode", FeatheringMode);
            FeatheringMinDist = parse.NumberProvider("m_flFeatheringMinDist", FeatheringMinDist);
            FeatheringMaxDist = parse.NumberProvider("m_flFeatheringMaxDist", FeatheringMaxDist);
            FeatheringFilter = parse.NumberProvider("m_flFeatheringFilter", FeatheringFilter);
            Refract = parse.Boolean("m_bRefract", Refract);
            RefractAmount = parse.NumberProvider("m_flRefractAmount", RefractAmount);
            RefractBlurRadius = Math.Clamp(parse.Int32("m_nRefractBlurRadius", RefractBlurRadius), 0, 4);
            RefractBlurType = parse.Enum("m_nRefractBlurType", RefractBlurType);
            RefractSolid = parse.Boolean("m_bRefractSolid", RefractSolid);
            DisableZBuffering = parse.Boolean("m_bDisableZBuffering", DisableZBuffering);
            ReverseZBuffering = parse.Boolean("m_bReverseZBuffering", ReverseZBuffering);
            PerParticleAlphaReference = parse.Enum("m_nPerParticleAlphaReference", PerParticleAlphaReference);
            PerParticleAlphaRefWindow = parse.Enum("m_nPerParticleAlphaRefWindow", PerParticleAlphaRefWindow);
            AlphaReferenceType = parse.Enum("m_nAlphaReferenceType", AlphaReferenceType);
            AlphaReferenceSoftness = parse.NumberProvider("m_flAlphaReferenceSoftness", AlphaReferenceSoftness);
            BlendMode = parse.Enum("m_nOutputBlendMode", BlendMode);
            BlendFrames = parse.Boolean("m_bBlendFramesSeq0", BlendFrames);
            AnimationRate = parse.Float("m_flAnimationRate", AnimationRate);
            AnimationType = parse.Enum("m_nAnimationType", AnimationType);
            AnimateInFps = parse.Boolean("m_bAnimateInFPS", AnimateInFps);
            StartFadeDot = parse.Float("m_flStartFadeDot", StartFadeDot);
            EndFadeDot = parse.Float("m_flEndFadeDot", EndFadeDot);

            SaturateColorPreAlphaBlend = parse.Boolean("m_bSaturateColorPreAlphaBlend", SaturateColorPreAlphaBlend);

            var gameInfo = scene.RendererContext.FileLoader.GameInfo;

            Fogged = parse.Enum("m_nFogType", ParticleFogType.PARTICLE_FOG_GAME_DEFAULT) switch
            {
                ParticleFogType.PARTICLE_FOG_ENABLED => true,
                ParticleFogType.PARTICLE_FOG_GAME_DEFAULT => gameInfo.ParticlesFoggedByDefault,
                _ => false,
            };

            if (gameInfo.ParticleShaderFeatureBranching)
            {
                MaxLuminanceFrameBlend = false;
                SaturateColorPreAlphaBlend = false;
                UnsignedBlendAlpha = true;
            }

            GammaCorrectVertexColors &= OutputIsColor;
            SaturateColorPreAlphaBlend &= OutputIsColor;

            (Layers, var textureName) = ParticleTextureLayer.Build(parse, rendererContext, defaultTextureName, srgbRead: TexturesAreColor);
            bufferLabel = $"{GetType().Name}: {System.IO.Path.GetFileName(textureName)}";

            var shaderArguments = CreateShaderArguments();
            shaderArguments["S_TEXTURE_LAYERS"] = (byte)(Layers.Length - 1);

            if (instancing != 0)
            {
                shaderArguments["S_PARTICLE_INSTANCED"] = instancing;
            }

            if (WantsSceneColor)
            {
                shaderArguments["S_REFRACT"] = 1;
            }

            Shader = rendererContext.ShaderLoader.LoadShader(ShaderName, shaderArguments);
        }

        /// <inheritdoc/>
        public override Texture.SpritesheetData? SpriteSheet => ParticleTextureLayer.FindSpriteSheet(Layers);

        /// <inheritdoc/>
        /// <remarks>Replaces the base texture. Layers composited over it keep their own textures, channels and blend settings.</remarks>
        public override void SetTextureOverride(RenderTexture texture)
        {
            Layers[0].Texture = texture;
        }

        /// <inheritdoc/>
        public override IEnumerable<string> GetSupportedRenderModes() => Shader.RenderModes;

        /// <summary>
        /// Creates the vertex buffer and the vertex array that reads it through <paramref name="layout"/>,
        /// with <paramref name="indexBuffer"/> as its indices, or none for 0, and advancing every attribute
        /// once per instance when <paramref name="instanceDivisor"/> is 1.
        /// </summary>
        protected void CreateVertexArray(VertexInputLayout layout, int indexBuffer = 0, int instanceDivisor = 0)
        {
            vertexBufferHandle = GraphicsDevice.CreateBuffer(bufferLabel);
            vertexStride = layout.Stride;
            VertexArrayHandle = layout.CreateVertexArray(bufferLabel, vertexBufferHandle, indexBuffer, instanceDivisor);
        }

        /// <summary>The size of one vertex, or of one instance's attributes, in floats.</summary>
        protected int VertexFloats => vertexStride / sizeof(float);

        /// <summary>Uploads the first <paramref name="vertexCount"/> vertices of <paramref name="data"/> to the vertex buffer.</summary>
        protected void UploadVertices(byte[] data, int vertexCount)
        {
            GL.NamedBufferData(vertexBufferHandle, vertexCount * vertexStride, data, BufferUsageHint.DynamicDraw);
        }

        /// <summary>
        /// The layout of an instance whose own attributes, <paramref name="elements"/> in buffer order,
        /// fill a <typeparamref name="TInstance"/>, followed by the two sheet rects of each layer past the
        /// first. Carries only the layers this renderer has, but allocates locations over every name the
        /// shader declares.
        /// </summary>
        protected VertexInputLayout BuildInstanceLayout<TInstance>(List<VertexAttribute> elements) where TInstance : unmanaged
        {
            for (var layer = 1; layer < Layers.Length; layer++)
            {
                elements.Add(new VertexAttribute(SpritecardVertex.LayerUvNames[layer - 1], DXGI_FORMAT.R32G32B32A32_FLOAT));
                elements.Add(new VertexAttribute(SpritecardVertex.LayerUvNextNames[layer - 1], DXGI_FORMAT.R32G32B32A32_FLOAT));
            }

            var stride = Unsafe.SizeOf<TInstance>() + (2 * Unsafe.SizeOf<Vector4>() * (Layers.Length - 1));

            return new VertexInputLayout(stride, SpritecardVertex.DeclaredAttributeNames, elements.ToArray());
        }

        /// <summary>
        /// Writes the attributes of the instance at <paramref name="instanceIndex"/> and returns the space
        /// after them, which holds two rects for each layer past the first.
        /// </summary>
        protected Span<Vector4> WriteInstance<TInstance>(Span<float> instances, int instanceIndex, in TInstance instance) where TInstance : unmanaged
        {
            var baseFloats = Unsafe.SizeOf<TInstance>() / sizeof(float);
            var record = instances.Slice(instanceIndex * VertexFloats, VertexFloats);

            MemoryMarshal.Cast<float, TInstance>(record[..baseFloats])[0] = instance;

            return MemoryMarshal.Cast<float, Vector4>(record[baseFloats..]);
        }

        /// <summary>Writes the rects of <paramref name="layer"/>, past the first, into the space <see cref="WriteInstance"/> returned.</summary>
        protected static void WriteLayerRects(Span<Vector4> layerRects, int layer, in SheetRects rects)
        {
            layerRects[(layer - 1) * 2] = Rect(rects.Min, rects.Max);
            layerRects[((layer - 1) * 2) + 1] = Rect(rects.NextMin, rects.NextMax);
        }

        /// <inheritdoc/>
        /// <remarks>Particles the renderer culls or fades out are not written, so this can hold fewer quads than there are particles.</remarks>
        public sealed override void UpdateBuffers(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            QuadCount = particles.Count == 0 ? 0 : UpdateVertices(particles, systemState, camera);
        }

        /// <summary>Fills and uploads the vertex buffer, returning how many quads it holds.</summary>
        protected abstract int UpdateVertices(ParticleCollection particles, ParticleSystemState systemState, Camera camera);

        /// <inheritdoc/>
        public override void Render(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            if (QuadCount == 0)
            {
                return;
            }

            using var _ = SpritecardStateScope(BlendMode, Pass == RenderPass.Opaque);

            Shader.Use();
            VertexArray.Bind(VertexArrayHandle, Shader);

            ParticleTextureLayer.Bind(Shader, Layers, systemState);
            SetSharedUniforms(systemState);
            Shader.SetUniform1("uBlendFrames", BlendFrames);

            // Set every draw: the program is shared with every other spritecard renderer, whatever their mode.
            Shader.SetUniform1("uBlendMode", (int)BlendMode);

            SetDrawUniforms(systemState);

            PerfStats.Active.Count(Counter.ParticleDraw);
            Draw();
        }

        /// <summary>Sets the uniforms only this renderer's draws read. Turns the outline off unless overridden.</summary>
        protected virtual void SetDrawUniforms(ParticleSystemState systemState)
        {
            Shader.SetUniform1("uOutline", false);
        }

        /// <summary>Draws the quads. Generates the four corners of each instance unless overridden.</summary>
        protected virtual void Draw()
        {
            GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, QuadCount);
        }

        /// <inheritdoc/>
        public override void Delete()
        {
            VertexArray.Delete(VertexArrayHandle);
            GL.DeleteBuffer(vertexBufferHandle);
        }

        /// <summary>
        /// The remap the shader applies to the sampled alpha, as the pair the shader expects.
        /// </summary>
        protected Vector2 GetAlphaRemapRange(ParticleSystemState systemState)
            => new(SourceAlphaValueToMapToZero.NextNumber(systemState),
                SourceAlphaValueToMapToOne.NextNumber(systemState));

        /// <summary>
        /// The state a spritecard draw runs under. The translucent pass leaves blend and depth to each
        /// draw, so blending is enabled and depth writes stopped or the card renders opaque; an opaque
        /// renderer keeps the pass defaults instead. Modulate-2x scales what is behind it and so needs
        /// its own factors, lighten keeps the brighter of the card and what is behind it, and everything
        /// else composites premultiplied. Both faces are drawn, since a card or ribbon can turn either
        /// one toward the camera. The lighten blend mode keeps the depth test whatever the z buffering
        /// settings ask.
        /// </summary>
        private RenderPassScope SpritecardStateScope(ParticleBlendMode blendMode, bool opaque)
        {
            var renderState = GraphicsContext.RenderState;

            var depthSettings = blendMode != ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_LIGHTEN;
            bool? depthTest = depthSettings && DisableZBuffering ? false : null;
            RsComparison? depthFunc = depthSettings && !DisableZBuffering && ReverseZBuffering ? RsComparison.Farther : null;

            if (opaque)
            {
                return renderState.Scope(cullMode: RsCullMode.None, depthTest: depthTest, depthFunc: depthFunc);
            }

            var (srcBlend, dstBlend, blendOp) = blendMode switch
            {
                ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_MOD2X => (RsBlendMode.DestColor, RsBlendMode.SrcColor, RsBlendOp.Add),
                ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_LIGHTEN => (RsBlendMode.One, RsBlendMode.One, RsBlendOp.Max),
                _ => (RsBlendMode.One, RsBlendMode.InvSrcAlpha, RsBlendOp.Add),
            };

            return renderState.Scope(blend: true, depthWrite: false, cullMode: RsCullMode.None,
                depthTest: depthTest, depthFunc: depthFunc,
                srcBlend: srcBlend, dstBlend: dstBlend, blendOp: blendOp);
        }

        /// <summary>The per particle values <see cref="GetAlphaReference"/> reads, from <see cref="ReadAlphaReferenceInputs"/>.</summary>
        /// <param name="reference">The values of <see cref="PerParticleAlphaReference"/>, when it reads an attribute.</param>
        /// <param name="window">The values of <see cref="PerParticleAlphaRefWindow"/>, when it reads an attribute.</param>
        protected readonly ref struct AlphaReferenceInputs(ReadOnlySpan<float> reference, ReadOnlySpan<float> window)
        {
            /// <summary>The values of <see cref="PerParticleAlphaReference"/>, or none when it reads no attribute.</summary>
            public ReadOnlySpan<float> Reference { get; } = reference;

            /// <summary>The values of <see cref="PerParticleAlphaRefWindow"/>, or none when it reads no attribute.</summary>
            public ReadOnlySpan<float> Window { get; } = window;
        }

        /// <summary>
        /// Reads the attributes the alpha test of every particle in <paramref name="particles"/> takes,
        /// reusing <paramref name="ages"/> when they were already read from the same particles.
        /// </summary>
        protected AlphaReferenceInputs ReadAlphaReferenceInputs(ParticleSpan particles, ReadOnlySpan<float> ages = default)
            => AlphaTested
                ? new AlphaReferenceInputs(
                    ReadPerParticleScale(PerParticleAlphaReference, particles, ages),
                    ReadPerParticleScale(PerParticleAlphaRefWindow, particles, ages))
                : default;

        /// <summary>
        /// The threshold a particle's texture alpha is tested against and the width of the band above it
        /// that passes, in x and y, as the shader reads them.
        /// </summary>
        /// <param name="inputs">The attributes read for the particles being drawn.</param>
        /// <param name="index">The index of the particle being drawn.</param>
        /// <param name="radius">The radius it is drawn at.</param>
        /// <param name="alpha">The alpha it is drawn with.</param>
        protected Vector2 GetAlphaReference(in AlphaReferenceInputs inputs, int index, float radius, float alpha)
            => AlphaTested
                ? new Vector2(
                    1f - GetPerParticleScale(PerParticleAlphaReference, inputs.Reference, index, radius, alpha),
                    GetPerParticleScale(PerParticleAlphaRefWindow, inputs.Window, index, radius, alpha))
                : Vector2.Zero;

        /// <summary>
        /// The attribute a per particle input reads for every particle, or nothing for an input that reads
        /// none. The particle ages are read unless <paramref name="ages"/> already holds them.
        /// </summary>
        private static ReadOnlySpan<float> ReadPerParticleScale(SpriteCardPerParticleScale source, ParticleSpan particles, ReadOnlySpan<float> ages)
        {
            switch (source)
            {
                case SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_PARTICLE_AGE:
                    if (ages.IsEmpty)
                    {
                        ages = particles.ReadAges();
                    }

                    var shiftedAges = ParticleScratch.Floats(particles.Length);
                    TensorPrimitives.Add(ages, 1f, shiftedAges);
                    return shiftedAges;
                case SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_SHADER_EXTRA_DATA1:
                    return particles.ReadFloats(ParticleField.ShaderExtraData1);
                case SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_SHADER_EXTRA_DATA2:
                    return particles.ReadFloats(ParticleField.ShaderExtraData2);
                case SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_ROLL:
                    return particles.ReadFloats(ParticleField.Roll);
                case SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_YAW:
                    return particles.ReadFloats(ParticleField.Yaw);
                case SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_PITCH:
                    return particles.ReadFloats(ParticleField.Pitch);
                default:
                    return default;
            }
        }

        /// <summary>The value a per particle input reads. Animation frames and the random sources read 1.</summary>
        private static float GetPerParticleScale(SpriteCardPerParticleScale source, ReadOnlySpan<float> values, int index, float radius, float alpha) => source switch
        {
            SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_PARTICLE_AGE
                or SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_SHADER_EXTRA_DATA1
                or SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_SHADER_EXTRA_DATA2
                or SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_ROLL
                or SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_YAW
                or SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_PITCH => values[index],
            SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_PARTICLE_ALPHA => alpha,
            SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_SHADER_RADIUS => radius,
            _ => 1f,
        };

        /// <summary>
        /// Uploads the shared source 2 renderer state every particle shader reads.
        /// </summary>
        private void SetSharedUniforms(ParticleSystemState systemState)
        {
            Shader.SetUniform1("uOverbrightFactor", OverbrightFactor.NextNumber(systemState));
            Shader.SetUniform1("uAddSelfAmount", 1f + AddSelfAmount.NextNumber(systemState));
            Shader.SetUniform1("uDiffuseAmount", DiffuseAmount.NextNumber(systemState));
            Shader.SetUniform1("uSelfIllumAmount", SelfIllumAmount.NextNumber(systemState));
            Shader.SetUniform1("uDesaturation", MathUtils.Saturate(Desaturation.NextNumber(systemState)));
            Shader.SetUniform3("uHsvShift", GetHsvShift(systemState));
            Shader.SetUniform2("uAlphaRemapRange", GetAlphaRemapRange(systemState));
            Shader.SetUniform1("uGammaCorrectVertexColors", GammaCorrectVertexColors);
            Shader.SetUniform1("uDepthBias", DepthBias.NextNumber(systemState));
            Shader.SetUniform1("uMaxLuminanceFrameBlend", MaxLuminanceFrameBlend);
            Shader.SetUniform1("uSaturateColor", SaturateColorPreAlphaBlend);
            Shader.SetUniform1("uUnsignedBlendAlpha", UnsignedBlendAlpha);
            Shader.SetUniform1("uLightingMode", (int)LightingMode);
            Shader.SetUniform1("g_tSceneDepth", (int)ReservedTextureSlots.SceneDepth);

            if (LitByProbes && OwnerNode != null)
            {
                Shader.SetUniform1("uObjectIndex", OwnerNode.Id);
            }

            Shader.SetUniform2("uFeatheringRange", WantsSceneDepth
                ? new Vector2(FeatheringMinDist.NextNumber(systemState), FeatheringMaxDist.NextNumber(systemState))
                : Vector2.Zero);
            Shader.SetUniform1("uFeatheringFilter", FeatheringFilter.NextNumber(systemState));
            Shader.SetUniform1("uFogParticles", Fogged);
            Shader.SetUniform1("uAlphaReference", AlphaTested);

            if (AlphaTested)
            {
                var softness = AlphaReferenceSoftness.NextNumber(systemState);
                var windowed = PerParticleAlphaRefWindow != SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_NONE;

                Shader.SetUniform2("uAlphaReferenceOpaque", new Vector2(
                    AlphaReferenceType is ParticleAlphaReferenceType.PARTICLE_ALPHA_REFERENCE_OPAQUE_ALPHA or ParticleAlphaReferenceType.PARTICLE_ALPHA_REFERENCE_OPAQUE_OPAQUE ? 1f : 0f,
                    AlphaReferenceType is ParticleAlphaReferenceType.PARTICLE_ALPHA_REFERENCE_ALPHA_OPAQUE or ParticleAlphaReferenceType.PARTICLE_ALPHA_REFERENCE_OPAQUE_OPAQUE ? 1f : 0f));
                Shader.SetUniform2("uAlphaReferenceSoftness", windowed
                    ? new Vector2(softness * 0.5f)
                    : new Vector2(0f, softness));
            }

            if (Refract)
            {
                Shader.SetUniform1("uRefractAmount", RefractAmount.NextNumber(systemState));
                Shader.SetUniform1("uRefractBlurRadius", RefractBlurRadius);
                Shader.SetUniform1("uRefractBlurBox", RefractBlurType == BlurFilterType.BLURFILTER_BOX);
                Shader.SetUniform1("uRefractSolid", RefractSolid);
            }
        }

        /// <summary>The attributes that choose the sheet sequence and frame of every particle in a span.</summary>
        protected readonly ref struct SheetInputs
        {
            /// <summary>Reads the attributes of every particle in <paramref name="particles"/>.</summary>
            public SheetInputs(ParticleSpan particles)
            {
                Ages = particles.ReadAges();
                Lifetimes = particles.ReadFloats(ParticleField.LifeDuration);
                ManualAnimationFrames = particles.ReadFloats(ParticleField.ManualAnimationFrame);
                SequenceNumbers = particles.ReadFloats(ParticleField.SequenceNumber);
                SecondSequenceNumbers = particles.ReadFloats(ParticleField.SecondSequenceNumber);
            }

            /// <summary>How long each particle has lived, in seconds.</summary>
            public ReadOnlySpan<float> Ages { get; }

            /// <summary>How long each particle lives, in seconds.</summary>
            public ReadOnlySpan<float> Lifetimes { get; }

            /// <summary>The passes through the sequence a manually animated particle has made.</summary>
            public ReadOnlySpan<float> ManualAnimationFrames { get; }

            /// <summary>The sequence the base layer plays.</summary>
            public ReadOnlySpan<float> SequenceNumbers { get; }

            /// <summary>The sequence the layers past the first play.</summary>
            public ReadOnlySpan<float> SecondSequenceNumbers { get; }
        }

        /// <summary>A sheet frame rectangle as the shader reads it: minimum in xy, maximum in zw.</summary>
        /// <param name="min">Lower corner.</param>
        /// <param name="max">Upper corner.</param>
        protected static Vector4 Rect(Vector2 min, Vector2 max) => new(min.X, min.Y, max.X, max.Y);

        /// <summary>The atlas rects a layer draws its current and next frame from.</summary>
        /// <param name="Min">Lower corner of the current frame.</param>
        /// <param name="Max">Upper corner of the current frame.</param>
        /// <param name="NextMin">Lower corner of the next frame.</param>
        /// <param name="NextMax">Upper corner of the next frame.</param>
        protected readonly record struct SheetRects(Vector2 Min, Vector2 Max, Vector2 NextMin, Vector2 NextMax)
        {
            /// <summary>The whole texture for both frames, for a layer that shows no sheet.</summary>
            public static SheetRects Whole { get; } = new(Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.One);
        }

        /// <summary>The two sheet frames a particle sits between and how far it has crossed from the first.</summary>
        /// <param name="Current">The frame it is leaving.</param>
        /// <param name="Next">The frame it is crossing to.</param>
        /// <param name="Blend">How far it has crossed, 0 to 1.</param>
        protected readonly record struct SheetFrames(
            Texture.SpritesheetData.Sequence.Frame Current,
            Texture.SpritesheetData.Sequence.Frame Next,
            float Blend);

        /// <summary>
        /// The frames the sheet of <paramref name="layer"/> shows for the particle at <paramref name="index"/>
        /// playing sequence <paramref name="sequenceNumber"/>, or null when the layer carries no sheet or the
        /// sequence no frames. A sequence number outside the sheet plays sequence 0.
        /// </summary>
        protected SheetFrames? GetSheetFrames(int layer, int sequenceNumber, in SheetInputs inputs, int index)
        {
            var spriteSheetData = Layers[layer].Texture.SpriteSheetData;

            if (spriteSheetData == null || spriteSheetData.Sequences.Length == 0)
            {
                return null;
            }

            var sequence = spriteSheetData.Sequences[(uint)sequenceNumber < (uint)spriteSheetData.Sequences.Length ? sequenceNumber : 0];

            if (sequence.Frames.Length == 0)
            {
                return null;
            }

            var (frame, nextFrame, blend) = GetSheetFrame(inputs, index, sequence);

            return new SheetFrames(sequence.Frames[frame], sequence.Frames[nextFrame], blend);
        }

        /// <summary>The uncropped atlas rects of the first image of both frames.</summary>
        protected static SheetRects UncroppedRects(in SheetFrames frames)
        {
            var current = frames.Current.Images[0];
            var next = frames.Next.Images[0];

            return new SheetRects(current.UncroppedMin, current.UncroppedMax, next.UncroppedMin, next.UncroppedMax);
        }

        /// <summary>
        /// The two sheet frames a particle sits between and how far it has crossed from the first to
        /// the second. The particle's age and animation type give the playback position, which
        /// <see cref="Texture.SpritesheetData.Sequence.GetFrameAtPosition"/> then resolves to frames.
        /// </summary>
        private (int Frame, int NextFrame, float Blend) GetSheetFrame(in SheetInputs inputs, int index, Texture.SpritesheetData.Sequence sequence)
        {
            var frameCount = sequence.Frames.Length;

            if (frameCount < 2)
            {
                return (0, 0, 0f);
            }

            var totalTime = sequence.EffectiveTotalTime;

            float passes;

            if (AnimationType == ParticleAnimationType.ANIMATION_TYPE_MANUAL_FRAMES)
            {
                passes = inputs.ManualAnimationFrames[index];
            }
            else
            {
                // The animation time is chosen by type first; animating in FPS only changes how the
                // rate is interpreted afterwards, it does not replace the type
                var animationTime = AnimationType switch
                {
                    ParticleAnimationType.ANIMATION_TYPE_FIT_LIFETIME => inputs.Ages[index] / Math.Max(MinimumAnimationLifetime, inputs.Lifetimes[index]),
                    _ => inputs.Ages[index],
                };

                passes = AnimateInFps
                    ? animationTime * AnimationRate / totalTime
                    : animationTime * AnimationRate;
            }

            var position = totalTime * (sequence.Clamp
                ? MathUtils.Saturate(passes)
                : MathUtils.Fract(passes));

            return sequence.GetFrameAtPosition(position);
        }

        /// <summary>Whether the view angle fade can take anything away, which needs its start below 1 and its end past the start.</summary>
        protected bool FadesByViewAngle => StartFadeDot < 1f && EndFadeDot > StartFadeDot;

        /// <summary>
        /// How much of a particle at <paramref name="position"/> is left by the view angle fade, which
        /// fades it out as <paramref name="facingAxis"/> lines up with the direction to the camera.
        /// </summary>
        protected float ViewAngleFade(Vector3 facingAxis, Vector3 position, Vector3 cameraPosition)
        {
            var toCamera = cameraPosition - position;

            if (toCamera.LengthSquared() <= ParticleMath.MinimumLengthSquared)
            {
                return 1f;
            }

            var facing = MathF.Abs(Vector3.Dot(MathUtils.SafeNormalize(facingAxis), Vector3.Normalize(toCamera)));

            return 1f - MathUtils.Smoothstep(StartFadeDot, EndFadeDot, facing);
        }

        /// <summary>
        /// The unit axis across a ribbon that runs along the unit <paramref name="direction"/> and lies
        /// flat in the plane facing <paramref name="planeNormal"/>. When the two are parallel the ribbon
        /// turns about world Z instead, or about world X while it runs nearly vertical.
        /// </summary>
        protected static Vector3 RibbonWidthAxis(Vector3 direction, Vector3 planeNormal)
        {
            var widthAxis = Vector3.Cross(direction, planeNormal);

            if (widthAxis.LengthSquared() > ParticleMath.MinimumLengthSquared)
            {
                return Vector3.Normalize(widthAxis);
            }

            var reference = MathF.Abs(direction.Z) < MaxRibbonVerticalShare ? Vector3.UnitZ : Vector3.UnitX;

            return MathUtils.SafeNormalize(Vector3.Cross(direction, reference), Vector3.UnitX);
        }
    }
}
