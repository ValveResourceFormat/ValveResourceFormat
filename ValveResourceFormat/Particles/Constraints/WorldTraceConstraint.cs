using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// Collides particles with the world. Each particle's movement over the step is tested against the
    /// surfaces its collision mode finds, and a particle that crosses one is moved back to the contact,
    /// where it can bounce, slide, stick or be killed.
    /// </summary>
    /// <remarks>
    /// The plane set modes trace from a control point instead of from the particles: once straight down
    /// for <see cref="ParticleCollisionMode.COLLISION_MODE_INITIAL_TRACE_DOWN"/>, and in 26 directions
    /// around it for the other two, retraced when the control point moves or the retest rate elapses. The
    /// set is shared by the whole system hierarchy per mode. Particles collide with the one-sided infinite
    /// planes through those hits, and a fast enough contact is confirmed by a real trace, which also adds
    /// its surface to the set.
    /// <see cref="ParticleCollisionMode.COLLISION_MODE_PER_PARTICLE_TRACE"/> traces each particle's
    /// movement instead. <c>m_nCollisionModeMin</c> has no effect.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_WorldTraceConstraint">C_OP_WorldTraceConstraint</seealso>
    class WorldTraceConstraint : ParticleFunctionConstraint
    {
        private const float ConfirmationTraceExtension = 64f;
        private const int ConfirmationBlockSize = 4;

        private readonly int controlPoint;
        private readonly Vector3 controlPointOffset;
        private readonly ParticleCollisionMode collisionMode = ParticleCollisionMode.COLLISION_MODE_PER_PARTICLE_TRACE;
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

        public WorldTraceConstraint(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.Int32("m_nCP", controlPoint);
            controlPointOffset = parse.Vector3("m_vecCpOffset", controlPointOffset);
            collisionMode = parse.Enum("m_nCollisionMode", collisionMode);
            controlPointMovementTolerance = parse.Float("m_flCpMovementTolerance", controlPointMovementTolerance);
            retestRate = parse.Float("m_flRetestRate", retestRate);
            confirmationSpeed = parse.Float("m_flCollisionConfirmationSpeed", confirmationSpeed);
            maxConfirmationTraces = parse.Int32("m_nMaxTracesPerFrame", -1) switch { -1 => 100, var traces => traces };
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
        }

        public override bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState)
        {
            var collision = particleSystemState.Collision;

            if (collisionMode == ParticleCollisionMode.COLLISION_MODE_DISABLED || !collision.HasGeometry)
            {
                return false;
            }

            var planeSet = collisionMode == ParticleCollisionMode.COLLISION_MODE_PER_PARTICLE_TRACE
                ? null
                : RefreshPlaneSet(collision, particleSystemState);

            var hitAny = false;
            var confirmations = 0;
            var index = 0;

            foreach (ref var particle in particles.Current)
            {
                // The confirmation trace budget is counted per block of four particles
                if (index++ % ConfirmationBlockSize == 0)
                {
                    confirmations = 0;
                }

                var bounce = bounceAmount.NextNumber(ref particle, particleSystemState);
                var slide = slideAmount.NextNumber(ref particle, particleSystemState);
                var scatter = randomDirectionScale.NextNumber(ref particle, particleSystemState);
                var scale = radiusScale.NextNumber(ref particle, particleSystemState);

                var stuckAt = stickField != ParticleField.NoneDisabled ? particle.GetVector(stickField) : Vector3.Zero;

                if (stuckAt.LengthSquared() > 0f)
                {
                    particle.Position = stuckAt;
                    particle.PositionPrevious = stuckAt;
                    continue;
                }

                var previous = particle.PositionPrevious;
                var step = particle.Position - previous;
                var stepLengthSquared = step.LengthSquared();

                if (stepLengthSquared <= ParticleMath.FloatEpsilon)
                {
                    continue;
                }

                var stepLength = MathF.Sqrt(stepLengthSquared);
                var direction = step / stepLength;
                var halfRadius = particle.Radius * scale * 0.5f;
                var traceEnd = particle.Position + (direction * halfRadius);

                if (!FindContact(collision, planeSet, previous, traceEnd, out var fraction, out var normal))
                {
                    continue;
                }

                hitAny = true;
                var remaining = MathF.Max(0f, 1f - fraction);

                if (planeSet != null && !ConfirmContact(collision, planeSet, previous - (direction * halfRadius), traceEnd, direction, frameTime, ref fraction, ref confirmations))
                {
                    continue;
                }

                var tooSlow = minimumSpeed >= 0f && stepLength < minimumSpeed * frameTime;

                // Source 2 bug: the contact is measured from behind the particle but applied from its previous position, so it can lie below the plane and let the particle fall through.
                var contact = previous + (step * fraction);

                if (killOnContact)
                {
                    if (minimumSpeed < 0f || tooSlow)
                    {
                        particle.Kill();
                    }

                    particle.Position = contact;
                    particle.PositionPrevious = contact;
                }
                else if (tooSlow)
                {
                    particle.Kill();
                }

                var writeNormal = setNormal;

                if (!killOnContact || bounceKilledParticles)
                {
                    if (bounce > 0f || slide > 0f)
                    {
                        var outgoing = Bounce(step, stepLength, direction, normal, bounce, slide, scatter, particleSystemState);
                        particle.Position = contact + (outgoing * remaining);
                        particle.PositionPrevious = particle.Position - outgoing;
                    }
                    else
                    {
                        particle.Position = contact;
                    }

                    if (stickField != ParticleField.NoneDisabled)
                    {
                        writeNormal = Stick(ref particle, frameTime, particleSystemState) && setNormal;
                    }
                }

                if (writeNormal)
                {
                    particle.SetVector(ParticleField.Normal, normal);
                }
            }

            return hitAny;
        }

        private CollisionPlaneSet RefreshPlaneSet(IParticleCollision collision, ParticleSystemState particleSystemState)
        {
            var planeSet = particleSystemState.CollisionPlaneSets[(int)collisionMode] ??= new CollisionPlaneSet();

            var lastRefresh = planeSet.LastRefreshTime;
            var force = lastRefresh == -1f || (retestRate >= 0f && particleSystemState.Age > retestRate + lastRefresh);
            var origin = particleSystemState.GetControlPoint(controlPoint).Position + controlPointOffset;

            planeSet.Refresh(collision, collisionMode, origin, controlPointMovementTolerance, force, particleSystemState.Age);

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
        private Vector3 Bounce(Vector3 step, float stepLength, Vector3 direction, Vector3 normal, float bounce, float slide, float scatter, ParticleSystemState particleSystemState)
        {
            var reflected = Vector3.Reflect(direction, normal);

            if (scatter > 0f)
            {
                reflected = MathUtils.SafeNormalize(reflected + (particleSystemState.Random.NextInUnitBallByRejection() * scatter));
            }

            if (decayBounce)
            {
                reflected *= stepLength;
            }

            return (reflected * bounce) + (MathUtils.ProjectOntoPlane(step, normal) * slide);
        }

        /// <summary>
        /// Sticks the particle where it rests when it moves slower than the stop speed, or always when the
        /// stop speed is negative, by caching its position in the stick field, which holds it there on later
        /// steps. Returns whether it stuck; with a stick field only particles that stick take the contact normal.
        /// </summary>
        private bool Stick(ref Particle particle, float frameTime, ParticleSystemState particleSystemState)
        {
            var stop = stopSpeed.NextNumber(ref particle, particleSystemState) * frameTime;

            if (stop >= 0f && Vector3.DistanceSquared(particle.Position, particle.PositionPrevious) >= stop * stop)
            {
                return false;
            }

            particle.SetVector(stickField, particle.Position);
            return true;
        }
    }
}
