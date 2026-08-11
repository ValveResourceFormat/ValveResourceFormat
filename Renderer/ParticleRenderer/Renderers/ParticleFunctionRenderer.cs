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

        /// <summary>Summed with <see cref="SelfIllumAmount"/> into the shader's colour factor.</summary>
        protected INumberProvider DiffuseAmount { get; } = new LiteralNumberProvider(1f);

        /// <inheritdoc cref="DiffuseAmount"/>
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

        protected ParticleFunctionRenderer(ParticleDefinitionParser parse) : base(parse)
        {
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
        }

        /// <summary>
        /// The hue shift, saturation and lightness scale carried by <see cref="HsvShiftControlPoint"/>,
        /// or the identity when no control point is bound.
        /// </summary>
        protected Vector3 GetHsvShift(ParticleSystemRenderState systemRenderState)
            => HsvShiftControlPoint >= 0
                ? systemRenderState.GetControlPoint(HsvShiftControlPoint).Position
                : new Vector3(0f, 1f, 1f);

        /// <summary>
        /// The remap the shader applies to the sampled alpha, as the pair the shader expects.
        /// </summary>
        protected Vector2 GetAlphaRemapRange(ParticleSystemRenderState systemRenderState)
            => new(SourceAlphaValueToMapToZero.NextNumber(systemRenderState),
                SourceAlphaValueToMapToOne.NextNumber(systemRenderState));

        /// <summary>
        /// Uploads the shared source 2 renderer state every particle shader reads.
        /// </summary>
        protected void SetSharedUniforms(Shader shader, ParticleSystemRenderState systemRenderState)
        {
            shader.SetUniform1("uOverbrightFactor", OverbrightFactor.NextNumber(systemRenderState));
            shader.SetUniform1("uAddSelfAmount", 1f + AddSelfAmount.NextNumber(systemRenderState));
            shader.SetUniform1("uColorFactor", DiffuseAmount.NextNumber(systemRenderState) + SelfIllumAmount.NextNumber(systemRenderState));
            shader.SetUniform1("uDesaturation", Desaturation.NextNumber(systemRenderState));
            shader.SetUniform3("uHsvShift", GetHsvShift(systemRenderState));
            shader.SetUniform2("uAlphaRemapRange", GetAlphaRemapRange(systemRenderState));
            shader.SetUniform3("uColorScale", ColorScale.NextVector(systemRenderState));
            shader.SetUniform1("uGammaCorrectVertexColors", GammaCorrectVertexColors);
            shader.SetUniform1("uDepthBias", DepthBias.NextNumber(systemRenderState));
            shader.SetUniform1("uSaturateColorPreAlphaBlend", SaturateColorPreAlphaBlend);
            shader.SetUniform1("uMaxLuminanceFrameBlend", MaxLuminanceFrameBlend);
        }

        /// <summary>
        /// The pass this renderer draws in.
        /// </summary>
        public RenderPass Pass { get; protected set; } = RenderPass.Translucent;

        public virtual void Update(ParticleCollection particles, ParticleSystemRenderState systemRenderState)
        {
        }

        public abstract void Render(ParticleCollection particles, ParticleSystemRenderState systemRenderState, Camera camera);

        /// <summary>
        /// The two sheet frames a particle sits between and how far it has crossed from the first to
        /// the second. Every frame is held for its own display time as a share of the sequence's total,
        /// so a sequence whose frames have uneven display times does not play at a uniform rate.
        /// A clamping sequence holds its last frame; otherwise it wraps back to the first.
        /// </summary>
        protected static (int Frame, int NextFrame, float Blend) GetSheetFrame(ref Particle particle,
            Texture.SpritesheetData.Sequence sequence, float animationRate, ParticleAnimationType animationType, bool animateInFps)
        {
            var frameCount = sequence.Frames.Length;

            if (frameCount < 2)
            {
                return (0, 0, 0f);
            }

            var totalTime = sequence.TotalTime > 0f ? sequence.TotalTime : 1f;
            var lastFrame = frameCount - 1;

            if (animationType == ParticleAnimationType.ANIMATION_TYPE_MANUAL_FRAMES)
            {
                var manualFrame = sequence.Clamp
                    ? Math.Clamp(particle.ManualAnimationFrame, 0, lastFrame)
                    : ((particle.ManualAnimationFrame % frameCount) + frameCount) % frameCount;

                return (manualFrame, manualFrame, 0f);
            }

            // The animation time is chosen by type first; animating in FPS only changes how the
            // rate is interpreted afterwards, it does not replace the type
            var animationTime = animationType switch
            {
                ParticleAnimationType.ANIMATION_TYPE_FIT_LIFETIME => particle.NormalizedAge,
                _ => particle.Age,
            };

            var passes = animateInFps
                ? animationTime * animationRate / totalTime
                : animationTime * animationRate;

            var position = totalTime * (sequence.Clamp
                ? Math.Clamp(passes, 0f, 1f)
                : passes - MathF.Floor(passes));

            var frameStart = 0f;

            for (var frame = 0; frame < lastFrame; frame++)
            {
                var displayTime = sequence.Frames[frame].DisplayTime;

                if (frameStart + displayTime > position)
                {
                    return (frame, frame + 1, CrossedFraction(position - frameStart, displayTime));
                }

                frameStart += displayTime;
            }

            return sequence.Clamp
                ? (lastFrame, lastFrame, 0f)
                : (lastFrame, 0, CrossedFraction(position - frameStart, totalTime - frameStart));
        }

        private static float CrossedFraction(float into, float span) => span > 0f ? into / span : 0f;

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
