using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Emitters
{
    /// <summary>
    /// Emits particles at the specified rate over time. By default (a duration of 0), the emitter
    /// continues to emit forever.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ContinuousEmitter">C_OP_ContinuousEmitter</seealso>
    class ContinuousEmitter : ParticleFunctionEmitter
    {
        public override bool IsFinished { get; protected set; }

        /// <summary>Length of time to continue emitting particles (seconds).</summary>
        private readonly INumberProvider emissionDuration = new LiteralNumberProvider(0);

        /// <summary>Time at which to begin emitting particles (seconds).</summary>
        private readonly INumberProvider startTime = new LiteralNumberProvider(0);

        /// <summary>Number of particles to spawn (per second).</summary>
        private readonly INumberProvider emitRate = new LiteralNumberProvider(100);

        /// <summary>
        /// Scales the rate by the number of rows in a snapshot. A bound control point that carries no
        /// snapshot scales the rate to zero, so the emitter stays silent until one arrives.
        /// </summary>
        private readonly SnapshotBinding snapshotBinding;

        /// <summary>
        /// Scales the rate by the number of control points in use (the highest index plus one). Only
        /// read below behavior version 2, and ignored when <see cref="scalePerParentParticle"/> is set.
        /// </summary>
        private readonly float emissionScale;

        /// <summary>Scales the rate by the parent system's live particle count, or by itself for a system without a parent.</summary>
        private readonly float scalePerParentParticle;

        /// <summary>Most particles spawned per update when positive. The excess stays due for later updates.</summary>
        private readonly int limitPerUpdate;

        private readonly bool forceEmitOnFirstUpdate;
        private readonly bool forceEmitOnLastUpdate;

        private Action<float>? particleEmitCallback;

        private EmissionAccumulator accumulator;

        /// <summary>Whether a stop is waiting for the one last update that spawns at least one particle.</summary>
        private bool finalEmitPending;

        public ContinuousEmitter(ParticleDefinitionParser parse) : base(parse)
        {
            emissionDuration = parse.NumberProvider("m_flEmissionDuration", emissionDuration);
            startTime = parse.NumberProvider("m_flStartTime", startTime);
            emitRate = parse.NumberProvider("m_flEmitRate", emitRate);
            snapshotBinding = new SnapshotBinding(parse);

            if (parse.BehaviorVersion < 2)
            {
                emissionScale = MathF.Max(0f, parse.Float("m_flEmissionScale", emissionScale));
            }

            scalePerParentParticle = parse.Float("m_flScalePerParentParticle", scalePerParentParticle);
            limitPerUpdate = parse.Int32("m_nLimitPerUpdate", limitPerUpdate);
            forceEmitOnFirstUpdate = parse.Boolean("m_bForceEmitOnFirstUpdate", forceEmitOnFirstUpdate);
            forceEmitOnLastUpdate = parse.Boolean("m_bForceEmitOnLastUpdate", forceEmitOnLastUpdate);
        }

        protected override void OnStart(Action<float> particleEmitCallback)
        {
            this.particleEmitCallback = particleEmitCallback;

            accumulator.Reset(initialCharge: 0d, flushEpsilon: 0.001f);

            IsFinished = false;
            finalEmitPending = false;
        }

        public override void Stop()
        {
            if (forceEmitOnLastUpdate && !IsFinished)
            {
                finalEmitPending = true;
                return;
            }

            IsFinished = true;
            particleEmitCallback = null;
        }

        public override void Emit(float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (IsFinished)
            {
                return;
            }

            var elapsed = particleSystemState.Age - StartAge;
            var frameStart = elapsed - frameTime;

            var nextStartTime = startTime.NextNumber(particleSystemState);
            var nextEmissionDuration = emissionDuration.NextNumber(particleSystemState);

            if (TryGetEmissionWindow(frameStart, elapsed, nextStartTime, nextEmissionDuration, out var windowStart, out var windowEnd))
            {
                // Re-evaluate the emit rate every frame: a control-point or curve-driven
                // rate changes over the emitter's lifetime.
                var rate = emitRate.NextNumber(particleSystemState) * strength;

                if (scalePerParentParticle > 0f)
                {
                    var parentCount = particleSystemState.ParentSystem is { } parentSystem
                        ? parentSystem.Data?.CurrentParticles.Length ?? 0
                        : 1;

                    rate *= parentCount * scalePerParentParticle;
                }
                else if (emissionScale > 0f)
                {
                    rate *= (particleSystemState.HighestControlPoint + 1) * emissionScale;
                }

                if (snapshotBinding.IsBound)
                {
                    rate *= snapshotBinding.Count(particleSystemState);
                }

                if (rate > 0f)
                {
                    var isFirstUpdate = particleSystemState.Age - frameTime == 0f;
                    var minimum = finalEmitPending || (forceEmitOnFirstUpdate && isFirstUpdate) ? 1 : 0;

                    accumulator.Charge(rate, windowStart, windowEnd, elapsed, particleEmitCallback, minimum, limitPerUpdate);

                    if (finalEmitPending)
                    {
                        finalEmitPending = false;
                        IsFinished = true;
                        particleEmitCallback = null;
                        return;
                    }
                }
            }

            if (nextEmissionDuration != 0f && elapsed > nextStartTime + nextEmissionDuration)
            {
                IsFinished = true;
            }
        }
    }
}
