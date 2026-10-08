namespace ValveResourceFormat.Particles.Emitters
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

        /// <summary>
        /// System age the emitter was last started at. Start times and emission durations are measured
        /// from it against <see cref="ParticleSystemState.Age"/>, which advances every frame whether or
        /// not the operator fade left this emitter running.
        /// </summary>
        protected float StartAge { get; private set; }

        /// <summary>Starts the emitter, registering the callback used to spawn particles.</summary>
        public void Start(Action<float> particleEmitCallback, ParticleSystemState particleSystemState)
        {
            StartAge = particleSystemState.Age;
            OnStart(particleEmitCallback);
        }

        /// <summary>Resets the emitter's own state for a fresh run.</summary>
        protected abstract void OnStart(Action<float> particleEmitCallback);

        /// <summary>Signals the emitter to stop spawning new particles.</summary>
        public abstract void Stop();

        /// <summary>Runs one frame of emission, scaled by the operator run strength.</summary>
        public abstract void Emit(float frameTime, ParticleSystemState particleSystemState, float strength);

        /// <summary>Gets whether the emitter has finished emitting and will produce no more particles.</summary>
        public abstract bool IsFinished { get; protected set; }

        /// <summary>
        /// Narrows the frame to the part of it the emitter is active for. An emission duration of 0
        /// leaves the frame unclipped; the emitter then runs until stopped.
        /// </summary>
        /// <returns><c>true</c> if any of the frame falls inside the emitting window.</returns>
        protected static bool TryGetEmissionWindow(float frameStart, float frameEnd, float startTime, float duration,
            out float windowStart, out float windowEnd)
        {
            windowStart = frameStart;
            windowEnd = frameEnd;

            if (startTime > frameEnd)
            {
                return false;
            }

            if (duration != 0f)
            {
                windowStart = MathF.Max(startTime, windowStart);
                windowEnd = MathF.Min(startTime + duration, windowEnd);
            }

            return windowEnd > windowStart;
        }

        /// <summary>
        /// The engine's sub-frame emission accumulator, shared by the rate-driven emitters: charge it
        /// over the active part of a frame and it spawns whole particles, staggering their creation
        /// times evenly across the charged interval so a frame's worth of particles are not coincident.
        /// </summary>
        protected struct EmissionAccumulator
        {
            private double pending;
            private double floorEpsilon;
            private long flushed;

            /// <summary>
            /// Resets to the emitter's initial state. A charge of 1 emits one particle immediately so
            /// short or slow emitters still produce something; the continuous emitter instead starts
            /// uncharged with a small epsilon inside the flush threshold.
            /// </summary>
            public void Reset(double initialCharge, float flushEpsilon)
            {
                pending = initialCharge;
                floorEpsilon = flushEpsilon;
                flushed = 0;
            }

            /// <summary>Accumulates <paramref name="rate"/> particles per second across the window and spawns whatever came due.</summary>
            public void Charge(float rate, float windowStart, float windowEnd, float frameEnd, Action<float>? emit)
            {
                pending += Math.Max(0f, rate) * (windowEnd - windowStart);

                var flushTo = (long)Math.Floor(pending + floorEpsilon);
                var toEmit = flushTo - flushed;

                if (toEmit <= 0)
                {
                    return;
                }

                flushed = flushTo;

                var step = (windowEnd - windowStart) / toEmit;

                for (var i = 0; i < toEmit; i++)
                {
                    var creationTime = MathF.Min(windowStart + ((i + 1) * step), windowEnd);
                    emit?.Invoke(frameEnd - creationTime);
                }
            }
        }
    }
}
