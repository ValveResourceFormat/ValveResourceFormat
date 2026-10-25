namespace ValveResourceFormat.Particles.Emitters
{
    /// <summary>
    /// Base class for all particle emitters. Emitters are responsible for spawning new particles
    /// into a particle system over time.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CParticleFunctionEmitter">CParticleFunctionEmitter</seealso>
    abstract class ParticleFunctionEmitter : ParticleFunction
    {
        protected ParticleFunctionEmitter(ParticleDefinitionParser parse) : base(parse)
        {
        }

        /// <summary>
        /// The emitter's position in the system's emitter list, which initializers bind to through
        /// <c>m_nAssociatedEmitterIndex</c>.
        /// </summary>
        public int EmitterIndex { get; set; }

        /// <summary>
        /// The parent particle events this emitter spawns from, or none for an emitter that runs in
        /// every step. An event emitter runs only when its parent hands it a matching event.
        /// </summary>
        public virtual ParticleEventType EventTypeMask => ParticleEventType.PARTICLE_EVENT_TYPE_MASK_NONE;

        /// <inheritdoc/>
        /// <remarks>The creation time each particle is emitted at.</remarks>
        public override ulong StoredFields => ParticleField.CreationTime.Mask();

        /// <summary>Builds the running state the emitter starts with.</summary>
        public abstract override void Reset();

        /// <summary>Lets the emitter emit again.</summary>
        public abstract void StartEmission();

        /// <summary>Stops the emitter.</summary>
        public abstract void StopEmission();

        /// <summary>Whether the emitter can still create particles.</summary>
        public abstract bool MayCreateMoreParticles(ParticleSystemState particleSystemState);

        /// <summary>
        /// Runs one step of emission, scaled by the operator run strength. Each particle is created by
        /// calling <paramref name="emit"/> with its creation time.
        /// </summary>
        public abstract void Emit(ParticleSystemState particleSystemState, float strength, Action<float> emit);

        /// <summary>
        /// Called after the system jumped forward to <paramref name="time"/>, so the emitter can
        /// simulate the span it would have emitted over.
        /// </summary>
        public virtual void SkipToTime(float time, ParticleSystemSimulation simulation)
        {
        }

        /// <summary>How many more particles the system has room for.</summary>
        protected static int FreeSlots(ParticleSystemState particleSystemState)
            => particleSystemState.Data is { } simulation ? simulation.ParticleCapacity - simulation.Particles.Count : 0;
    }
}
