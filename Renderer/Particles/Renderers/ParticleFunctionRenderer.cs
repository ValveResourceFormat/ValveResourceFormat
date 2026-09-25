using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Particles;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Base class for all particle renderers. Renderers are responsible for drawing the visual
    /// representation of a particle collection each frame.
    /// </summary>
    abstract class ParticleFunctionRenderer : ParticleFunction
    {
        /// <summary>Scales the radius every particle is drawn at.</summary>
        protected INumberProvider RadiusScale { get; } = new LiteralNumberProvider(1f);

        /// <summary>Scales the alpha every particle is drawn at.</summary>
        protected INumberProvider AlphaScale { get; } = new LiteralNumberProvider(1f);

        /// <summary>Scales the colour every particle is drawn at.</summary>
        protected IVectorProvider ColorScale { get; } = new LiteralVectorProvider(Vector3.One);

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

        /// <summary>Whether the colour is clamped to 0-1 before the alpha blend rather than after.</summary>
        protected bool SaturateColorPreAlphaBlend { get; } = true;

        /// <summary>Whether sheet frame blending keeps the brighter of the two frames per channel.</summary>
        protected bool MaxLuminanceFrameBlend { get; }

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
        private INumberProvider FeatheringMaxDist { get; } = new LiteralNumberProvider(0f);

        /// <summary>Widens the fade by how transparent and how off-centre a fragment already is.</summary>
        private INumberProvider FeatheringFilter { get; } = new LiteralNumberProvider(1f);

        /// <summary>Whether this renderer samples the opaque scene depth.</summary>
        public bool WantsSceneDepth => FeatheringMode != ParticleDepthFeatheringMode.PARTICLE_DEPTH_FEATHERING_OFF;

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
            RadiusScale = parse.NumberProvider("m_flRadiusScale", RadiusScale);
            AlphaScale = parse.NumberProvider("m_flAlphaScale", AlphaScale);
            ColorScale = parse.VectorProvider("m_vecColorScale", ColorScale);
            OverbrightFactor = parse.NumberProvider("m_flOverbrightFactor", OverbrightFactor);
            AddSelfAmount = parse.NumberProvider("m_flAddSelfAmount", AddSelfAmount);
            DepthBias = parse.NumberProvider("m_flDepthBias", DepthBias);
            DiffuseAmount = parse.NumberProvider("m_flDiffuseAmount", DiffuseAmount);
            SelfIllumAmount = parse.NumberProvider("m_flSelfIllumAmount", SelfIllumAmount);
            Desaturation = parse.NumberProvider("m_flDesaturation", Desaturation);
            HsvShiftControlPoint = parse.Int32("m_nHSVShiftControlPoint", HsvShiftControlPoint);
            SourceAlphaValueToMapToZero = parse.NumberProvider("m_flSourceAlphaValueToMapToZero", SourceAlphaValueToMapToZero);
            SourceAlphaValueToMapToOne = parse.NumberProvider("m_flSourceAlphaValueToMapToOne", SourceAlphaValueToMapToOne);
            GammaCorrectVertexColors = parse.Boolean("m_bGammaCorrectVertexColors", GammaCorrectVertexColors);
            SaturateColorPreAlphaBlend = parse.Boolean("m_bSaturateColorPreAlphaBlend", SaturateColorPreAlphaBlend);
            MaxLuminanceFrameBlend = parse.Boolean("m_bMaxLuminanceBlendingSequence0", MaxLuminanceFrameBlend);
            CenterXOffset = parse.NumberProvider("m_flCenterXOffset", CenterXOffset);
            CenterYOffset = parse.NumberProvider("m_flCenterYOffset", CenterYOffset);
            FeatheringMode = parse.Enum("m_nFeatheringMode", FeatheringMode);
            FeatheringMinDist = parse.NumberProvider("m_flFeatheringMinDist", FeatheringMinDist);
            FeatheringMaxDist = parse.NumberProvider("m_flFeatheringMaxDist", FeatheringMaxDist);
            FeatheringFilter = parse.NumberProvider("m_flFeatheringFilter", FeatheringFilter);

            GammaCorrectVertexColors &= OutputIsColor;
        }

        /// <summary>Creates the static combos for this renderer's shader, lit from the scene when it draws an image.</summary>
        protected Dictionary<string, byte> CreateShaderArguments()
            => LitByScene ? scene!.LightingInfo.CreateShaderArguments() : [];

        /// <summary>
        /// The hue shift, saturation and lightness scale carried by <see cref="HsvShiftControlPoint"/>,
        /// or the identity when no control point is bound.
        /// </summary>
        protected Vector3 GetHsvShift(ParticleSystemState systemState)
            => HsvShiftControlPoint >= 0
                ? systemState.GetControlPoint(HsvShiftControlPoint).Position
                : new Vector3(0f, 1f, 1f);

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
        /// its own factors, while everything else composites premultiplied. Both faces are drawn,
        /// since a card or ribbon can turn either one toward the camera.
        /// </summary>
        protected static RenderPassScope SpritecardStateScope(RenderStateTracker renderState, ParticleBlendMode blendMode, bool opaque = false)
        {
            if (opaque)
            {
                return renderState.Scope(cullMode: RsCullMode.None);
            }

            var mod2x = blendMode == ParticleBlendMode.PARTICLE_OUTPUT_BLEND_MODE_MOD2X;

            return renderState.Scope(blend: true, depthWrite: false, cullMode: RsCullMode.None,
                srcBlend: mod2x ? RsBlendMode.DestColor : RsBlendMode.One,
                dstBlend: mod2x ? RsBlendMode.SrcColor : RsBlendMode.InvSrcAlpha);
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
            shader.SetUniform1("uDesaturation", Desaturation.NextNumber(systemState));
            shader.SetUniform3("uHsvShift", GetHsvShift(systemState));
            shader.SetUniform2("uAlphaRemapRange", GetAlphaRemapRange(systemState));
            shader.SetUniform3("uColorScale", ColorScale.NextVector(systemState));
            shader.SetUniform1("uGammaCorrectVertexColors", GammaCorrectVertexColors);
            shader.SetUniform1("uDepthBias", DepthBias.NextNumber(systemState));
            shader.SetUniform1("uSaturateColorPreAlphaBlend", SaturateColorPreAlphaBlend);
            shader.SetUniform1("uMaxLuminanceFrameBlend", MaxLuminanceFrameBlend);
            shader.SetUniform1("g_tSceneDepth", (int)ReservedTextureSlots.SceneDepth);

            if (LitByProbes && OwnerNode != null)
            {
                shader.SetUniform1("uObjectIndex", OwnerNode.Id);
            }

            shader.SetUniform2("uFeatheringRange", WantsSceneDepth
                ? new Vector2(FeatheringMinDist.NextNumber(systemState), FeatheringMaxDist.NextNumber(systemState))
                : Vector2.Zero);
            shader.SetUniform1("uFeatheringFilter", FeatheringFilter.NextNumber(systemState));
        }

        /// <summary>The pass this renderer draws in.</summary>
        public RenderPass Pass { get; protected set; } = RenderPass.Translucent;

        /// <summary>
        /// The scene node the system this belongs to renders under, when it was created for one.
        /// Set by the system renderer; renderers that light themselves read its bindings through it.
        /// </summary>
        public SceneNode? OwnerNode { get; set; }

        /// <summary>Called as the system finishes a step. Runs on the pool, so touch only what this renderer owns.</summary>
        /// <param name="particles">The system's particles, as the step left them.</param>
        /// <param name="systemState">The state the system's functions read it through.</param>
        public virtual void Simulate(ParticleCollection particles, ParticleSystemState systemState)
        {
        }

        /// <summary>Finishes work done on <see cref="Simulate"/>, for what reaches past the system.</summary>
        /// <param name="particles">The system's particles, as the step left them.</param>
        /// <param name="systemState">The state the system's functions read it through.</param>
        public virtual void Act(ParticleCollection particles, ParticleSystemState systemState)
        {
        }

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
        protected static (int Frame, int NextFrame, float Blend) GetSheetFrame(ref Particle particle,
            Texture.SpritesheetData.Sequence sequence, float animationRate, ParticleAnimationType animationType, bool animateInFps)
        {
            var frameCount = sequence.Frames.Length;

            if (frameCount < 2)
            {
                return (0, 0, 0f);
            }

            var totalTime = sequence.EffectiveTotalTime;
            var lastFrame = frameCount - 1;

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

        public virtual void SetWireframe(bool wireframe) { }
        public virtual void SetRenderMode(string renderMode) { }
        public virtual IEnumerable<string> GetSupportedRenderModes() => [];
        public virtual void Delete() { }
    }
}
