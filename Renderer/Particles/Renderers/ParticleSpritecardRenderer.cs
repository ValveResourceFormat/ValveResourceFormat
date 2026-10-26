using System.Diagnostics.CodeAnalysis;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
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

        /// <summary>The texture unit the sheet cache is bound to, after the layers and the motion vectors.</summary>
        private const int SheetCacheTextureUnit = RenderMaterial.TextureUnitStart + ParticleTextureLayer.MaxLayers + 1;

        /// <summary>The largest share of a ribbon's direction that may lie along world Z for Z to serve as its fallback side axis.</summary>
        private const float MaxRibbonVerticalShare = 0.999f;

        /// <summary>Multiplies the luminance-weighted blend the output alpha is scaled by.</summary>
        protected INumberProvider OverbrightFactor { get; } = new LiteralNumberProvider(1f);

        /// <summary>Scales the scene light a particle is lit by.</summary>
        protected INumberProvider DiffuseAmount { get; } = new LiteralNumberProvider(1f);

        /// <summary>
        /// The control point the whole system samples its lighting at while this renderer is lit, or -1
        /// to leave it where it is.
        /// </summary>
        private int LightingControlPoint { get; } = -1;

        /// <summary>Added on top of the scene light, unlit.</summary>
        protected INumberProvider SelfIllumAmount { get; } = new LiteralNumberProvider(0f);

        /// <summary>Fraction by which the sampled colour is pulled toward its own luminance.</summary>
        protected INumberProvider Desaturation { get; } = new LiteralNumberProvider(0f);

        /// <summary>
        /// The source alpha values that map to 0 and 1, which the shader remaps the texture alpha by
        /// linearly.
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
        /// colour of the blend-add modes unpremultiplied. Shaders with texture layers do, the older
        /// shaders without them keep the factor's sign.
        /// </summary>
        private bool UnsignedBlendAlpha { get; }

        /// <summary>
        /// Whether each texture layer's composite is clamped to [0, 1] rather than only floored at 0, as
        /// every layered shader but Counter-Strike 2's does.
        /// </summary>
        private bool ClampLayers { get; }

        /// <summary>Whether the base texture, and any texture that overrides it, is decoded from sRGB.</summary>
        private bool BaseTextureSrgbRead { get; }

        /// <summary>Additive self-colour laid over the alpha blend; uploaded as 1 + amount.</summary>
        protected INumberProvider AddSelfAmount { get; } = new LiteralNumberProvider(0f);

        /// <summary>Shifts the drawn quad within its own plane, in radii.</summary>
        protected INumberProvider CenterXOffset { get; } = new LiteralNumberProvider(0f);

        /// <inheritdoc cref="CenterXOffset"/>
        protected INumberProvider CenterYOffset { get; } = new LiteralNumberProvider(0f);

        /// <summary>
        /// Distance toward the camera the depth is re-taken from, in world units, leaving the drawn
        /// position alone. Pulls a card in front of geometry it would otherwise intersect.
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
        private ParticleAnimationType AnimationType { get; } = ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;

        /// <summary>
        /// What the age each particle hands the shader is measured by. Normally <see cref="AnimationType"/>,
        /// while the shader keeps reading the rate by <see cref="AnimationType"/> either way.
        /// </summary>
        protected ParticleAnimationType AgeAnimationType { get; init; }

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

        /// <summary>
        /// The sheet of motion vectors that warps the first two layers toward the frames they blend
        /// between, or null when there is none. It is not a layer and composites nothing.
        /// </summary>
        protected RenderTexture? MotionVectors { get; }

        /// <summary>Scales how far the motion vectors push, across and down the base texture.</summary>
        private INumberProvider MotionVectorScaleU { get; } = new LiteralNumberProvider(1f);

        /// <inheritdoc cref="MotionVectorScaleU"/>
        private INumberProvider MotionVectorScaleV { get; } = new LiteralNumberProvider(1f);

        /// <summary>
        /// Whether the card shrinks to the art of the frames its first layer blends, which only a first
        /// layer showing a sheet does.
        /// </summary>
        private bool CropsFrames { get; }

        /// <summary>The vertex array the draws read through.</summary>
        protected int VertexArrayHandle { get; private set; }

        /// <summary>How many quads the vertex buffer holds since the last <see cref="UpdateBuffers"/>.</summary>
        protected int QuadCount { get; private set; }

        private readonly string bufferLabel;
        private readonly Scene scene;
        private readonly int clampSampler;
        private float diffuseAmount;
        private readonly ParticleSheetCache sheetCache;
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
            LightingControlPoint = parse.Int32("m_nLightingControlPoint", LightingControlPoint);
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
            AgeAnimationType = AnimationType;
            AnimateInFps = parse.Boolean("m_bAnimateInFPS", AnimateInFps);
            StartFadeDot = parse.Float("m_flStartFadeDot", StartFadeDot);
            EndFadeDot = parse.Float("m_flEndFadeDot", EndFadeDot);
            MotionVectorScaleU = parse.NumberProvider("m_flMotionVectorScaleU", MotionVectorScaleU);
            MotionVectorScaleV = parse.NumberProvider("m_flMotionVectorScaleV", MotionVectorScaleV);

            SaturateColorPreAlphaBlend = parse.Boolean("m_bSaturateColorPreAlphaBlend", SaturateColorPreAlphaBlend);

            // TODO: m_bUseMixedResolutionRendering draws the renderer into a lower resolution target that is
            // upscaled over the scene, this draws it at full resolution

            var gameInfo = scene.RendererContext.FileLoader.GameInfo;

            Fogged = parse.Enum("m_nFogType", ParticleFogType.PARTICLE_FOG_GAME_DEFAULT) switch
            {
                ParticleFogType.PARTICLE_FOG_ENABLED => true,
                ParticleFogType.PARTICLE_FOG_GAME_DEFAULT => gameInfo.ParticlesFoggedByDefault,
                _ => false,
            };

            // Games that switch particle shader features at run time pass these two as feature bits instead of
            // static combos, and their shader never reads the saturate bit and tests the max luminance bit so
            // that it never matches
            if (gameInfo.ParticleShaderFeatureBranching)
            {
                MaxLuminanceFrameBlend = false;
                SaturateColorPreAlphaBlend = false;
            }

            // Shaders that switch features at run time have texture layers, and so do the static combo ones of these games
            UnsignedBlendAlpha = gameInfo.ParticleShaderFeatureBranching || gameInfo.Game is Game.Dota or Game.Steampal;
            ClampLayers = gameInfo.Game != Game.Csgo;

            GammaCorrectVertexColors &= OutputIsColor;
            SaturateColorPreAlphaBlend &= OutputIsColor;

            // Static combo shaders read the modulate-2x base texture as stored, so a stored half is the neutral,
            // rather than decoded from sRGB like any other layer
            var rawMod2xBase = !gameInfo.ParticleShaderFeatureBranching && BlendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_MOD2X;
            BaseTextureSrgbRead = TexturesAreColor && !rawMod2xBase;

            (Layers, MotionVectors, var textureName) = ParticleTextureLayer.Build(parse, rendererContext, defaultTextureName,
                srgbRead: TexturesAreColor, baseSrgbRead: BaseTextureSrgbRead);
            bufferLabel = $"{GetType().Name}: {System.IO.Path.GetFileName(textureName)}";
            clampSampler = rendererContext.MaterialLoader.GetOrCreateSampler(RsTextureAddressMode.Clamp, RsTextureAddressMode.Clamp);
            sheetCache = rendererContext.ParticleSheets;
            this.scene = scene;
            CropsFrames = ReadCropsFrames(parse);

            var shaderArguments = CreateShaderArguments();
            shaderArguments["S_TEXTURE_LAYERS"] = (byte)(Layers.Length - 1);

            if (MotionVectors != null)
            {
                shaderArguments["S_MOTION_VECTORS"] = 1;
            }

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
        /// <remarks>The base texture's sheet, which maps the particles' sequence numbers to sequences.</remarks>
        public override Texture.SpritesheetData? SpriteSheet => Layers[0].Texture.SpriteSheetData;

        /// <inheritdoc/>
        /// <remarks>Replaces the base texture. Layers composited over it keep their own textures, channels and blend settings.</remarks>
        public override void SetTextureOverride(RenderTexture texture)
        {
            Layers[0].Texture = texture;
        }

        /// <inheritdoc/>
        /// <remarks>The texture is decoded the way the base texture it replaces is.</remarks>
        public override void SetTextureOverride(string textureName)
        {
            SetTextureOverride(scene.RendererContext.MaterialLoader.GetTexture(textureName, BaseTextureSrgbRead, streaming: true));
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

        /// <summary>Uploads the first <paramref name="vertexCount"/> vertices of <paramref name="data"/> to the vertex buffer.</summary>
        protected void UploadVertices(byte[] data, int vertexCount)
        {
            GL.NamedBufferData(vertexBufferHandle, vertexCount * vertexStride, data, BufferUsageHint.DynamicDraw);
        }

        /// <summary>
        /// The layout of an instance whose attributes fill a <typeparamref name="TInstance"/>, allocating
        /// locations over every name the shader declares.
        /// </summary>
        protected static VertexInputLayout InstanceLayout<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)] TInstance>()
            where TInstance : struct
            => VertexInputLayout.FromStruct<TInstance>(SpritecardVertex.DeclaredAttributeNames);

        /// <inheritdoc/>
        /// <remarks>Particles the renderer culls are not written, so this can hold fewer quads than there are particles.</remarks>
        public sealed override void UpdateBuffers(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            diffuseAmount = DiffuseAmount.NextNumber(systemState);
            UpdateLightingOrigin(systemState);

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

            ParticleTextureLayer.Bind(Shader, Layers, systemState, sheetCache);
            BindMotionVectors(systemState);

            // Bound once every sheet of this draw is in, since adding one can replace the texture
            Shader.SetTexture(SheetCacheTextureUnit, "uSheetCache", sheetCache.Texture);

            SetSharedUniforms(systemState);
            Shader.SetUniform1("uBlendFrames", BlendFrames);
            Shader.SetUniform1("uCropFrames", CropsFrames);

            // A manually animated particle's age is already its place in the sequence
            var animationRate = AnimationType == ParticleAnimationType.ANIMATION_TYPE_MANUAL_FRAMES ? 1f : AnimationRate;
            Shader.SetUniform1("uAnimationRate", animationRate);
            Shader.SetUniform1("uAnimateInFps", AnimateInFps);

            // Set every draw: the program is shared with every other spritecard renderer, whatever their mode.
            Shader.SetUniform1("uBlendMode", (int)BlendMode);

            SetDrawUniforms(systemState);

            // The clamp samplers are bound for this draw only, so other draws keep their textures' own state
            var hasMotionVectors = MotionVectors != null;
            ParticleTextureLayer.BindClampSamplers(Layers, hasMotionVectors, clampSampler);

            PerfStats.Active.Count(Counter.ParticleDraw);
            Draw();

            ParticleTextureLayer.UnbindClampSamplers(Layers, hasMotionVectors);
        }

        /// <summary>
        /// Binds the motion vector sheet, where its sequences sit in the sheet cache, and how far a full
        /// vector pushes, in units of the base texture: the sheet's maximum distance in texels, scaled per
        /// axis and divided by the base texture's size.
        /// </summary>
        private void BindMotionVectors(ParticleSystemState systemState)
        {
            if (MotionVectors == null)
            {
                return;
            }

            var baseTexture = Layers[0].Texture;
            var maxDistance = (float)MotionVectors.MotionVectorsMaxDistance;
            var rows = sheetCache.GetRows(MotionVectors);

            Shader.SetTexture(RenderMaterial.TextureUnitStart + ParticleTextureLayer.MaxLayers, "uMotionVectors", MotionVectors);
            Shader.SetUniform2("uMotionVectorSheet", new Vector2(rows.FirstRow, rows.SequenceCount));
            Shader.SetUniform2("uMotionVectorScale", new Vector2(
                maxDistance * MotionVectorScaleU.NextNumber(systemState) / baseTexture.Width,
                maxDistance * MotionVectorScaleV.NextNumber(systemState) / baseTexture.Height));
        }

        /// <summary>
        /// The edge softness pair that turns the shader's soft edge off, as its lower end is not below its upper end.
        /// </summary>
        protected static readonly Vector2 NoEdgeSoftness = new(1000f, -1000f);

        /// <summary>Sets the uniforms only this renderer's draws read. Turns the outline and soft edges off unless overridden.</summary>
        protected virtual void SetDrawUniforms(ParticleSystemState systemState)
        {
            Shader.SetUniform1("uOutline", false);
            Shader.SetUniform2("uEdgeSoftness", NoEdgeSoftness);
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
        /// The scale and offset the shader remaps the sampled alpha by.
        /// </summary>
        /// <remarks>
        /// The offset is the alpha that maps to zero left unscaled, so the remap only sends that alpha to
        /// zero when it is zero itself.
        /// </remarks>
        private Vector2 GetAlphaRemap(ParticleSystemState systemState)
        {
            var mapToZero = SourceAlphaValueToMapToZero.NextNumber(systemState);
            var mapToOne = SourceAlphaValueToMapToOne.NextNumber(systemState);

            return new Vector2(1f / (mapToOne - mapToZero), -mapToZero);
        }

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

        /// <summary>
        /// Reads the random value of every particle in <paramref name="particles"/>, which the per particle
        /// modes spread per control. It is fixed for the particle's life, and displaced past the slots of
        /// the operators run in the last step.
        /// </summary>
        protected static ReadOnlySpan<float> ReadParticleRandoms(ParticleSpan particles, ParticleSystemState systemState)
        {
            var particleIds = particles.ReadIntegers(ParticleField.ParticleId);
            var randoms = ParticleScratch.Floats(particles.Length);

            for (var i = 0; i < randoms.Length; i++)
            {
                randoms[i] = systemState.Random.ForParticle(particleIds[i], RandomSlot);
            }

            return randoms;
        }

        /// <summary>Where the per particle random value sits past the particle's own slot.</summary>
        private const int RandomSlot = 235;

        /// <summary>
        /// Makes the system's root node sample its lighting at <see cref="LightingControlPoint"/>, when the
        /// renderer is lit and names one.
        /// </summary>
        private void UpdateLightingOrigin(ParticleSystemState systemState)
        {
            if (LightingControlPoint == -1 || OwnerNode is not { } node || diffuseAmount <= 0f)
            {
                return;
            }

            var origin = systemState.GetControlPoint(LightingControlPoint).Position;

            if (node.LightingOrigin == origin)
            {
                return;
            }

            node.LightingOrigin = origin;
            scene.UpdateNodeEnvironmentMaps(node);
        }

        /// <summary>
        /// Uploads the shared source 2 renderer state every particle shader reads.
        /// </summary>
        private void SetSharedUniforms(ParticleSystemState systemState)
        {
            Shader.SetUniform1("uOverbrightFactor", OverbrightFactor.NextNumber(systemState));
            // A literal amount at or below zero turns add-self off, which leaves the colour unscaled
            var addSelfDisabled = AddSelfAmount is LiteralNumberProvider { Value: <= 0f };
            Shader.SetUniform1("uAddSelfAmount", addSelfDisabled ? 1f : 1f + AddSelfAmount.NextNumber(systemState));
            Shader.SetUniform1("uDiffuseAmount", diffuseAmount);
            Shader.SetUniform1("uSelfIllumAmount", SelfIllumAmount.NextNumber(systemState));
            Shader.SetUniform1("uDesaturation", MathUtils.Saturate(Desaturation.NextNumber(systemState)));
            Shader.SetUniform3("uHsvShift", GetHsvShift(systemState));
            Shader.SetUniform2("uAlphaRemap", GetAlphaRemap(systemState));
            Shader.SetUniform1("uGammaCorrectVertexColors", GammaCorrectVertexColors);
            Shader.SetUniform1("uDepthBias", -DepthBias.NextNumber(systemState));
            Shader.SetUniform1("uMaxLuminanceFrameBlend", MaxLuminanceFrameBlend);
            Shader.SetUniform1("uSaturateColor", SaturateColorPreAlphaBlend);
            Shader.SetUniform1("uUnsignedBlendAlpha", UnsignedBlendAlpha);
            Shader.SetUniform1("uClampLayers", ClampLayers);
            Shader.SetUniform1("uLightingMode", (int)LightingMode);
            Shader.SetUniform1("g_tSceneDepth", (int)ReservedTextureSlots.SceneDepth);

            if (LitByProbes && OwnerNode != null)
            {
                Shader.SetUniform1("uObjectIndex", OwnerNode.Id);
            }

            Shader.SetUniform1("uFeathering", WantsSceneDepth);

            if (WantsSceneDepth)
            {
                Shader.SetUniform2("uFeatheringRange", new Vector2(FeatheringMinDist.NextNumber(systemState), FeatheringMaxDist.NextNumber(systemState)));
            }

            Shader.SetUniform1("uFeatheringFilter", FeatheringFilter.NextNumber(systemState));
            Shader.SetUniform1("uFogParticles", Fogged);
            Shader.SetUniform1("uAlphaReference", AlphaTested);
            Shader.SetUniform1("uAlphaReferenceMode", (int)PerParticleAlphaReference);
            Shader.SetUniform1("uAlphaRefWindowMode", (int)PerParticleAlphaRefWindow);

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

        /// <summary>
        /// The age every particle in <paramref name="particles"/> plays its sheet by, as
        /// <see cref="AgeAnimationType"/> measures it: the seconds it has lived, the share of its life it
        /// has lived, or the passes through the sequence it was given by hand.
        /// </summary>
        protected ReadOnlySpan<float> ReadSheetAges(ParticleSpan particles)
        {
            switch (AgeAnimationType)
            {
                case ParticleAnimationType.ANIMATION_TYPE_FIT_LIFETIME:
                    var ages = particles.ReadAges();
                    var lifetimes = particles.ReadFloats(ParticleField.LifeDuration);
                    var lifeShares = ParticleScratch.Floats(particles.Length);

                    for (var i = 0; i < lifeShares.Length; i++)
                    {
                        // A particle without a lifetime stays at the start of its sequence
                        lifeShares[i] = lifetimes[i] > 0f ? ages[i] / lifetimes[i] : 0f;
                    }

                    return lifeShares;
                case ParticleAnimationType.ANIMATION_TYPE_MANUAL_FRAMES:
                    return particles.ReadFloats(ParticleField.ManualAnimationFrame);
                default:
                    return particles.ReadAges();
            }
        }

        /// <summary>
        /// The sequence a particle's sequence number plays, as the shader reads it. The number is rounded
        /// to the nearest integer and, unless <paramref name="rawIndices"/> is set, looked up as a sequence
        /// id of the first layer's sheet, where an id the sheet does not have plays sequence 0.
        /// </summary>
        protected float SequenceLane(float sequenceNumber, bool rawIndices)
        {
            var number = (int)MathF.Round(sequenceNumber);
            var sheet = Layers[0].Texture.SpriteSheetData;

            if (rawIndices || sheet == null)
            {
                // A raw index is handed over in 16 bits
                return (short)number;
            }

            return Math.Max(sheet.GetSequenceIndex(number), 0);
        }

        /// <summary>
        /// The sequence every particle plays in place of its own, or -1 for none. The override is truncated
        /// and always looked up as a sequence id of the first layer's sheet, where an id the sheet does not
        /// have plays sequence 0. Without a sheet there is nothing to override.
        /// </summary>
        protected int OverrideSequence(float sequenceOverride)
        {
            var number = (int)sequenceOverride;
            var sheet = Layers[0].Texture.SpriteSheetData;

            if (number == -1 || sheet == null)
            {
                return -1;
            }

            return Math.Max(sheet.GetSequenceIndex(number), 0);
        }

        /// <summary>
        /// Whether the first layer's sheet frames are cropped to their art, as
        /// <c>m_nCropTextureOverride</c> asks, or by default when no later layer would be cut by it.
        /// </summary>
        private bool ReadCropsFrames(ParticleDefinitionParser parse)
            => parse.Enum("m_nCropTextureOverride", ParticleSequenceCropOverride.PARTICLE_SEQUENCE_CROP_OVERRIDE_DEFAULT) switch
            {
                ParticleSequenceCropOverride.PARTICLE_SEQUENCE_CROP_OVERRIDE_DEFAULT => LaterLayersAreColorLookups(Layers),
                ParticleSequenceCropOverride.PARTICLE_SEQUENCE_CROP_OVERRIDE_FORCE_ON => true,
                _ => false,
            };

        /// <summary>
        /// Whether every layer past the first only maps colours through a lookup, so none of them samples
        /// a sheet frame that cropping the card would cut.
        /// </summary>
        private static bool LaterLayersAreColorLookups(ParticleTextureLayer[] layers)
        {
            for (var layer = 1; layer < layers.Length; layer++)
            {
                if (layers[layer].EffectMode != SpriteCardTextureType.SPRITECARD_TEXTURE_1D_COLOR_LOOKUP)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether the view angle fade can take anything away, which needs its start below 1 and its end past the start.</summary>
        protected bool FadesByViewAngle => StartFadeDot < 1f && EndFadeDot > StartFadeDot;

        /// <summary>Turns the shader's view angle fade on or off; when on, it fades over the start and end dots.</summary>
        protected void SetViewAngleFadeUniforms(bool active)
        {
            Shader.SetUniform1("uViewAngleFade", active);
            Shader.SetUniform2("uViewAngleFadeDots", new Vector2(StartFadeDot, EndFadeDot));
        }

        /// <summary>
        /// Sets the size clamp and the distance fade, each bound a radius per unit of camera distance, or
        /// turns both off.
        /// </summary>
        protected void SetSizeClampAndFadeUniforms(bool active, float minSize, float startFadeSize, float endFadeSize, float maxSize)
        {
            Shader.SetUniform1("uSizeClampAndFade", active);
            Shader.SetUniform4("uSizeBounds", new Vector4(minSize, startFadeSize, endFadeSize, maxSize));
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
