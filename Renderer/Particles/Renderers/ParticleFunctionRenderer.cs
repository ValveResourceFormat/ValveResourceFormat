using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Particles;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Base class for particle renderers, which draw a particle collection each frame.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CBaseRendererSource2">CBaseRendererSource2</seealso>
    abstract class ParticleFunctionRenderer : ParticleFunction
    {
        /// <summary>Scales the radius every particle is drawn at.</summary>
        protected INumberProvider RadiusScale { get; } = new LiteralNumberProvider(1f);

        /// <summary>Scales the alpha every particle is drawn at.</summary>
        protected INumberProvider AlphaScale { get; } = new LiteralNumberProvider(1f);

        /// <summary>Scales the roll every particle is drawn at.</summary>
        protected INumberProvider RollScale { get; } = new LiteralNumberProvider(1f);

        /// <summary>Scales the colour every particle is drawn at, combined as <see cref="ColorBlendType"/> asks.</summary>
        protected IVectorProvider ColorScale { get; } = new LiteralVectorProvider(Vector3.One);

        /// <summary>How <see cref="ColorScale"/> combines with each particle's colour.</summary>
        protected ParticleColorBlendType ColorBlendType { get; }

        /// <summary>The attribute that scales each particle's alpha alongside its own, or <see cref="ParticleField.NoneDisabled"/> for none.</summary>
        protected ParticleField Alpha2Field { get; } = ParticleField.AlphaAlternate;

        /// <summary>How far each particle is drawn moved toward the camera, in world units.</summary>
        protected float CameraBias { get; }

        /// <summary>Multiplies the luminance-weighted blend the output alpha is scaled by.</summary>
        protected INumberProvider OverbrightFactor { get; } = new LiteralNumberProvider(1f);

        /// <summary>Scales the scene light a particle is lit by.</summary>
        protected INumberProvider DiffuseAmount { get; } = new LiteralNumberProvider(1f);

        /// <summary>Added on top of the scene light, unlit.</summary>
        protected INumberProvider SelfIllumAmount { get; } = new LiteralNumberProvider(0f);

        /// <summary>Fraction by which the sampled colour is pulled toward its own luminance.</summary>
        protected INumberProvider Desaturation { get; } = new LiteralNumberProvider(0f);

        /// <summary>Control point carrying a hue shift, saturation and lightness scale. Negative for none.</summary>
        protected int HsvShiftControlPoint { get; } = -1;

        /// <summary>
        /// The source alpha values that map to 0 and 1. The engine writes both unconditionally and the
        /// shader honours the remap whenever the first is below the second, so it is live by default.
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

        /// <summary>The order the particles are drawn in.</summary>
        private ParticleSortingChoiceList SortMethod { get; }

        /// <summary>Whether this renderer draws only into the water effects map, never into the scene.</summary>
        public bool OnlyRenderInEffectsWaterPass { get; }

        /// <summary>Render in the bloom effects.</summary>
        public bool OnlyRenderInEffectsBloomPass { get; }

        /// <summary>Whether what this renderer draws is an image; the water effects map takes data instead.</summary>
        protected bool OutputIsColor => !OnlyRenderInEffectsWaterPass;

        // ON_OPTIONAL and ON_REQUIRED currently treated the same
        private ParticleDepthFeatheringMode FeatheringMode { get; }

        /// <summary>How far a fragment stands off the opaque scene before it draws at full strength.</summary>
        private INumberProvider FeatheringMinDist { get; } = new LiteralNumberProvider(0f);

        /// <inheritdoc cref="FeatheringMinDist"/>
        private INumberProvider FeatheringMaxDist { get; } = new LiteralNumberProvider(15f);

        /// <summary>Widens the fade by how transparent and how off-centre a fragment already is.</summary>
        private INumberProvider FeatheringFilter { get; } = new LiteralNumberProvider(0f);

        /// <summary>Whether this renderer samples the opaque scene depth.</summary>
        public bool WantsSceneDepth => FeatheringMode != ParticleDepthFeatheringMode.PARTICLE_DEPTH_FEATHERING_OFF;

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

        /// <summary>Whether this renderer samples the opaque scene colour.</summary>
        public bool WantsSceneColor => Refract && OutputIsColor;

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

        private readonly Scene? scene;

        /// <summary>Whether this renderer is lit by the scene. The water effects map takes data rather than an image, so it never is.</summary>
        private bool LitByScene => scene != null && OutputIsColor;

        /// <summary>Whether this renderer is lit from the scene's light probes.</summary>
        private bool LitByProbes => LitByScene && scene!.LightingInfo.HasValidLightProbes;

        protected ParticleFunctionRenderer(ParticleDefinitionParser parse, Scene? scene = null) : base(parse)
        {
            this.scene = scene;

            OnlyRenderInEffectsWaterPass = parse.Boolean("m_bOnlyRenderInEffectsWaterPass", false);
            OnlyRenderInEffectsBloomPass = parse.Boolean("m_bOnlyRenderInEffectsBloomPass", false);
            LightingMode = parse.Enum("m_nLightingMode", LightingMode);
            RadiusScale = parse.NumberProvider("m_flRadiusScale", RadiusScale);
            AlphaScale = parse.NumberProvider("m_flAlphaScale", AlphaScale);
            RollScale = parse.NumberProvider("m_flRollScale", RollScale);
            ColorScale = parse.VectorProvider("m_vecColorScale", ColorScale);
            ColorBlendType = parse.Enum("m_nColorBlendType", ColorBlendType);
            Alpha2Field = parse.ParticleField("m_nAlpha2Field", Alpha2Field);

            if (parse.Data.GetSubCollection("VisibilityInputs") is { } visibilityInputs)
            {
                CameraBias = parse.Nested(visibilityInputs).Float("m_flCameraBias", CameraBias);
            }

            OverbrightFactor = parse.NumberProvider("m_flOverbrightFactor", OverbrightFactor);
            AddSelfAmount = parse.NumberProvider("m_flAddSelfAmount", AddSelfAmount);
            DepthBias = parse.NumberProvider("m_flDepthBias", DepthBias);
            SortMethod = parse.Enum("m_nSortMethod", SortMethod);
            DiffuseAmount = parse.NumberProvider("m_flDiffuseAmount", DiffuseAmount);
            SelfIllumAmount = parse.NumberProvider("m_flSelfIllumAmount", SelfIllumAmount);
            Desaturation = parse.NumberProvider("m_flDesaturation", Desaturation);
            HsvShiftControlPoint = parse.Int32("m_nHSVShiftControlPoint", HsvShiftControlPoint);
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

            SaturateColorPreAlphaBlend = parse.Boolean("m_bSaturateColorPreAlphaBlend", SaturateColorPreAlphaBlend);

            var fileLoader = scene?.RendererContext.FileLoader;

            Fogged = parse.Enum("m_nFogType", ParticleFogType.PARTICLE_FOG_GAME_DEFAULT) switch
            {
                ParticleFogType.PARTICLE_FOG_ENABLED => true,
                ParticleFogType.PARTICLE_FOG_GAME_DEFAULT => fileLoader?.ParticlesFoggedByDefault ?? false,
                _ => false,
            };

            if (fileLoader?.ParticleShaderFeatureBranching == true)
            {
                MaxLuminanceFrameBlend = false;
                SaturateColorPreAlphaBlend = false;
                UnsignedBlendAlpha = true;
            }

            GammaCorrectVertexColors &= OutputIsColor;
            SaturateColorPreAlphaBlend &= OutputIsColor;
        }

        /// <summary>Creates the static combos for this renderer's shader, lit from the scene when it draws an image.</summary>
        protected Dictionary<string, byte> CreateShaderArguments()
            => LitByScene ? scene!.LightingInfo.CreateShaderArguments() : [];

        /// <summary>
        /// The hue shift, saturation and lightness scale carried by <see cref="HsvShiftControlPoint"/>,
        /// or the identity when no control point is bound. The control point holds the two scales
        /// less one, so a zeroed one is the identity too.
        /// </summary>
        protected Vector3 GetHsvShift(ParticleSystemState systemState)
            => HsvShiftControlPoint >= 0
                ? systemState.GetControlPoint(HsvShiftControlPoint).Position + new Vector3(0f, 1f, 1f)
                : new Vector3(0f, 1f, 1f);

        /// <summary>The renderer scales, evaluated once for a draw of the whole collection.</summary>
        /// <param name="Radius">Scales every particle's radius.</param>
        /// <param name="Alpha">Scales every particle's alpha.</param>
        /// <param name="Roll">Scales every particle's roll.</param>
        /// <param name="Color">The colour scale converted for <see cref="ParticleColorBlend.Apply"/>, with the alpha scale in w.</param>
        protected readonly record struct RendererScales(float Radius, float Alpha, float Roll, Vector4 Color);

        /// <summary>Evaluates the renderer scales for one draw.</summary>
        protected RendererScales EvaluateScales(ParticleSystemState systemState)
        {
            var alpha = AlphaScale.NextNumber(systemState);

            return new RendererScales(
                RadiusScale.NextNumber(systemState),
                alpha,
                RollScale.NextNumber(systemState),
                new Vector4(ParticleColorBlend.ConvertScale(ColorBlendType, ColorScale.NextVector(systemState)), alpha));
        }

        private ParticleBuffer? blendedParticles;

        /// <summary>
        /// The particles as they stand at the time the system was asked to reach. That is
        /// <see cref="ParticleCollection.Current"/>, unless the last step carried the system past that
        /// time: then each particle is blended back from its state before the step, and the ones created
        /// after that time get no alpha.
        /// </summary>
        protected ParticleSpan GetBlendedParticles(ParticleCollection particles, ParticleSystemState systemState)
        {
            var current = particles.Current;

            if (current.IsEmpty || systemState.Data?.TryGetRenderBlend(out var fraction) != true)
            {
                return current;
            }

            var previous = particles.PreviousStep;

            if (blendedParticles == null || blendedParticles.Capacity < current.Length)
            {
                blendedParticles = new ParticleBuffer(current.Length);
            }

            blendedParticles.Copy(current.Buffer!, current.Start, 0, current.Length);
            var blended = new ParticleSpan(blendedParticles, 0, current.Length);

            for (var i = 0; i < blended.Length; i++)
            {
                Blend(previous[i], current[i], blended[i], fraction, systemState.RequestedAge);
            }

            return blended;
        }

        /// <summary>Writes the blend from <paramref name="previous"/> to <paramref name="current"/> into <paramref name="particle"/>, a copy of <paramref name="current"/>.</summary>
        private static void Blend(Particle previous, Particle current, Particle particle, float fraction, float requestedAge)
        {
            particle.Position = MathUtils.Lerp(previous.Position, current.Position, fraction);
            particle.PositionPrevious = MathUtils.Lerp(previous.PositionPrevious, current.PositionPrevious, fraction);
            particle.Age = MathUtils.Lerp(previous.Age, current.Age, fraction);
            particle.Radius = MathUtils.Lerp(previous.Radius, current.Radius, fraction);
            particle.Color = MathUtils.Lerp(previous.Color, current.Color, fraction);
            particle.Alpha = current.CreationTime > requestedAge ? 0f : MathUtils.Lerp(previous.Alpha, current.Alpha, fraction);
            particle.AlphaAlternate = MathUtils.Lerp(previous.AlphaAlternate, current.AlphaAlternate, fraction);
            particle.Rotation = MathUtils.Lerp(previous.Rotation, current.Rotation, fraction);
            particle.Normal = MathUtils.Lerp(previous.Normal, current.Normal, fraction);
            particle.TrailLength = MathUtils.Lerp(previous.TrailLength, current.TrailLength, fraction);
            particle.ManualAnimationFrame = MathUtils.Lerp(previous.ManualAnimationFrame, current.ManualAnimationFrame, fraction);
            particle.ShaderExtraData1 = MathUtils.Lerp(previous.ShaderExtraData1, current.ShaderExtraData1, fraction);
            particle.ShaderExtraData2 = MathUtils.Lerp(previous.ShaderExtraData2, current.ShaderExtraData2, fraction);
        }

        /// <summary>A particle's position moved toward the camera by <see cref="CameraBias"/>.</summary>
        protected Vector3 BiasTowardCamera(Vector3 position, Vector3 cameraPosition)
            => CameraBias == 0f
                ? position
                : position + (MathUtils.SafeNormalize(cameraPosition - position) * CameraBias);

        /// <summary>The particle's <see cref="Alpha2Field"/>, or 1 when none is set.</summary>
        protected float GetAlpha2(Particle particle)
            => Alpha2Field is ParticleField.NoneDisabled or < 0 ? 1f : particle.GetScalar(Alpha2Field);

        /// <summary>
        /// A particle's colour and alpha combined with the colour and alpha scale, in 0 to 1. The alpha
        /// is taken with <see cref="Alpha2Field"/> and <paramref name="alphaPrescale"/>, and both are
        /// clamped to bytes before they combine.
        /// </summary>
        protected Vector4 BlendColor(Particle particle, in RendererScales scales, float alphaPrescale = 1f)
            => ParticleColorBlend.Apply(ColorBlendType,
                ParticleColorBlend.ToBytes(particle.Color, alphaPrescale * particle.Alpha * GetAlpha2(particle)),
                scales.Color) / 255f;

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
        protected RenderPassScope SpritecardStateScope(ParticleBlendMode blendMode, bool opaque = false)
        {
            var renderState = GraphicsContext.RenderState;

            var depthSettings = blendMode != ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_LIGHTEN;
            bool? depthTest = depthSettings && DisableZBuffering ? false : null;
            RsComparison? depthFunc = depthSettings && !DisableZBuffering && ReverseZBuffering ? RsComparison.Farther : null;

            if (opaque)
            {
                return renderState.Scope(cullMode: RsCullMode.None, depthTest: depthTest, depthFunc: depthFunc);
            }

            var mod2x = blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_MOD2X;
            var lighten = blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_LIGHTEN;

            return renderState.Scope(blend: true, depthWrite: false, cullMode: RsCullMode.None,
                depthTest: depthTest, depthFunc: depthFunc,
                srcBlend: mod2x ? RsBlendMode.DestColor : RsBlendMode.One,
                dstBlend: lighten ? RsBlendMode.One : mod2x ? RsBlendMode.SrcColor : RsBlendMode.InvSrcAlpha,
                blendOp: lighten ? RsBlendOp.Max : RsBlendOp.Add);
        }

        /// <summary>
        /// The threshold a particle's texture alpha is tested against and the width of the band above it
        /// that passes, in x and y, as the shader reads them.
        /// </summary>
        /// <param name="particle">The particle being drawn.</param>
        /// <param name="radius">The radius it is drawn at.</param>
        /// <param name="alpha">The alpha it is drawn with.</param>
        protected Vector2 GetAlphaReference(Particle particle, float radius, float alpha)
            => AlphaTested
                ? new Vector2(
                    1f - GetPerParticleScale(PerParticleAlphaReference, particle, radius, alpha),
                    GetPerParticleScale(PerParticleAlphaRefWindow, particle, radius, alpha))
                : Vector2.Zero;

        /// <summary>The value a per particle input reads. Animation frames and the random sources read 1.</summary>
        private static float GetPerParticleScale(SpriteCardPerParticleScale source, Particle particle, float radius, float alpha) => source switch
        {
            SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_PARTICLE_AGE => particle.Age + 1f,
            SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_SHADER_EXTRA_DATA1 => particle.ShaderExtraData1,
            SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_SHADER_EXTRA_DATA2 => particle.ShaderExtraData2,
            SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_PARTICLE_ALPHA => alpha,
            SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_SHADER_RADIUS => radius,
            SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_ROLL => particle.Rotation.Z,
            SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_YAW => particle.Rotation.X,
            SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_PITCH => particle.Rotation.Y,
            _ => 1f,
        };

        /// <summary>
        /// Whether the particles are drawn farthest from the camera first rather than newest first.
        /// </summary>
        protected bool SortsByDistance => SortMethod == ParticleSortingChoiceList.PARTICLE_SORTING_NEAREST;

        /// <summary>
        /// Fills <paramref name="order"/> with one entry per particle, farthest from the camera first.
        /// The low half of an entry is the particle's index.
        /// </summary>
        protected static void SortBackToFront(ParticleSpan particles, Vector3 cameraPosition, Span<ulong> order)
        {
            for (var i = 0; i < order.Length; i++)
            {
                var distance = Vector3.DistanceSquared(particles[i].Position, cameraPosition);

                // A distance is never negative, so its bits order as an integer, and inverting them
                // puts the farthest first. The index below them keeps coincident particles in a
                // steady order from frame to frame.
                order[i] = ((ulong)~BitConverter.SingleToUInt32Bits(distance) << 32) | (uint)i;
            }

            order.Sort();
        }

        /// <summary>
        /// Uploads the shared source 2 renderer state every particle shader reads.
        /// </summary>
        protected void SetSharedUniforms(Shader shader, ParticleSystemState systemState)
        {
            shader.SetUniform1("uOverbrightFactor", OverbrightFactor.NextNumber(systemState));
            shader.SetUniform1("uAddSelfAmount", 1f + AddSelfAmount.NextNumber(systemState));
            shader.SetUniform1("uDiffuseAmount", DiffuseAmount.NextNumber(systemState));
            shader.SetUniform1("uSelfIllumAmount", SelfIllumAmount.NextNumber(systemState));
            shader.SetUniform1("uDesaturation", MathUtils.Saturate(Desaturation.NextNumber(systemState)));
            shader.SetUniform3("uHsvShift", GetHsvShift(systemState));
            shader.SetUniform2("uAlphaRemapRange", GetAlphaRemapRange(systemState));
            shader.SetUniform1("uGammaCorrectVertexColors", GammaCorrectVertexColors);
            shader.SetUniform1("uDepthBias", DepthBias.NextNumber(systemState));
            shader.SetUniform1("uMaxLuminanceFrameBlend", MaxLuminanceFrameBlend);
            shader.SetUniform1("uSaturateColor", SaturateColorPreAlphaBlend);
            shader.SetUniform1("uUnsignedBlendAlpha", UnsignedBlendAlpha);
            shader.SetUniform1("uLightingMode", (int)LightingMode);
            shader.SetUniform1("g_tSceneDepth", (int)ReservedTextureSlots.SceneDepth);

            if (LitByProbes && OwnerNode != null)
            {
                shader.SetUniform1("uObjectIndex", OwnerNode.Id);
            }

            shader.SetUniform2("uFeatheringRange", WantsSceneDepth
                ? new Vector2(FeatheringMinDist.NextNumber(systemState), FeatheringMaxDist.NextNumber(systemState))
                : Vector2.Zero);
            shader.SetUniform1("uFeatheringFilter", FeatheringFilter.NextNumber(systemState));
            shader.SetUniform1("uFogParticles", Fogged);
            shader.SetUniform1("uAlphaReference", AlphaTested);

            if (AlphaTested)
            {
                var softness = AlphaReferenceSoftness.NextNumber(systemState);
                var windowed = PerParticleAlphaRefWindow != SpriteCardPerParticleScale.SPRITECARD_TEXTURE_PP_SCALE_NONE;

                shader.SetUniform2("uAlphaReferenceOpaque", new Vector2(
                    AlphaReferenceType is ParticleAlphaReferenceType.PARTICLE_ALPHA_REFERENCE_OPAQUE_ALPHA or ParticleAlphaReferenceType.PARTICLE_ALPHA_REFERENCE_OPAQUE_OPAQUE ? 1f : 0f,
                    AlphaReferenceType is ParticleAlphaReferenceType.PARTICLE_ALPHA_REFERENCE_ALPHA_OPAQUE or ParticleAlphaReferenceType.PARTICLE_ALPHA_REFERENCE_OPAQUE_OPAQUE ? 1f : 0f));
                shader.SetUniform2("uAlphaReferenceSoftness", windowed
                    ? new Vector2(softness * 0.5f)
                    : new Vector2(0f, softness));
            }

            if (Refract)
            {
                shader.SetUniform1("uRefractAmount", RefractAmount.NextNumber(systemState));
                shader.SetUniform1("uRefractBlurRadius", RefractBlurRadius);
                shader.SetUniform1("uRefractBlurBox", RefractBlurType == BlurFilterType.BLURFILTER_BOX);
                shader.SetUniform1("uRefractSolid", RefractSolid);
            }
        }

        /// <summary>Adds the static combos every spritecard renderer derives from these settings.</summary>
        protected void AddSpritecardShaderArguments(Dictionary<string, byte> shaderArguments)
        {
            if (WantsSceneColor)
            {
                shaderArguments["S_REFRACT"] = 1;
            }
        }

        /// <summary>The pass this renderer draws in.</summary>
        public RenderPass Pass { get; protected set; } = RenderPass.Translucent;

        /// <summary>Where scene lighting is sampled.</summary>
        protected ParticleLightingQuality LightingMode { get; } = ParticleLightingQuality.PARTICLE_LIGHTING_PER_PARTICLE;

        /// <summary>
        /// The scene node the system this belongs to renders under, when it was created for one.
        /// Set by the system renderer; renderers that light themselves read its bindings through it.
        /// </summary>
        public SceneNode? OwnerNode { get; set; }

        /// <summary>Called as the system finishes each step of a frame. Runs on the pool, so touch only what this renderer owns.</summary>
        /// <param name="particles">The system's particles, as the step left them.</param>
        /// <param name="systemState">The state the system's functions read it through.</param>
        public virtual void Simulate(ParticleCollection particles, ParticleSystemState systemState)
        {
        }

        /// <summary>
        /// Called once the initializers have run over particles the system just created. Runs on the
        /// pool, so touch only what this renderer owns.
        /// </summary>
        /// <param name="particles">The system's particles.</param>
        /// <param name="systemState">The state the system's functions read it through.</param>
        /// <param name="first">The index of the first new particle.</param>
        /// <param name="count">How many particles were created.</param>
        public virtual void InitializeParticles(ParticleCollection particles, ParticleSystemState systemState, int first, int count)
        {
        }

        /// <summary>The particle events this renderer wants through <see cref="OnParticleEvent"/>.</summary>
        public virtual ParticleEventType ObservedEvents => ParticleEventType.PARTICLE_EVENT_TYPE_MASK_NONE;

        /// <summary>
        /// Called when particles of the system raise an event, during the step. Runs on the pool, so
        /// touch only what this renderer owns.
        /// </summary>
        /// <param name="particles">The system's particles; particles being killed are still in place.</param>
        /// <param name="eventType">The event, a single flag.</param>
        /// <param name="indices">The indices of the particles that raised it, ascending.</param>
        public virtual void OnParticleEvent(ParticleCollection particles, ParticleEventType eventType, ReadOnlySpan<int> indices)
        {
        }

        /// <summary>Finishes work done on <see cref="Simulate"/>, for what reaches past the system.</summary>
        /// <param name="particles">The system's particles, as the step left them.</param>
        /// <param name="systemState">The state the system's functions read it through.</param>
        public virtual void Act(ParticleCollection particles, ParticleSystemState systemState)
        {
        }

        /// <summary>
        /// Called in place of <see cref="Act"/> while the system does not draw: hidden, on a disabled
        /// layer, stopped, out of draw distance or faded out. Renderers that place nodes or decals in the
        /// scene take them away here.
        /// </summary>
        public virtual void Hide()
        {
        }

        /// <summary>
        /// Whether this renderer draws in the passes of the node it belongs to. Renderers that draw
        /// through scene nodes or decals of their own do not.
        /// </summary>
        public virtual bool DrawsInPasses => true;

        /// <summary>Uploads this renderer's vertex buffer. Called once a frame, before any pass draws.</summary>
        public virtual void UpdateBuffers(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
        }

        public abstract void Render(ParticleCollection particles, ParticleSystemState systemState, Camera camera);

        /// <summary>Whether this renderer draws itself into <see cref="RenderPass.DepthOnly"/>. Set alongside <see cref="Pass"/>.</summary>
        public bool CanRenderDepth { get; protected set; }

        /// <summary>Draws depth only.</summary>
        public virtual void RenderDepth(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
        }

        /// <summary>
        /// Whether <see cref="RenderReplacement"/> draws anything. Only the renderers that hand over
        /// world space vertices can; the rest expand their geometry in their own vertex shader.
        /// </summary>
        public virtual bool CanRenderReplacement => false;

        /// <summary>
        /// Draws with a pass replacement shader, for the picking buffer and the outline mask.
        /// </summary>
        /// <param name="replacement">The program the pass replaced the material shaders with.</param>
        /// <param name="objectId">The owning scene node's id, drawn as the instancing base.</param>
        public virtual void RenderReplacement(Shader replacement, uint objectId)
        {
        }

        /// <summary>Draws indexed geometry with the object id as the instancing base, which is how the
        /// picking and outline programs read it.</summary>
        protected static void DrawReplacement(Shader replacement, uint objectId, int vaoHandle, int indexCount, DrawElementsType indexType)
        {
            if (indexCount == 0)
            {
                return;
            }

            VertexArray.Bind(vaoHandle, replacement);

            GL.DrawElementsInstancedBaseInstance(PrimitiveType.Triangles, indexCount, indexType, 0, 1, objectId);
        }

        /// <summary>
        /// The sheet this renderer animates its particles with, or null when the textures it draws
        /// with carry none.
        /// </summary>
        public virtual Texture.SpritesheetData? SpriteSheet => null;

        /// <summary>A sheet frame rectangle as the shader reads it: minimum in xy, maximum in zw.</summary>
        /// <param name="min">Lower corner.</param>
        /// <param name="max">Upper corner.</param>
        protected static Vector4 Rect(Vector2 min, Vector2 max) => new(min.X, min.Y, max.X, max.Y);

        /// <summary>
        /// The two sheet frames a particle sits between and how far it has crossed from the first to
        /// the second. The particle's age and animation type give the playback position, which
        /// <see cref="Texture.SpritesheetData.Sequence.GetFrameAtPosition"/> then resolves to frames.
        /// </summary>
        protected static (int Frame, int NextFrame, float Blend) GetSheetFrame(Particle particle,
            Texture.SpritesheetData.Sequence sequence, float animationRate, ParticleAnimationType animationType, bool animateInFps)
        {
            var frameCount = sequence.Frames.Length;

            if (frameCount < 2)
            {
                return (0, 0, 0f);
            }

            var totalTime = sequence.EffectiveTotalTime;

            float passes;

            if (animationType == ParticleAnimationType.ANIMATION_TYPE_MANUAL_FRAMES)
            {
                passes = particle.ManualAnimationFrame;
            }
            else
            {
                // The animation time is chosen by type first; animating in FPS only changes how the
                // rate is interpreted afterwards, it does not replace the type
                var animationTime = animationType switch
                {
                    ParticleAnimationType.ANIMATION_TYPE_FIT_LIFETIME => particle.NormalizedAge,
                    _ => particle.Age,
                };

                passes = animateInFps
                    ? animationTime * animationRate / totalTime
                    : animationTime * animationRate;
            }

            var position = totalTime * (sequence.Clamp
                ? MathUtils.Saturate(passes)
                : MathUtils.Fract(passes));

            return sequence.GetFrameAtPosition(position);
        }

        /// <summary>
        /// Replaces the texture this renderer draws with.
        /// </summary>
        public virtual void SetTextureOverride(RenderTexture texture) { }

        /// <summary>
        /// Replaces the material this renderer draws with, for renderers that draw with one.
        /// </summary>
        public virtual void SetMaterialOverride(RenderMaterial material) { }

        public virtual void SetRenderMode(string renderMode) { }
        public virtual IEnumerable<string> GetSupportedRenderModes() => [];
        public virtual void Delete() { }
    }
}
