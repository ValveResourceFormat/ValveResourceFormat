namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Drops a spawning particle onto the world by tracing from its attribute along a direction and moving
    /// the attribute onto what the trace hits, pulled back along the trace by an offset. Moving the position
    /// keeps the particle's velocity.
    /// </summary>
    /// <remarks>
    /// The offset applies whether or not the trace hit, and the radius offset is a plain pull-back by
    /// <c>m_flOffsetByRadiusFactor</c> times the radius, so <c>m_bOffsetonColOnly</c> has no effect.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_PositionPlaceOnGround">C_INIT_PositionPlaceOnGround</seealso>
    class PositionPlaceOnGround : ParticleFunctionInitializer
    {
        private const float StartBackoff = 0.1f;

        private readonly INumberProvider offset = new LiteralNumberProvider(0f);
        private readonly INumberProvider maxTraceLength = new LiteralNumberProvider(128f);
        private readonly IVectorProvider traceDirection = new LiteralVectorProvider(-Vector3.UnitZ);
        private readonly ParticleTraceMissBehavior missBehavior = ParticleTraceMissBehavior.PARTICLE_TRACE_MISS_BEHAVIOR_TRACE_END;
        private readonly ParticleField attribute = ParticleField.Position;
        private readonly bool setPreviousOnly;
        private readonly bool setNormal;
        private readonly ParticleField groundNormalAttribute = ParticleField.Normal;
        private readonly float offsetByRadiusFactor;
        private readonly int preserveOffsetControlPoint = -1;

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
            offsetByRadiusFactor = parse.Float("m_flOffsetByRadiusFactor", offsetByRadiusFactor);
            preserveOffsetControlPoint = parse.Int32("m_nPreserveOffsetCP", preserveOffsetControlPoint);
        }

        public override ulong WrittenFields => FieldMask(attribute) | FieldMask(ParticleField.LifeDuration)
            | (attribute == ParticleField.Position ? FieldMask(ParticleField.PositionPrevious) : 0)
            | (setNormal ? FieldMask(groundNormalAttribute) : 0);

        public override Particle Initialize(Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var collision = particleSystemState.Collision;
            var direction = MathUtils.SafeNormalize(traceDirection.NextVector(particle, particleSystemState));
            var value = particle.GetVector(attribute);
            var pullBack = offset.NextNumber(particle, particleSystemState);
            var length = maxTraceLength.NextNumber(particle, particleSystemState);

            var start = value - (direction * StartBackoff);
            var end = start + (direction * length);
            var hitGround = collision.TraceRay(start, end, out _, out var hitNormal, out var fraction);

            if (!hitGround)
            {
                switch (missBehavior)
                {
                    case ParticleTraceMissBehavior.PARTICLE_TRACE_MISS_BEHAVIOR_NONE:
                        return particle;
                    case ParticleTraceMissBehavior.PARTICLE_TRACE_MISS_BEHAVIOR_KILL:
                        particle.Lifetime = -1f;

                        if (setNormal)
                        {
                            particle.SetVector(groundNormalAttribute, hitNormal);
                        }

                        return particle;
                }
            }

            if (offsetByRadiusFactor != 0f)
            {
                pullBack += offsetByRadiusFactor * particle.Radius;
            }

            if (preserveOffsetControlPoint > -1)
            {
                pullBack += start.Z - particleSystemState.GetControlPoint(preserveOffsetControlPoint).Position.Z;
            }

            var delta = direction * ((length * fraction) - StartBackoff - pullBack);

            if (!setPreviousOnly)
            {
                particle.SetVector(attribute, value + delta);
            }

            if (attribute == ParticleField.Position)
            {
                particle.PositionPrevious += delta;
            }

            if (setNormal)
            {
                particle.SetVector(groundNormalAttribute, hitNormal);
            }

            return particle;
        }
    }
}
