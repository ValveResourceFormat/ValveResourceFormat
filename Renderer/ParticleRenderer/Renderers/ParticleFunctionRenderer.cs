using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Base class for all particle renderers. Renderers are responsible for drawing the visual
    /// representation of a particle collection each frame.
    /// </summary>
    abstract class ParticleFunctionRenderer : ParticleFunction
    {
        protected ParticleFunctionRenderer(ParticleDefinitionParser parse) : base(parse)
        {
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
