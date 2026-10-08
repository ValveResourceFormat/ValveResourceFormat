namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Drops a spawning particle onto the world by tracing from its attribute along a direction and moving
    /// the attribute onto what the trace hits, pulled back along the trace by an offset. Moving the position
    /// keeps the particle's velocity.
    /// </summary>
    /// <remarks>
    /// Follows CS2, whose gameinfo sets <c>Particles/ParticleTraceOffsetOnlyHit</c>: the offset applies
    /// whether or not the trace hit, and <c>m_bOffsetonColOnly</c> only keeps the radius offset off a miss.
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
        private readonly bool offsetOnCollisionOnly;
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
            offsetOnCollisionOnly = parse.Boolean("m_bOffsetonColOnly", offsetOnCollisionOnly);
            offsetByRadiusFactor = parse.Float("m_flOffsetByRadiusFactor", offsetByRadiusFactor);
            preserveOffsetControlPoint = parse.Int32("m_nPreserveOffsetCP", preserveOffsetControlPoint);
        }

        public override ulong WrittenFields => FieldMask(attribute) | FieldMask(ParticleField.LifeDuration)
            | (attribute == ParticleField.Position ? FieldMask(ParticleField.PositionPrevious) : 0)
            | (setNormal ? FieldMask(groundNormalAttribute) : 0);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var collision = particleSystemState.Collision;
            var direction = MathUtils.SafeNormalize(traceDirection.NextVector(ref particle, particleSystemState));
            var value = particle.GetVector(attribute);
            var pullBack = offset.NextNumber(ref particle, particleSystemState);
            var length = maxTraceLength.NextNumber(ref particle, particleSystemState);

            var start = value - (direction * StartBackoff);
            var end = start + (direction * length);
            var hitGround = collision.TraceRay(start, end, out var hit);
            var fraction = hitGround ? hit.Fraction : 1f;

            if (!hitGround)
            {
                switch (missBehavior)
                {
                    case ParticleTraceMissBehavior.PARTICLE_TRACE_MISS_BEHAVIOR_NONE:
                        return particle;
                    case ParticleTraceMissBehavior.PARTICLE_TRACE_MISS_BEHAVIOR_KILL:
                        particle.Kill();
                        SetGroundNormal(ref particle, hit.Normal);
                        return particle;
                }
            }

            var radiusOffset = Vector3.Zero;

            if (offsetByRadiusFactor != 0f && (!offsetOnCollisionOnly || fraction != 1f))
            {
                var radius = particle.Radius;
                var facing = Vector3.Dot(direction, hit.Normal);
                var reach = MathF.Max(-facing * length, radius);

                fraction = reach * fraction <= radius * 0.5f ? 0f : fraction - (radius * 0.5f / reach);

                var away = MathUtils.SafeNormalize((MathUtils.SafeNormalize(Vector3.Reflect(direction, hit.Normal)) * 0.5f) + hit.Normal);
                radiusOffset = away * radius * offsetByRadiusFactor;

                var probeStart = hitGround ? hit.Position : end;

                if (collision.TraceRay(probeStart, probeStart + (radiusOffset * 0.5f), out _))
                {
                    particle.Kill();
                }

                pullBack += radius * offsetByRadiusFactor;
            }

            if (preserveOffsetControlPoint > -1)
            {
                pullBack += start.Z - particleSystemState.GetControlPoint(preserveOffsetControlPoint).Position.Z;
            }

            var delta = (direction * ((length * fraction) - StartBackoff - pullBack)) + radiusOffset;

            if (!setPreviousOnly)
            {
                particle.SetVector(attribute, value + delta);
            }
            else if (attribute == ParticleField.Position && particles.CurrentFrameTime > 0f)
            {
                // The previous position is rebuilt from the velocity once the initializers have run
                particle.Velocity -= delta / particles.CurrentFrameTime;
            }

            SetGroundNormal(ref particle, hit.Normal);

            return particle;
        }

        private void SetGroundNormal(ref Particle particle, Vector3 normal)
        {
            if (setNormal)
            {
                particle.SetVector(groundNormalAttribute, normal);
            }
        }
    }
}
