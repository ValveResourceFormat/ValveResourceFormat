using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Emitters
{
    /// <summary>
    /// Emits a fixed number of particles in a single burst at a specified start time.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_InstantaneousEmitter">C_OP_InstantaneousEmitter</seealso>
    class InstantaneousEmitter : ParticleFunctionEmitter
    {
        public override bool IsFinished { get; protected set; }

        private Action<float>? particleEmitCallback;

        private readonly INumberProvider emitCount = new LiteralNumberProvider(100);
        private readonly INumberProvider startTime = new LiteralNumberProvider(0);

        /// <summary>
        /// Multiplies the burst by the parent system's live particle count. -1 leaves it unscaled.
        /// </summary>
        private readonly INumberProvider parentParticleScale = new LiteralNumberProvider(-1);

        private readonly int maxEmittedPerFrame = -1;

        /// <summary>Scales the burst by how many parent particles died this frame, making it recur.</summary>
        private readonly float initFromKilledParentParticles;
        private readonly SnapshotBinding snapshotBinding;

        private float time;
        private int remainingToEmit;
        private bool countResolved;
        private bool snapshotResolved;

        /// <summary>
        /// The snapshot's creation-time column, when it carries one. Its presence turns the burst into
        /// a timed release rather than a single instant.
        /// </summary>
        private float[]? snapshotTimes;

        /// <summary>How far into the timed snapshot release the burst has got.</summary>
        private int snapshotCursor;

        public InstantaneousEmitter(ParticleDefinitionParser parse) : base(parse)
        {
            emitCount = parse.NumberProvider("m_nParticlesToEmit", emitCount);
            startTime = parse.NumberProvider("m_flStartTime", startTime);
            parentParticleScale = parse.NumberProvider("m_flParentParticleScale", parentParticleScale);
            maxEmittedPerFrame = parse.Int32("m_nMaxEmittedPerFrame", maxEmittedPerFrame);
            initFromKilledParentParticles = parse.Float("m_flInitFromKilledParentParticles", initFromKilledParentParticles);
            snapshotBinding = new SnapshotBinding(parse);
        }

        public override void Start(Action<float> particleEmitCallback)
        {
            this.particleEmitCallback = particleEmitCallback;

            IsFinished = false;

            time = 0;
            remainingToEmit = 0;
            countResolved = false;
            snapshotCursor = 0;
        }

        public override void Stop()
        {
            IsFinished = true;
            particleEmitCallback = null;
        }

        public override void Emit(float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            if (IsFinished)
            {
                return;
            }

            time += frameTime;

            var nextStartTime = startTime.NextNumber(particleSystemState);

            if (time < nextStartTime)
            {
                return;
            }

            if (!snapshotResolved)
            {
                snapshotResolved = true;

                if (snapshotBinding.ResolveAttribute(particleSystemState, ParticleField.CreationTime) is float[] times && times.Length > 0)
                {
                    snapshotTimes = times;
                }
            }

            if (snapshotTimes != null)
            {
                if (snapshotCursor >= snapshotTimes.Length)
                {
                    IsFinished = true;
                    return;
                }

                remainingToEmit = CountDueSnapshotParticles();
            }
            else if (initFromKilledParentParticles > 0f)
            {
                remainingToEmit = ResolveEmitCount(particleSystemState);
            }
            else if (!countResolved)
            {
                countResolved = true;
                remainingToEmit = ResolveEmitCount(particleSystemState);
            }

            // A negative cap means "no authored limit", which resolves to the pool size rather than
            // to no limit at all, so an oversized burst drips out as slots free
            var perFrameCap = maxEmittedPerFrame >= 0
                ? maxEmittedPerFrame
                : Math.Min(particleSystemState.Data?.ParticleCapacity ?? 20000, 20000);

            // The burst budget is spent before the strength fade, so a faded frame loses its share outright
            var claimed = Math.Min(remainingToEmit, perFrameCap);
            remainingToEmit -= claimed;

            var numToEmit = (int)(claimed * strength);

            for (var i = 0; i < numToEmit; i++)
            {
                // Every particle is stamped at the start-time instant, offset by its snapshot release time
                var ageAtSpawn = time - nextStartTime;

                if (snapshotTimes != null)
                {
                    ageAtSpawn -= snapshotTimes[snapshotCursor];
                    snapshotCursor++;
                }

                particleEmitCallback?.Invoke(ageAtSpawn);
            }

            if (snapshotTimes == null && initFromKilledParentParticles <= 0f && remainingToEmit <= 0)
            {
                IsFinished = true;
            }
        }

        /// <summary>
        /// How many entries of a timed snapshot have come due but not yet been released.
        /// </summary>
        private int CountDueSnapshotParticles()
        {
            var due = 0;

            while (snapshotCursor + due < snapshotTimes!.Length && snapshotTimes[snapshotCursor + due] <= time)
            {
                due++;
            }

            return due;
        }

        /// <summary>
        /// The size of the burst. Emitting from a whole snapshot spawns one particle per snapshot
        /// element so each maps 1:1 to its snapshot index (<c>C_INIT_InitFromCPSnapshot</c> reads by
        /// particle id); a subset string selects a sub-range, which is unsupported, so the authored
        /// count stands there. Otherwise the authored count may be scaled by the parent's live
        /// particle count.
        /// </summary>
        private int ResolveEmitCount(ParticleSystemRenderState particleSystemState)
        {
            if (snapshotBinding.IsBound)
            {
                return snapshotBinding.Count(particleSystemState);
            }

            var count = emitCount.NextNumber(particleSystemState);
            var parentSystem = particleSystemState.ParentSystem;

            // Spawning from killed parents is a per-frame count rather than a one-shot burst
            if (initFromKilledParentParticles > 0f)
            {
                return (int)((parentSystem?.Data?.KilledLastPass ?? 0) * count * initFromKilledParentParticles);
            }

            if (!snapshotBinding.HasControlPoint && parentSystem != null)
            {
                var scale = parentParticleScale.NextNumber(particleSystemState);

                if (scale != -1f)
                {
                    count *= (parentSystem.Data?.CurrentParticles.Length ?? 0) * scale;
                }
            }

            return (int)count;
        }
    }
}
