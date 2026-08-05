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
        /// Resolves a sheet frame number against a sequence's length. A clamping sequence holds its last
        /// frame; otherwise it loops.
        /// </summary>
        protected static int ResolveSheetFrame(int frameId, int frameCount, bool clamp)
        {
            if (clamp)
            {
                return Math.Clamp(frameId, 0, frameCount - 1);
            }

            frameId %= frameCount;
            return frameId < 0 ? frameId + frameCount : frameId;
        }

        /// <summary>
        /// The fractional sheet frame a particle is on. The integer part selects the frame and the
        /// remainder is how far into the next one it has travelled.
        /// </summary>
        protected static float GetSheetFrame(ref Particle particle, float framesPerSecond, float animationRate,
            ParticleAnimationType animationType, bool animateInFps)
        {
            if (animationType == ParticleAnimationType.ANIMATION_TYPE_MANUAL_FRAMES)
            {
                return particle.ManualAnimationFrame;
            }

            if (animateInFps)
            {
                return animationRate * particle.Age;
            }

            var animationTime = animationType switch
            {
                ParticleAnimationType.ANIMATION_TYPE_FIT_LIFETIME => particle.NormalizedAge,
                _ => particle.Age,
            };

            return animationTime * animationRate * framesPerSecond;
        }

        public virtual void SetWireframe(bool wireframe) { }
        public virtual void SetRenderMode(string renderMode) { }
        public virtual IEnumerable<string> GetSupportedRenderModes() => [];
        public virtual void Delete() { }
    }
}
