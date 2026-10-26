using ValveResourceFormat.IO;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Drops a spawning particle onto the world by tracing from its attribute along a direction and moving
    /// the attribute onto what the trace hits, pulled back along the trace by an offset. Moving the position
    /// keeps the particle's velocity.
    /// </summary>
    /// <remarks>
    /// The offset applies whether or not the trace hit. The radius offset is a pull-back by
    /// <c>m_flOffsetByRadiusFactor</c> times the radius, unless the game pushes particles off what they hit
    /// (<see cref="GameInfo.ParticleTraceOffsetOnlyHit"/>): then it also moves the particle along
    /// the hit normal turned halfway toward the reflected trace, shortens the trace by half the radius, kills
    /// the particle when that way out is blocked, and with <c>m_bOffsetonColOnly</c> applies only after a hit.
    /// The trace direction is evaluated once per call, for the first particle of the collection, and
    /// every particle traces along it.
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
        private readonly bool offsetOnCollisionOnly;
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
            offsetOnCollisionOnly = parse.Boolean("m_bOffsetonColOnly", offsetOnCollisionOnly);
            preserveOffsetControlPoint = parse.Int32("m_nPreserveOffsetCP", preserveOffsetControlPoint);
        }

        public override ulong WrittenFields => attribute.Mask() | ParticleField.LifeDuration.Mask()
            | (attribute == ParticleField.Position ? ParticleField.PositionPrevious.Mask() : 0)
            | (setNormal ? groundNormalAttribute.Mask() : 0);

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var pullBacks = offset.Fill(run, particleSystemState);
            var lengths = maxTraceLength.Fill(run, particleSystemState);
            var direction = MathUtils.SafeNormalize(traceDirection.NextVector(particles.Current, 0, particleSystemState));

            var collision = particleSystemState.Collision;
            var lifetimes = run.Floats(ParticleField.LifeDuration);
            var radii = offsetByRadiusFactor != 0f ? run.ReadFloats(ParticleField.Radius) : default;
            var writableValues = ParticleBuffer.IsVector(attribute) ? run.Vectors(attribute) : default;
            var values = writableValues.IsEmpty ? run.ReadVectors(attribute) : writableValues;
            var previousPositions = attribute == ParticleField.Position ? run.Vectors(ParticleField.PositionPrevious) : default;
            var normals = setNormal && ParticleBuffer.IsVector(groundNormalAttribute) ? run.Vectors(groundNormalAttribute) : default;
            var preservedHeight = preserveOffsetControlPoint > -1 ? particleSystemState.GetControlPoint(preserveOffsetControlPoint).Position.Z : 0f;
            var offsetOnlyHit = particleSystemState.Data?.FileLoader is GameFileLoader { GameInfo.ParticleTraceOffsetOnlyHit: true };

            for (var i = 0; i < values.Length; i++)
            {
                var value = values[i];
                var pullBack = pullBacks[i];
                var length = lengths[i];

                var start = value - (direction * StartBackoff);
                var end = start + (direction * length);

                // The trace set and collision group are not parsed, so the trace hits all geometry.
                // A ray that starts inside a solid is not reported as a hit at its start, so only a hit further along the ray counts.
                var hitGround = collision.TraceRay(start, end, out var hitPosition, out var hitNormal, out var fraction);

                if (!hitGround)
                {
                    if (missBehavior == ParticleTraceMissBehavior.PARTICLE_TRACE_MISS_BEHAVIOR_NONE)
                    {
                        continue;
                    }

                    if (missBehavior == ParticleTraceMissBehavior.PARTICLE_TRACE_MISS_BEHAVIOR_KILL)
                    {
                        lifetimes[i] = -1f;

                        if (!normals.IsEmpty)
                        {
                            normals[i] = hitNormal;
                        }

                        continue;
                    }
                }

                var pushOff = Vector3.Zero;
                var skipsRadiusOffset = offsetOnlyHit && offsetOnCollisionOnly && fraction == 1f;

                if (offsetByRadiusFactor != 0f && !skipsRadiusOffset)
                {
                    var radius = radii[i];

                    if (offsetOnlyHit)
                    {
                        (fraction, pushOff) = PushOffSurface(direction, length, fraction, hitNormal, radius);

                        if (collision.TraceRay(hitPosition, hitPosition + (pushOff * 0.5f), out _, out _, out _))
                        {
                            lifetimes[i] = -1f;
                        }
                    }

                    pullBack += offsetByRadiusFactor * radius;
                }

                if (preserveOffsetControlPoint > -1)
                {
                    pullBack += start.Z - preservedHeight;
                }

                var delta = (direction * ((length * fraction) - StartBackoff - pullBack)) + pushOff;

                if (!setPreviousOnly && !writableValues.IsEmpty)
                {
                    writableValues[i] = value + delta;
                }

                if (!previousPositions.IsEmpty)
                {
                    previousPositions[i] += delta;
                }

                if (!normals.IsEmpty)
                {
                    normals[i] = hitNormal;
                }
            }
        }

        /// <summary>
        /// Shortens the trace so the particle stops half its radius short of the surface, measured against how
        /// steeply the trace meets it, and gives the radius offset along the hit normal turned halfway toward the
        /// reflected trace.
        /// </summary>
        private (float Fraction, Vector3 Offset) PushOffSurface(Vector3 direction, float length, float fraction, Vector3 hitNormal, float radius)
        {
            var along = Vector3.Dot(hitNormal, direction);
            var reflected = direction - ((along + along) * hitNormal);
            var reach = MathF.Max(-along * length, radius);
            var halfRadius = radius * 0.5f;
            var shortened = reach * fraction <= halfRadius ? 0f : fraction - (halfRadius / reach);
            var away = MathUtils.SafeNormalize((MathUtils.SafeNormalize(reflected) * 0.5f) + hitNormal);

            return (shortened, away * radius * offsetByRadiusFactor);
        }
    }
}
