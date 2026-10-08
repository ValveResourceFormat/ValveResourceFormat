using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Keeps particles resting on the world as they move, tracing from a little above each one along a
    /// direction and moving it onto what the trace hits.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_MovementPlaceOnGround">C_OP_MovementPlaceOnGround</seealso>
    class MovementPlaceOnGround : ParticleFunctionOperator
    {
        private readonly INumberProvider offset = new LiteralNumberProvider(0f);
        private readonly float maxTraceLength = 128f;
        private readonly IVectorProvider traceDirection = new LiteralVectorProvider(-Vector3.UnitZ);
        private readonly float traceOffset = 64f;
        private readonly float lerpRate;
        private readonly ParticleTraceMissBehavior missBehavior = ParticleTraceMissBehavior.PARTICLE_TRACE_MISS_BEHAVIOR_TRACE_END;
        private readonly bool setNormal;
        private readonly bool scaleOffset;

        public MovementPlaceOnGround(ParticleDefinitionParser parse) : base(parse)
        {
            offset = parse.NumberProvider("m_flOffset", offset);
            maxTraceLength = parse.Float("m_flMaxTraceLength", maxTraceLength);
            traceDirection = parse.VectorProvider("m_vecTraceDir", traceDirection);
            traceOffset = parse.Float("m_flTraceOffset", traceOffset);
            lerpRate = parse.Float("m_flLerpRate", lerpRate);
            missBehavior = parse.Enum("m_nTraceMissBehavior", missBehavior);
            setNormal = parse.Boolean("m_bSetNormal", setNormal);
            scaleOffset = parse.Boolean("m_bScaleOffset", scaleOffset);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var collision = particleSystemState.Collision;
            var interpolation = lerpRate > 0f ? MathUtils.Saturate(lerpRate * frameTime) : 1f;

            foreach (ref var particle in particles.Current)
            {
                var direction = MathUtils.SafeNormalize(traceDirection.NextVector(ref particle, particleSystemState), -Vector3.UnitZ, ParticleMath.MinimumLengthSquared);

                // Starting back from the particle lets it climb onto ground that rose under it
                var start = particle.Position - (direction * traceOffset);
                var end = start + (direction * maxTraceLength);

                Vector3 target;

                if (collision.TraceRay(start, end, out var hit))
                {
                    target = hit.Position;

                    if (setNormal)
                    {
                        particle.SetVector(ParticleField.Normal, hit.Normal);
                    }
                }
                else if (missBehavior == ParticleTraceMissBehavior.PARTICLE_TRACE_MISS_BEHAVIOR_KILL)
                {
                    particle.Kill();
                    continue;
                }
                else if (missBehavior == ParticleTraceMissBehavior.PARTICLE_TRACE_MISS_BEHAVIOR_TRACE_END)
                {
                    target = end;
                }
                else
                {
                    continue;
                }

                var lift = offset.NextNumber(ref particle, particleSystemState);
                target -= direction * (scaleOffset ? lift * particle.Radius : lift);

                // Moving the previous position along keeps the snap from reading as velocity
                var delta = (target - particle.Position) * interpolation * strength;
                particle.Position += delta;
                particle.PositionPrevious += delta;
            }
        }
    }
}
