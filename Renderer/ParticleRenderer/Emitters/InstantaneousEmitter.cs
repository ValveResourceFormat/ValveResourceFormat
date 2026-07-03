using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Emitters
{
    /// <summary>
    /// Emits a fixed number of particles in a single burst at a specified start time.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_InstantaneousEmitter">C_OP_InstantaneousEmitter</seealso>
    class InstantaneousEmitter : ParticleFunctionEmitter
    {
        public override bool IsFinished { get; protected set; }

        private Action? particleEmitCallback;

        private readonly INumberProvider emitCount = new LiteralNumberProvider(1);
        private readonly INumberProvider startTime = new LiteralNumberProvider(0);
        private readonly int snapshotControlPoint;
        private readonly bool hasSnapshotSubset;

        private float time;

        public InstantaneousEmitter(ParticleDefinitionParser parse) : base(parse)
        {
            emitCount = parse.NumberProvider("m_nParticlesToEmit", emitCount);
            startTime = parse.NumberProvider("m_flStartTime", startTime);
            snapshotControlPoint = parse.Int32("m_nSnapshotControlPoint", -1);
            hasSnapshotSubset = !string.IsNullOrEmpty(parse.Data.GetStringProperty("m_strSnapshotSubset"));
        }

        public override void Start(Action particleEmitCallback)
        {
            this.particleEmitCallback = particleEmitCallback;

            IsFinished = false;

            time = 0;
        }

        public override void Stop()
        {
            IsFinished = true;
            particleEmitCallback = null;
        }

        public override void Emit(float frameTime, ParticleSystemRenderState particleSystemState)
        {
            if (IsFinished)
            {
                return;
            }

            time += frameTime;

            if (time >= startTime.NextNumber(particleSystemState))
            {
                var numToEmit = (int)emitCount.NextNumber(particleSystemState);

                // When emitting from a whole snapshot, spawn one particle per snapshot element so each maps
                // 1:1 to its snapshot index (C_INIT_InitFromCPSnapshot reads by particle id). A subset string
                // selects a sub-range, which we don't support, so leave the literal count in that case.
                if (snapshotControlPoint >= 0 && !hasSnapshotSubset)
                {
                    var snapshot = particleSystemState.GetControlPointSnapshot(snapshotControlPoint);
                    if (snapshot != null)
                    {
                        numToEmit = (int)snapshot.NumParticles;
                    }
                }

                for (var i = 0; i < numToEmit; i++)
                {
                    particleEmitCallback?.Invoke();
                }

                IsFinished = true;
            }
        }
    }
}
