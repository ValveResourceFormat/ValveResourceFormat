namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Drops a spawning particle onto the world by tracing from it along a direction, placing it on
    /// what the trace hits, lifted back by an offset.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_PositionPlaceOnGround">C_INIT_PositionPlaceOnGround</seealso>
    class PositionPlaceOnGround : ParticleFunctionInitializer
    {
        private readonly INumberProvider offset = new LiteralNumberProvider(0f);
        private readonly INumberProvider maxTraceLength = new LiteralNumberProvider(128f);
        private readonly IVectorProvider traceDirection = new LiteralVectorProvider(-Vector3.UnitZ);
        private readonly ParticleTraceMissBehavior missBehavior = ParticleTraceMissBehavior.PARTICLE_TRACE_MISS_BEHAVIOR_TRACE_END;
        private readonly ParticleField attribute = ParticleField.Position;
        private readonly bool setPreviousOnly;
        private readonly bool setNormal;
        private readonly ParticleField groundNormalAttribute = ParticleField.Normal;
        private readonly bool offsetOnCollisionOnly;
        private readonly float offsetByRadiusFactor;

        public PositionPlaceOnGround(ParticleDefinitionParser parse) : base(parse)
        {
            offset = parse.NumberProvider("m_flOffset", offset);
            maxTraceLength = parse.NumberProvider("m_flMaxTraceLength", maxTraceLength);
            traceDirection = parse.VectorProvider("m_vecTraceDir", traceDirection);
            missBehavior = parse.Enum("m_nTraceMissBehavior", missBehavior);
            attribute = parse.ParticleField("m_nAttribute", attribute);
            setPreviousOnly = parse.Boolean("m_bSetPXYZOnly", setPreviousOnly);
            setNormal = parse.Boolean("m_bSetNormal", setNormal);
            groundNormalAttribute = parse.ParticleField("m_nGroundNormalAttribute", groundNormalAttribute);
            offsetOnCollisionOnly = parse.Boolean("m_bOffsetonColOnly", offsetOnCollisionOnly);
            offsetByRadiusFactor = parse.Float("m_flOffsetByRadiusFactor", offsetByRadiusFactor);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious)
            | (attribute != ParticleField.Position ? FieldMask(attribute) : 0)
            | (setNormal ? FieldMask(groundNormalAttribute) : 0);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var direction = MathUtils.SafeNormalize(traceDirection.NextVector(ref particle, particleSystemState), -Vector3.UnitZ, 1e-12f);
            var start = particle.Position;
            var end = start + (direction * maxTraceLength.NextNumber(ref particle, particleSystemState));

            Vector3 placed;
            var hitGround = particleSystemState.Collision.TraceRay(start, end, out var hit);

            if (hitGround)
            {
                placed = hit.Position;

                if (setNormal)
                {
                    particle.SetVector(groundNormalAttribute, hit.Normal);
                }
            }
            else
            {
                switch (missBehavior)
                {
                    case ParticleTraceMissBehavior.PARTICLE_TRACE_MISS_BEHAVIOR_KILL:
                        particle.Kill();
                        return particle;
                    case ParticleTraceMissBehavior.PARTICLE_TRACE_MISS_BEHAVIOR_TRACE_END:
                        placed = end;
                        break;
                    default:
                        return particle;
                }
            }

            // The offset lifts the particle back along the trace, away from the surface
            if (hitGround || !offsetOnCollisionOnly)
            {
                placed -= direction * (offset.NextNumber(ref particle, particleSystemState) + (particle.Radius * offsetByRadiusFactor));
            }

            if (attribute != ParticleField.Position)
            {
                particle.SetVector(attribute, placed);
                return particle;
            }

            if (!setPreviousOnly)
            {
                particle.Position = placed;
            }

            particle.PositionPrevious = placed;

            return particle;
        }
    }
}
