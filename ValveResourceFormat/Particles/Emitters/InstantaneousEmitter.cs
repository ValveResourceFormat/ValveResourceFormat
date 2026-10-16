using ValveResourceFormat.Blocks;
using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Emitters
{
    /// <summary>
    /// Emits a fixed number of particles in a single burst at a specified start time.
    /// </summary>
    /// <remarks>
    /// With <c>m_flInitFromKilledParentParticles</c> it emits a burst for every parent particle event of
    /// <c>m_nEventType</c> instead, at the moment the parent raises it. With a snapshot control point
    /// the burst is one particle per snapshot row, each released once the system reaches the row's
    /// creation time.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_InstantaneousEmitter">C_OP_InstantaneousEmitter</seealso>
    class InstantaneousEmitter : ParticleFunctionEmitter
    {
        private readonly INumberProvider emitCount = new LiteralNumberProvider(100);
        private readonly INumberProvider startTime = new LiteralNumberProvider(0);

        /// <summary>
        /// Multiplies the burst by the parent system's live particle count. -1 leaves it unscaled.
        /// </summary>
        private readonly INumberProvider parentParticleScale = new LiteralNumberProvider(-1);

        private readonly int maxEmittedPerFrame = -1;

        /// <summary>Scales the burst by how many parent particles raised the event.</summary>
        private readonly float initFromKilledParentParticles;
        private readonly ParticleEventType eventType = ParticleEventType.PARTICLE_EVENT_TYPE_MASK_KILLED;
        private readonly SnapshotBinding snapshotBinding;
        private readonly bool resetSnapshotIndexOnChanges;

        private int remainingToEmit;
        private int baseCount;
        private float emitTime = -1f;
        private float timeBase;
        private bool burstComputed;
        private bool emitting = true;

        /// <summary>The next snapshot row the walk releases.</summary>
        private int snapshotIndex;

        /// <summary>The next snapshot row whose creation time an emitted particle takes.</summary>
        private int snapshotEmitIndex;
        private ParticleSnapshot? walkedSnapshot;

        public InstantaneousEmitter(ParticleDefinitionParser parse) : base(parse)
        {
            emitCount = parse.NumberProvider("m_nParticlesToEmit", emitCount);
            startTime = parse.NumberProvider("m_flStartTime", startTime);
            parentParticleScale = parse.NumberProvider("m_flParentParticleScale", parentParticleScale);
            maxEmittedPerFrame = parse.Int32("m_nMaxEmittedPerFrame", maxEmittedPerFrame);
            initFromKilledParentParticles = parse.Float("m_flInitFromKilledParentParticles", initFromKilledParentParticles);
            eventType = parse.Enum("m_nEventType", eventType);
            snapshotBinding = new SnapshotBinding(parse);
            resetSnapshotIndexOnChanges = parse.Boolean("m_bResetSnapshotIndexOnChanges", resetSnapshotIndexOnChanges);

            if (resetSnapshotIndexOnChanges)
            {
                startTime = new LiteralNumberProvider(0f);
            }
        }

        private bool IsEventEmitter => initFromKilledParentParticles > 0f;

        public override ParticleEventType EventTypeMask => IsEventEmitter ? eventType : ParticleEventType.PARTICLE_EVENT_TYPE_MASK_NONE;

        public override void Reset()
        {
            remainingToEmit = 0;
            baseCount = 0;
            emitTime = -1f;
            timeBase = 0f;
            burstComputed = false;
            emitting = true;
            snapshotIndex = 0;
            snapshotEmitIndex = 0;
            walkedSnapshot = null;
        }

        public override void Restart(ParticleSystemState particleSystemState)
        {
            var isEndCapChild = particleSystemState.Data?.IsEndCapChild ?? false;

            timeBase = isEndCapChild ? 0f : particleSystemState.Age;
            remainingToEmit = 0;
            emitTime = -1f;
            burstComputed = false;
            snapshotIndex = 0;
            snapshotEmitIndex = 0;
        }

        public override void StartEmission()
        {
            emitting = true;
        }

        public override void StopEmission()
        {
            emitting = false;
        }

        public override bool MayCreateMoreParticles(ParticleSystemState particleSystemState)
            => emitting && !IsEventEmitter && (!burstComputed || remainingToEmit > 0);

        private void UpdateParticlesToEmit(ParticleSystemState particleSystemState)
        {
            var time = particleSystemState.Age;

            if (IsEventEmitter)
            {
                if (!burstComputed)
                {
                    burstComputed = true;
                    baseCount = (int)emitCount.NextNumber(particleSystemState);
                }

                var events = particleSystemState.ParentSystem != null ? particleSystemState.EmitterEventCount : 0;
                remainingToEmit = (int)(events * (baseCount * initFromKilledParentParticles));
                emitTime = time;
                return;
            }

            emitTime = timeBase + startTime.NextNumber(particleSystemState);

            if (emitTime > time)
            {
                return;
            }

            if (!snapshotBinding.IsBound)
            {
                if (burstComputed)
                {
                    return;
                }

                var scale = 1f;

                if (particleSystemState.ParentSystem?.Data is { } parent)
                {
                    var parentScale = parentParticleScale.NextNumber(particleSystemState);

                    if (parentScale != -1f)
                    {
                        scale = parent.Particles.Count * parentScale;
                    }
                }

                baseCount = (int)(scale * emitCount.NextNumber(particleSystemState));
                remainingToEmit = Math.Max(baseCount, 0);
                burstComputed = true;
                return;
            }

            var snapshot = snapshotBinding.Resolve(particleSystemState);

            if (resetSnapshotIndexOnChanges && !ReferenceEquals(snapshot, walkedSnapshot))
            {
                snapshotIndex = 0;
                snapshotEmitIndex = 0;
            }

            walkedSnapshot = snapshot;

            if (snapshot == null)
            {
                return;
            }

            var count = (int)snapshot.NumParticles;
            var creationTimes = SnapshotCreationTimes(particleSystemState);
            var elapsed = time - timeBase;

            while (snapshotIndex < count && (creationTimes == null || snapshotIndex >= creationTimes.Length || creationTimes[snapshotIndex] <= elapsed))
            {
                snapshotIndex++;
                remainingToEmit++;
            }
        }

        private float[]? SnapshotCreationTimes(ParticleSystemState particleSystemState)
            => snapshotBinding.ResolveAttribute(particleSystemState, ParticleField.CreationTime) as float[];

        public override void Emit(ParticleSystemState particleSystemState, float strength, Action<float> emit)
        {
            if (!emitting)
            {
                return;
            }

            UpdateParticlesToEmit(particleSystemState);

            if (emitTime > particleSystemState.Age || remainingToEmit <= 0)
            {
                return;
            }

            var perFrameCap = maxEmittedPerFrame >= 0
                ? maxEmittedPerFrame
                : particleSystemState.Data?.ParticleCapacity ?? ParticleCollection.MAX_PARTICLES;

            var claimed = Math.Min(perFrameCap, remainingToEmit);
            remainingToEmit -= claimed;

            var count = Math.Min(FreeSlots(particleSystemState), (int)(claimed * strength));
            var creationTimes = snapshotBinding.IsBound ? SnapshotCreationTimes(particleSystemState) : null;

            for (var i = 0; i < count; i++)
            {
                var creationTime = emitTime;

                if (creationTimes != null && snapshotEmitIndex < creationTimes.Length)
                {
                    creationTime += creationTimes[snapshotEmitIndex];
                }

                snapshotEmitIndex++;
                emit(creationTime);
            }
        }

        public override void SkipToTime(float time, ParticleSystemSimulation simulation)
        {
            var state = simulation.RenderState;

            if (IsEventEmitter || startTime.NextNumber(state) + timeBase >= time)
            {
                return;
            }

            simulation.Resimulate(state.Age - time, time);
        }
    }
}
