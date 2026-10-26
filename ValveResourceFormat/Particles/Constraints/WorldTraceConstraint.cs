using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// Collides particles with the world. Each particle's movement over the step is tested against the
    /// surfaces its collision mode finds, and a particle that crosses one is moved back to the contact,
    /// where it can bounce, slide, stick or have its lifetime ended. It runs once per step, after the
    /// relaxation passes of the other constraints, only when it bounces or slides; otherwise it runs inside
    /// the passes. Contacts raise collision events for the children that listen for them.
    /// </summary>
    /// <remarks>
    /// The plane set modes trace from a control point instead of from the particles: once straight down
    /// for <see cref="ParticleCollisionMode.COLLISION_MODE_INITIAL_TRACE_DOWN"/>, and in 26 directions
    /// around it for the other two, retraced when the control point moves or the retest rate elapses. The
    /// set is shared by the whole system hierarchy per mode. Particles collide with the one-sided infinite
    /// planes through those hits, and a fast enough contact is confirmed by a real trace, which also adds
    /// its surface to the set.
    /// <see cref="ParticleCollisionMode.COLLISION_MODE_PER_PARTICLE_TRACE"/> traces each particle's
    /// movement instead. A mode above the detail level drops to that level, though never below
    /// <c>m_nCollisionModeMin</c>, and the lowest detail level disables collision unless that minimum asks for some.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_WorldTraceConstraint">C_OP_WorldTraceConstraint</seealso>
    class WorldTraceConstraint : ParticleFunctionConstraint
    {
        private const float ConfirmationTraceExtension = 64f;

        /// <summary>The bit of <see cref="ParticleField.UserEventStates"/> marking a particle that has collided before.</summary>
        private const int CollidedEventState = 0x10000;
        private const int BlockSize = 4;

        /// <summary>How many confirmation traces a step may make when the definition leaves the limit unset.</summary>
        private const int DefaultMaxConfirmationTraces = 100;

        private readonly int controlPoint;
        private readonly Vector3 controlPointOffset;
        private readonly ParticleCollisionMode collisionMode = ParticleCollisionMode.COLLISION_MODE_PER_PARTICLE_TRACE;
        private readonly ParticleCollisionMode collisionModeMin = ParticleCollisionMode.COLLISION_MODE_DISABLED;
        private readonly float controlPointMovementTolerance = 5f;
        private readonly float retestRate = -1f;
        private readonly float confirmationSpeed = 24f;
        private readonly int maxConfirmationTraces;
        private readonly INumberProvider radiusScale = new LiteralNumberProvider(1f);
        private readonly INumberProvider bounceAmount = new LiteralNumberProvider(0f);
        private readonly INumberProvider slideAmount = new LiteralNumberProvider(0f);
        private readonly INumberProvider randomDirectionScale = new LiteralNumberProvider(0f);
        private readonly bool decayBounce;
        private readonly bool killOnContact;
        private readonly float minimumSpeed = -1f;
        private readonly bool bounceKilledParticles;
        private readonly bool setNormal;
        private readonly ParticleField stickField = ParticleField.NoneDisabled;
        private readonly INumberProvider stopSpeed = new LiteralNumberProvider(-1f);
        private readonly ulong storedFields;

        public WorldTraceConstraint(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.Int32("m_nCP", controlPoint);
            controlPointOffset = parse.Vector3("m_vecCpOffset", controlPointOffset);
            collisionMode = parse.Enum("m_nCollisionMode", collisionMode);
            collisionModeMin = parse.Enum("m_nCollisionModeMin", collisionModeMin);
            collisionModeMin = collisionModeMin < collisionMode ? collisionModeMin : collisionMode;
            controlPointMovementTolerance = parse.Float("m_flCpMovementTolerance", controlPointMovementTolerance);
            retestRate = parse.Float("m_flRetestRate", retestRate);
            confirmationSpeed = parse.Float("m_flCollisionConfirmationSpeed", confirmationSpeed);
            var maxTraces = parse.Int32("m_nMaxTracesPerFrame", -1);
            maxConfirmationTraces = maxTraces == -1 ? DefaultMaxConfirmationTraces : maxTraces;
            radiusScale = parse.NumberProvider("m_flRadiusScale", radiusScale);
            bounceAmount = parse.NumberProvider("m_flBounceAmount", bounceAmount);
            slideAmount = parse.NumberProvider("m_flSlideAmount", slideAmount);
            randomDirectionScale = parse.NumberProvider("m_flRandomDirScale", randomDirectionScale);
            decayBounce = parse.Boolean("m_bDecayBounce", decayBounce);
            killOnContact = parse.Boolean("m_bKillonContact", killOnContact);
            minimumSpeed = parse.Float("m_flMinSpeed", minimumSpeed);
            bounceKilledParticles = parse.Boolean("m_bKillonContactBounce", bounceKilledParticles);
            setNormal = parse.Boolean("m_bSetNormal", setNormal);
            stickField = parse.ParticleField("m_nStickOnCollisionField", stickField);
            stopSpeed = parse.NumberProvider("m_flStopSpeed", stopSpeed);

            // Sticking to hit entities through m_nEntityStickDataField and m_nEntityStickNormalField is not modelled
            storedFields = ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask() | ParticleField.UserEventStates.Mask();

            if (stickField != ParticleField.NoneDisabled)
            {
                storedFields |= stickField.Mask();
            }

            if (killOnContact || minimumSpeed >= 0f)
            {
                storedFields |= ParticleField.LifeDuration.Mask();
            }

            if (setNormal)
            {
                storedFields |= ParticleField.Normal.Mask();
            }
        }

        /// <inheritdoc/>
        public override ulong StoredFields => storedFields;

        public override bool IsFinalConstraint
        {
            get
            {
                var bounces = bounceAmount is not LiteralNumberProvider { Value: 0f };
                var slides = slideAmount is not LiteralNumberProvider { Value: 0f };

                return bounces || slides;
            }
        }

        public override bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var collision = particleSystemState.Collision;
            var mode = GetEffectiveCollisionMode(particleSystemState.DetailLevel);

            if (mode == ParticleCollisionMode.COLLISION_MODE_DISABLED || !collision.HasGeometry)
            {
                return false;
            }

            var planeSet = mode == ParticleCollisionMode.COLLISION_MODE_PER_PARTICLE_TRACE
                ? null
                : RefreshPlaneSet(collision, mode, particleSystemState);

            var hitAny = false;
            var confirmations = 0;
            var listened = particleSystemState.Data?.ListenedEvents ?? ParticleEventType.PARTICLE_EVENT_TYPE_MASK_NONE;
            var hitEvent = killOnContact ? ParticleEventType.PARTICLE_EVENT_TYPE_MASK_KILLED_ON_COLLISION : ParticleEventType.PARTICLE_EVENT_TYPE_MASK_COLLISION;

            var current = particles.Current;
            var positions = current.Vectors(ParticleField.Position);
            var previousPositions = current.Vectors(ParticleField.PositionPrevious);
            var userEventStates = current.Integers(ParticleField.UserEventStates);
            var radii = current.ReadFloats(ParticleField.Radius);
            var lifetimes = killOnContact || minimumSpeed >= 0f ? current.Floats(ParticleField.LifeDuration) : default;
            var normals = setNormal ? current.Vectors(ParticleField.Normal) : default;
            var stickColumn = stickField != ParticleField.NoneDisabled && ParticleBuffer.IsVector(stickField) ? current.Vectors(stickField) : default;

            var bounces = bounceAmount.Fill(current, particleSystemState);
            var slides = slideAmount.Fill(current, particleSystemState);
            var scatters = randomDirectionScale.Fill(current, particleSystemState);
            var radiusScales = radiusScale.Fill(current, particleSystemState);
            var stopSpeeds = stickField != ParticleField.NoneDisabled ? stopSpeed.Fill(current, particleSystemState) : default;
            var bounceOffset = (listened & ParticleEventType.PARTICLE_EVENT_TYPE_MASK_COLLISION) != 0 ? ParticleMath.FloatEpsilon : 0f;

            Span<Vector3> scatterPoints = stackalloc Vector3[BlockSize];
            var blockBounces = false;
            var blockScatters = false;
            var scatterDrawn = false;

            for (var i = 0; i < positions.Length; i++)
            {
                // The confirmation trace budget, whether particles bounce or slide and whether they
                // scatter are decided per block of four particles
                if (i % BlockSize == 0)
                {
                    confirmations = 0;
                    (blockBounces, blockScatters) = BlockResponse(bounces, slides, scatters, i, bounceOffset);
                    scatterDrawn = false;
                }

                var bounce = bounces[i] + bounceOffset;

                var slide = slides[i];
                var scatter = scatters[i];
                var scale = radiusScales[i];

                var stuckAt = stickColumn.IsEmpty ? Vector3.Zero : stickColumn[i];

                if (stuckAt.LengthSquared() > 0f)
                {
                    positions[i] = stuckAt;
                    previousPositions[i] = stuckAt;
                    continue;
                }

                var previous = previousPositions[i];
                var step = positions[i] - previous;
                var stepLengthSquared = step.LengthSquared();

                if (stepLengthSquared <= ParticleMath.FloatEpsilon)
                {
                    continue;
                }

                var stepLength = MathF.Sqrt(stepLengthSquared);
                var direction = step / stepLength;
                var halfRadius = radii[i] * scale * 0.5f;
                var traceEnd = positions[i] + (direction * halfRadius);

                if (!FindContact(collision, planeSet, previous, traceEnd, out var fraction, out var normal))
                {
                    continue;
                }

                hitAny = true;
                // A ray that starts inside a solid is not reported, so such a contact keeps the exit face's movement and normal
                var remaining = MathF.Max(0f, 1f - fraction);

                // A block draws its scatter points once, at its first hit, before the hit is confirmed
                if ((!killOnContact || bounceKilledParticles) && blockBounces && blockScatters && !scatterDrawn)
                {
                    particleSystemState.ThreadRandom.NextInUnitBall(scatterPoints);
                    scatterDrawn = true;
                }

                if (planeSet != null && !ConfirmContact(collision, planeSet, previous - (direction * halfRadius), traceEnd, direction, frameTime, ref fraction, ref confirmations))
                {
                    continue;
                }

                var tooSlow = minimumSpeed >= 0f && stepLength < minimumSpeed * frameTime;

                // The fraction is along the trace that runs half a radius past the particle, and scales the step without that extension.
                var contact = previous + (step * fraction);

                if (killOnContact)
                {
                    if (minimumSpeed < 0f || tooSlow)
                    {
                        lifetimes[i] = 0f;
                    }

                    positions[i] = contact;

                    if ((listened & ParticleEventType.PARTICLE_EVENT_TYPE_MASK_KILLED_ON_COLLISION) == 0)
                    {
                        previousPositions[i] = contact;
                    }
                }
                else if (tooSlow)
                {
                    lifetimes[i] = 0f;
                }

                var writeNormal = setNormal;
                var stopped = false;

                if (!killOnContact || bounceKilledParticles)
                {
                    // A particle that neither bounces nor slides in a block that does comes to rest at the contact
                    if (blockBounces)
                    {
                        var outgoing = Bounce(step, stepLength, direction, normal, bounce, slide, blockScatters, scatter, scatterPoints[i % BlockSize]);
                        positions[i] = contact + (outgoing * remaining);
                        previousPositions[i] = positions[i] - outgoing;
                    }
                    else
                    {
                        positions[i] = contact;
                    }

                    if (stickField != ParticleField.NoneDisabled)
                    {
                        stopped = Stick(i, positions, previousPositions, stickColumn, stopSpeeds[i] * frameTime);
                        writeNormal = stopped && setNormal;
                    }
                }

                if (writeNormal)
                {
                    normals[i] = normal;
                }

                RaiseCollisionEvents(particles, i, ref userEventStates[i], listened, hitEvent, stopped);
            }

            return hitAny;
        }

        /// <summary>
        /// How the block of four from <paramref name="blockStart"/> responds to contact: every particle
        /// of it bounces and slides by its own amounts when some particle has a positive bounce or slide
        /// amount, and every particle of it scatters, from points drawn from the thread generator, when
        /// some particle has a positive random direction scale.
        /// </summary>
        private static (bool Bounces, bool Scatters) BlockResponse(ReadOnlySpan<float> bounces, ReadOnlySpan<float> slides, ReadOnlySpan<float> scatters, int blockStart, float bounceOffset)
        {
            var blockEnd = Math.Min(blockStart + BlockSize, bounces.Length);
            var anyBouncesOrSlides = false;
            var anyScatters = false;

            for (var i = blockStart; i < blockEnd; i++)
            {
                anyBouncesOrSlides |= bounces[i] + bounceOffset > 0f || slides[i] > 0f;
                anyScatters |= scatters[i] > 0f;
            }

            return (anyBouncesOrSlides, anyScatters);
        }

        private static void RaiseCollisionEvents(ParticleCollection particles, int index, ref int userEventState, ParticleEventType listened, ParticleEventType hitEvent, bool stopped)
        {
            if ((listened & (ParticleEventType.PARTICLE_EVENT_TYPE_MASK_COLLISION | ParticleEventType.PARTICLE_EVENT_TYPE_MASK_KILLED_ON_COLLISION)) != 0)
            {
                particles.RaiseEvent(index, hitEvent);
            }

            // After a block of four particles that are all stuck, the user event state column stops advancing,
            // so the first collision bit of particles in an earlier block is tested and set; each particle uses its own state here
            if ((listened & ParticleEventType.PARTICLE_EVENT_TYPE_MASK_FIRST_COLLISION) != 0 && (userEventState & CollidedEventState) == 0)
            {
                userEventState |= CollidedEventState;
                particles.RaiseEvent(index, ParticleEventType.PARTICLE_EVENT_TYPE_MASK_FIRST_COLLISION);
            }

            if (stopped && (listened & ParticleEventType.PARTICLE_EVENT_TYPE_MASK_COLLISION_STOPPED) != 0)
            {
                particles.RaiseEvent(index, ParticleEventType.PARTICLE_EVENT_TYPE_MASK_COLLISION_STOPPED);
            }
        }

        private ParticleCollisionMode GetEffectiveCollisionMode(ParticleDetailLevel detailLevel)
        {
            var levelMode = (ParticleCollisionMode)detailLevel;

            if (detailLevel == ParticleDetailLevel.PARTICLEDETAIL_LOW)
            {
                levelMode = ParticleCollisionMode.COLLISION_MODE_DISABLED;
            }
            else if (collisionMode <= levelMode)
            {
                return collisionMode;
            }

            return collisionModeMin > levelMode ? collisionModeMin : levelMode;
        }

        private CollisionPlaneSet RefreshPlaneSet(IParticleCollision collision, ParticleCollisionMode mode, ParticleSystemState particleSystemState)
        {
            var planeSet = particleSystemState.CollisionPlaneSets[(int)mode] ??= new CollisionPlaneSet();

            var lastRefresh = planeSet.LastRefreshTime;
            var force = lastRefresh == -1f || (retestRate >= 0f && particleSystemState.Age > retestRate + lastRefresh);
            var origin = particleSystemState.GetControlPoint(controlPoint).Position + controlPointOffset;

            planeSet.Refresh(collision, mode, origin, controlPointMovementTolerance, force, particleSystemState.Age);

            return planeSet;
        }

        private static bool FindContact(IParticleCollision collision, CollisionPlaneSet? planeSet, Vector3 start, Vector3 end, out float fraction, out Vector3 normal)
        {
            if (planeSet != null)
            {
                return planeSet.Intersect(start, end, out fraction, out normal);
            }

            collision.TraceRay(start, end, out _, out normal, out fraction);

            return fraction < 1f;
        }

        /// <summary>
        /// Checks a plane set contact fast enough to need it with a real trace from behind the particle to
        /// past its step, adding the surface found to the set. A contact the trace does not find within the
        /// step is dropped; one it finds closer moves to where it was found.
        /// </summary>
        private bool ConfirmContact(IParticleCollision collision, CollisionPlaneSet planeSet, Vector3 start, Vector3 end, Vector3 direction, float frameTime,
            ref float fraction, ref int confirmations)
        {
            var segmentLengthSquared = Vector3.DistanceSquared(start, end);

            if (confirmationSpeed * confirmationSpeed > segmentLengthSquared / frameTime || confirmations >= maxConfirmationTraces)
            {
                return true;
            }

            confirmations++;

            var traceEnd = end + (direction * ConfirmationTraceExtension);

            if (!collision.TraceRay(start, traceEnd, out var hitPosition, out var hitNormal, out var hitFraction))
            {
                return false;
            }

            planeSet.Add(hitPosition, hitNormal);

            var confirmed = Vector3.Distance(start, traceEnd) * hitFraction / MathF.Sqrt(segmentLengthSquared);

            if (confirmed >= 1f)
            {
                return false;
            }

            fraction = confirmed;
            return true;
        }

        /// <summary>
        /// The particle's movement after the contact, over a whole step: the reflected direction scaled by
        /// the bounce amount, plus the part of the step along the surface scaled by the slide amount.
        /// </summary>
        private Vector3 Bounce(Vector3 step, float stepLength, Vector3 direction, Vector3 normal, float bounce, float slide, bool scatters, float scatter, Vector3 scatterPoint)
        {
            var reflected = Vector3.Reflect(direction, normal);

            if (scatters)
            {
                reflected = MathUtils.SafeNormalize(reflected + (scatterPoint * scatter));
            }

            if (decayBounce)
            {
                reflected *= stepLength;
            }

            return (reflected * bounce) + (MathUtils.ProjectOntoPlane(step, normal) * slide);
        }

        /// <summary>
        /// Sticks the particle where it rests when it moved less than <paramref name="stop"/> over the step,
        /// the stop speed times the frame time, or always when that is negative, by caching its position in the stick field, which holds it there on later
        /// steps. Returns whether it stuck; with a stick field only particles that stick take the contact normal.
        /// </summary>
        private static bool Stick(int index, Span<Vector3> positions, Span<Vector3> previousPositions, Span<Vector3> stickColumn, float stop)
        {
            if (stop >= 0f && Vector3.DistanceSquared(positions[index], previousPositions[index]) >= stop * stop)
            {
                return false;
            }

            if (!stickColumn.IsEmpty)
            {
                stickColumn[index] = positions[index];
            }

            return true;
        }
    }
}
