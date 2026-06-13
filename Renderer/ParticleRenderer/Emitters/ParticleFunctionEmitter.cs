namespace ValveResourceFormat.Renderer.Particles.Emitters
{
    /// <summary>
    /// Base class for all particle emitters. Emitters are responsible for spawning new particles
    /// into a particle system over time.
    /// </summary>
    abstract class ParticleFunctionEmitter : ParticleFunction
    {
        protected ParticleFunctionEmitter(ParticleDefinitionParser parse) : base(parse)
        {
        }

        /// <summary>Starts the emitter, registering the callback used to spawn particles.</summary>
        public abstract void Start(Action particleEmitCallback);

        /// <summary>Signals the emitter to stop spawning new particles.</summary>
        public abstract void Stop();

        /// <summary>Called each frame to emit particles based on elapsed time.</summary>
        public abstract void Emit(float frameTime);

        /// <summary>Gets whether the emitter has finished emitting and will produce no more particles.</summary>
        public abstract bool IsFinished { get; protected set; }
    }
}
