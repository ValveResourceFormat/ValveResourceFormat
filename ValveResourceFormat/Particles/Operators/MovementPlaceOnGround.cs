namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Keeps particles resting on the world as they move, tracing from a little behind each one along a
    /// direction and moving it onto what the trace hits. The move keeps the particle's velocity.
    /// </summary>
    /// <remarks>
    /// With reference control points and a tolerance, particles are retraced only when one of those
    /// control points moves by more than the tolerance. With an interpolation rate or control point,
    /// particles move from their previous trace target to the new one along the trace axes: over
    /// <c>m_flLerpRate</c> seconds, retracing whenever that completes, or by how far the interpolation
    /// control point has moved relative to the tolerance. The targets are kept in the initial previous
    /// position and the hitbox relative position.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_MovementPlaceOnGround">C_OP_MovementPlaceOnGround</seealso>
    class MovementPlaceOnGround : ParticleFunctionOperator
    {
        private readonly INumberProvider offset = new LiteralNumberProvider(0f);
        private readonly float maxTraceLength = 128f;
        private readonly float tolerance = 32f;
        private readonly IVectorProvider traceDirection = new LiteralVectorProvider(-Vector3.UnitZ);
        private readonly float traceOffset = 64f;
        private readonly float lerpRate;
        private readonly int referenceControlPoint1 = -1;
        private readonly int referenceControlPoint2 = -1;
        private readonly int lerpControlPoint = -1;
        private readonly ParticleTraceMissBehavior missBehavior = ParticleTraceMissBehavior.PARTICLE_TRACE_MISS_BEHAVIOR_TRACE_END;
        private readonly bool setNormal;
        private readonly bool scaleOffset;
        private readonly int preserveOffsetControlPoint = -1;

        private readonly bool retraceOnControlPointMove;
        private readonly bool interpolates;

        private Vector3 lastReferencePosition1;
        private Vector3 lastReferencePosition2;
        private Vector3 lastLerpPosition;
        private float lastRetraceTime;

        public MovementPlaceOnGround(ParticleDefinitionParser parse) : base(parse)
        {
            offset = parse.NumberProvider("m_flOffset", offset);
            maxTraceLength = parse.Float("m_flMaxTraceLength", maxTraceLength);
            tolerance = parse.Float("m_flTolerance", tolerance);
            traceDirection = parse.VectorProvider("m_vecTraceDir", traceDirection);
            traceOffset = parse.Float("m_flTraceOffset", traceOffset);
            lerpRate = parse.Float("m_flLerpRate", lerpRate);
            referenceControlPoint1 = parse.Int32("m_nRefCP1", referenceControlPoint1);
            referenceControlPoint2 = parse.Int32("m_nRefCP2", referenceControlPoint2);
            lerpControlPoint = parse.Int32("m_nLerpCP", lerpControlPoint);
            missBehavior = parse.Enum("m_nTraceMissBehavior", missBehavior);
            setNormal = parse.Boolean("m_bSetNormal", setNormal);
            scaleOffset = parse.Boolean("m_bScaleOffset", scaleOffset);
            preserveOffsetControlPoint = parse.Int32("m_nPreserveOffsetCP", preserveOffsetControlPoint);

            retraceOnControlPointMove = (referenceControlPoint1 > -1 || referenceControlPoint2 > -1 || lerpControlPoint > -1) && tolerance > 0f;
            interpolates = lerpControlPoint > -1 || lerpRate > 0f;

            parse.AddInitialRead(ParticleField.Position);
            parse.AddInitialRead(ParticleField.PositionPrevious);

            if (preserveOffsetControlPoint > -1)
            {
                parse.AddInitialRead(ParticleField.HitboxOffsetPosition);
            }
        }

        public override void Reset()
        {
            lastReferencePosition1 = new Vector3(float.MaxValue);
            lastReferencePosition2 = new Vector3(float.MaxValue);
            lastLerpPosition = new Vector3(float.MaxValue);
            lastRetraceTime = 0f;
        }

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask() | ParticleField.LifeDuration.Mask()
            | ParticleField.HitboxOffsetPosition.Mask() | ParticleField.Normal.Mask();

        /// <inheritdoc/>
        public override ulong InitialWrittenFields => ParticleField.PositionPrevious.Mask() | ParticleField.HitboxOffsetPosition.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;
            var offsets = offset.Fill(current, particleSystemState);
            var directions = traceDirection.Prepare(current, particleSystemState);

            var retrace = false;

            if (retraceOnControlPointMove)
            {
                if (ControlPointMoved(particleSystemState, referenceControlPoint1, ref lastReferencePosition1))
                {
                    retrace = true;
                    lastRetraceTime = particleSystemState.Age;
                }

                if (ControlPointMoved(particleSystemState, referenceControlPoint2, ref lastReferencePosition2))
                {
                    retrace = true;
                    lastRetraceTime = particleSystemState.Age;
                }

                ControlPointMoved(particleSystemState, lerpControlPoint, ref lastLerpPosition);
            }
            else if (!interpolates)
            {
                retrace = true;
            }

            var interpolation = 0f;

            if (interpolates)
            {
                if (lerpRate > 0f)
                {
                    interpolation = MathUtils.RemapValClamped(particleSystemState.Age, lastRetraceTime, lastRetraceTime + lerpRate, 0f, 1f);

                    if (interpolation == 1f && !retraceOnControlPointMove)
                    {
                        retrace = true;
                        lastRetraceTime = particleSystemState.Age;
                    }
                }
                else if (lerpControlPoint > -1)
                {
                    var moved = Vector3.Distance(particleSystemState.GetControlPoint(lerpControlPoint).Position, lastLerpPosition) / tolerance;
                    interpolation = float.IsNaN(moved) ? 0f : MathF.Min(1f, moved);
                }
            }
            else if (!retrace)
            {
                return;
            }

            var collision = particleSystemState.Collision;
            var preserveOffsetHeight = preserveOffsetControlPoint > -1
                ? particleSystemState.GetControlPoint(preserveOffsetControlPoint).Position.Z
                : 0f;

            var positions = current.Vectors(ParticleField.Position);
            var previousPositions = current.Vectors(ParticleField.PositionPrevious);
            var hitboxOffsets = current.Vectors(ParticleField.HitboxOffsetPosition);
            var lifetimes = current.Floats(ParticleField.LifeDuration);
            var normals = setNormal ? current.Vectors(ParticleField.Normal) : default;
            var radii = scaleOffset ? current.ReadFloats(ParticleField.Radius) : default;
            var initialPreviousPositions = interpolates ? particles.Initial.Vectors(ParticleField.PositionPrevious) : default;
            var initialHitboxOffsets = preserveOffsetControlPoint > -1 ? particles.Initial.Vectors(ParticleField.HitboxOffsetPosition) : default;
            var initialPositions = preserveOffsetControlPoint > -1 ? particles.Initial.ReadVectors(ParticleField.Position) : default;

            current.FillPerBlockInputs(particleSystemState, traceDirection, directions);

            for (var i = 0; i < positions.Length; i++)
            {
                var direction = MathUtils.SafeNormalize(directions[i]);
                var position = positions[i];
                var velocityStep = position - previousPositions[i];
                var from = interpolates ? initialPreviousPositions[i] : Vector3.Zero;
                var to = hitboxOffsets[i];

                var lift = offsets[i];

                if (scaleOffset)
                {
                    lift *= radii[i];
                }

                var traceLength = missBehavior == ParticleTraceMissBehavior.PARTICLE_TRACE_MISS_BEHAVIOR_NONE
                    ? maxTraceLength
                    : MathF.Max(maxTraceLength, lift + traceOffset);

                if (preserveOffsetControlPoint > -1)
                {
                    var preserved = initialHitboxOffsets[i];

                    if (preserved.X == 0f)
                    {
                        preserved = new Vector3(1f, preserved.Y, initialPositions[i].Z - preserveOffsetHeight);
                        initialHitboxOffsets[i] = preserved;
                    }

                    lift += preserved.Z;
                }

                if (interpolates && to == Vector3.Zero)
                {
                    retrace = true;
                    from = position;
                    to = position;
                }

                if (retrace)
                {
                    var start = position - (direction * traceOffset);
                    var end = start + (direction * traceLength);
                    var hitGround = collision.TraceRay(start, end, out var hitPosition, out var hitNormal, out _);

                    if (!hitGround && missBehavior == ParticleTraceMissBehavior.PARTICLE_TRACE_MISS_BEHAVIOR_NONE)
                    {
                        continue;
                    }

                    if (!hitGround && missBehavior == ParticleTraceMissBehavior.PARTICLE_TRACE_MISS_BEHAVIOR_KILL)
                    {
                        lifetimes[i] = -1f;
                    }
                    else
                    {
                        var target = hitPosition + (setNormal ? hitNormal * lift : -direction * lift);

                        if (setNormal)
                        {
                            normals[i] = hitNormal;
                        }

                        if (!interpolates)
                        {
                            positions[i] = target;
                            previousPositions[i] = target - velocityStep;
                            continue;
                        }

                        initialPreviousPositions[i] = to;
                        hitboxOffsets[i] = target;
                        from = to;
                        to = target;
                    }
                }

                if (interpolates)
                {
                    var axes = Vector3.Abs(direction);
                    var placed = (Vector3.Lerp(from, to, interpolation) * axes) + ((Vector3.One - axes) * position);

                    positions[i] = placed;
                    previousPositions[i] = placed - velocityStep;
                }
            }
        }

        private bool ControlPointMoved(ParticleSystemState particleSystemState, int controlPoint, ref Vector3 lastPosition)
        {
            if (controlPoint < 0)
            {
                return false;
            }

            var position = particleSystemState.GetControlPoint(controlPoint).Position;

            if (Vector3.DistanceSquared(position, lastPosition) <= tolerance * tolerance)
            {
                return false;
            }

            lastPosition = position;
            return true;
        }
    }
}
