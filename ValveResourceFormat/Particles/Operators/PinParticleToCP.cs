namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Holds one particle on a control point plus an offset until a break condition releases it for
    /// good. The pinned particle has its force scale set to 0, so forces and rope constraints treat it
    /// as fixed, and it carries no velocity; a released one gets a force scale of 1 back.
    /// </summary>
    /// <remarks>
    /// The distance breaks compare the current distance with the one at the first test, so
    /// <c>m_flBreakDistance</c> is a stretch ratio rather than a length.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_PinParticleToCP">C_OP_PinParticleToCP</seealso>
    class PinParticleToCP : ParticleFunctionOperator
    {
        private readonly int controlPoint;
        private readonly IVectorProvider offset = new LiteralVectorProvider(Vector3.Zero);
        private readonly bool offsetLocal = true;
        private readonly ParticleSelection particleSelection = ParticleSelection.PARTICLE_SELECTION_FIRST;
        private readonly INumberProvider particleNumber = new LiteralNumberProvider(0f);
        private readonly ParticlePinDistance pinBreakType = ParticlePinDistance.PARTICLE_PIN_DISTANCE_NONE;
        private readonly INumberProvider breakDistance = new LiteralNumberProvider(1.75f);
        private readonly INumberProvider breakSpeed = new LiteralNumberProvider(0f);
        private readonly INumberProvider age = new LiteralNumberProvider(0f);
        private readonly int breakControlPoint = -1;
        private readonly int breakControlPoint2 = -1;
        private readonly INumberProvider breakValue = new LiteralNumberProvider(0f);
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1f);
        private readonly bool retainInitialVelocity;

        private bool broken;
        private float initialDistance;
        private int pinnedParticle;
        private int referenceParticle;
        private int pinnedParticleId;
        private Vector3 inheritedVelocity;

        public PinParticleToCP(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.ControlPoint("m_nControlPointNumber", controlPoint);
            offset = parse.CollectionVectorProvider("m_vecOffset", offset);
            offsetLocal = parse.Boolean("m_bOffsetLocal", offsetLocal);
            particleSelection = parse.Enum("m_nParticleSelection", particleSelection);
            particleNumber = parse.CollectionNumberProvider("m_nParticleNumber", particleNumber);
            pinBreakType = parse.Enum("m_nPinBreakType", pinBreakType);
            breakDistance = parse.CollectionNumberProvider("m_flBreakDistance", breakDistance);
            breakSpeed = parse.CollectionNumberProvider("m_flBreakSpeed", breakSpeed);
            age = parse.CollectionNumberProvider("m_flAge", age);
            breakControlPoint = parse.Int32("m_nBreakControlPointNumber", breakControlPoint);
            breakControlPoint2 = parse.Int32("m_nBreakControlPointNumber2", breakControlPoint2);
            breakValue = parse.CollectionNumberProvider("m_flBreakValue", breakValue);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);
            retainInitialVelocity = parse.Boolean("m_bRetainInitialVelocity", retainInitialVelocity);
        }

        public override void Reset()
        {
            broken = false;
            initialDistance = -1f;
            pinnedParticle = -1;
            referenceParticle = -1;
            pinnedParticleId = -1;
            inheritedVelocity = new Vector3(float.MaxValue);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (broken)
            {
                return;
            }

            var cp = particleSystemState.GetControlPoint(controlPoint);
            UpdatePinnedParticle(particles, frameTime, particleSystemState, cp);

            if (pinnedParticle < 0)
            {
                return;
            }

            ref var particle = ref particles.Current[pinnedParticle];

            if (IsBroken(particles, frameTime, particleSystemState, cp))
            {
                particle.ForceScale = 1f;
                broken = true;
                return;
            }

            particle.ForceScale = 0f;

            var pinOffset = offset.NextVector(particleSystemState);
            var target = cp.Position + (offsetLocal ? Vector3.TransformNormal(pinOffset, cp.GetRotationMatrix()) : pinOffset);

            particle.Position = MathUtils.Lerp(particle.Position, target, interpolation.NextNumber(ref particle, particleSystemState));
            particle.PositionPrevious = particle.Position;
        }

        private void UpdatePinnedParticle(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, ControlPoint cp)
        {
            var live = particles.Current;
            var count = live.Length;
            var number = (int)particleNumber.NextNumber(particleSystemState);
            var previous = pinnedParticle;

            switch (particleSelection)
            {
                case ParticleSelection.PARTICLE_SELECTION_FIRST:
                    pinnedParticle = Math.Clamp(number, 0, count - 1);
                    break;

                case ParticleSelection.PARTICLE_SELECTION_NUMBER:
                    pinnedParticle = Math.Clamp(number, -1, count - 1);

                    if (previous != -1 && previous != pinnedParticle && previous < count)
                    {
                        live[previous].ForceScale = 1f;
                    }

                    break;

                case ParticleSelection.PARTICLE_SELECTION_LAST:
                    pinnedParticle = Math.Clamp(count - number - 1, 0, count - 1);
                    SwitchLastPinned(particles, frameTime, cp);
                    break;
            }

            referenceParticle = pinBreakType switch
            {
                ParticlePinDistance.PARTICLE_PIN_DISTANCE_NEIGHBOR => particleSelection == ParticleSelection.PARTICLE_SELECTION_FIRST ? pinnedParticle + 1 : pinnedParticle - 1,
                ParticlePinDistance.PARTICLE_PIN_DISTANCE_FARTHEST => count - pinnedParticle <= count / 2 ? 0 : count - 1,
                ParticlePinDistance.PARTICLE_PIN_DISTANCE_FIRST => 0,
                ParticlePinDistance.PARTICLE_PIN_DISTANCE_LAST => count - 1,
                _ => referenceParticle,
            };

            referenceParticle = Math.Clamp(referenceParticle, 0, count - 1);
        }

        /// <summary>
        /// Releases the particle pinned last step once another particle has become the last, handing it
        /// the velocity it was created with, turned with the control point, when that is retained.
        /// </summary>
        private void SwitchLastPinned(ParticleCollection particles, float frameTime, ControlPoint cp)
        {
            var live = particles.Current;
            var id = live[pinnedParticle].ParticleId;

            if (id == pinnedParticleId)
            {
                return;
            }

            var released = -1;

            for (var i = live.Length - 1; i >= 0; i--)
            {
                if (live[i].ParticleId == pinnedParticleId)
                {
                    released = i;
                    break;
                }
            }

            pinnedParticleId = id;

            if (released >= 0)
            {
                live[released].ForceScale = 1f;

                if (retainInitialVelocity)
                {
                    var velocity = Vector3.TransformNormal(inheritedVelocity, cp.GetRotationMatrix());
                    live[released].PositionPrevious -= velocity * frameTime;
                    inheritedVelocity = GetLocalInitialVelocity(particles, cp);
                }
            }
            else if (retainInitialVelocity && inheritedVelocity == new Vector3(float.MaxValue))
            {
                inheritedVelocity = GetLocalInitialVelocity(particles, cp);
            }
        }

        private Vector3 GetLocalInitialVelocity(ParticleCollection particles, ControlPoint cp)
        {
            var velocity = particles.Initial[pinnedParticle].GetVelocity(particles.PreviousFrameTime);

            return new Vector3(Vector3.Dot(velocity, cp.Forward), Vector3.Dot(velocity, cp.Left), Vector3.Dot(velocity, cp.Up));
        }

        private bool IsBroken(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, ControlPoint cp)
        {
            switch (pinBreakType)
            {
                case ParticlePinDistance.PARTICLE_PIN_DISTANCE_NONE:
                    return false;

                case ParticlePinDistance.PARTICLE_PIN_SPEED:
                    return cp.StepDelta.Length() / frameTime > breakSpeed.NextNumber(particleSystemState);

                case ParticlePinDistance.PARTICLE_PIN_COLLECTION_AGE:
                    return particleSystemState.Age > age.NextNumber(particleSystemState);

                case ParticlePinDistance.PARTICLE_PIN_FLOAT_VALUE:
                    return breakValue.NextNumber(particleSystemState) >= 1f;
            }

            var maxStretch = breakDistance.NextNumber(particleSystemState);

            if (maxStretch <= -1f)
            {
                return false;
            }

            if (initialDistance == -1f)
            {
                initialDistance = GetPinDistance(particles.Initial, particleSystemState, cp);
            }

            return GetPinDistance(particles.Current, particleSystemState, cp) / initialDistance > maxStretch;
        }

        private float GetPinDistance(Span<Particle> positions, ParticleSystemState particleSystemState, ControlPoint cp)
        {
            var pinned = positions[pinnedParticle].Position;

            return pinBreakType switch
            {
                ParticlePinDistance.PARTICLE_PIN_DISTANCE_CENTER => Vector3.Distance(pinned, particleSystemState.Data?.WorldBounds.Center ?? Vector3.Zero),
                ParticlePinDistance.PARTICLE_PIN_DISTANCE_CP => DistanceToBreakPoint(particleSystemState, cp, breakControlPoint),
                ParticlePinDistance.PARTICLE_PIN_DISTANCE_CP_PAIR_EITHER or ParticlePinDistance.PARTICLE_PIN_DISTANCE_CP_PAIR_BOTH
                    when breakControlPoint < 0 || breakControlPoint2 < 0 => 0f,
                ParticlePinDistance.PARTICLE_PIN_DISTANCE_CP_PAIR_EITHER => MathF.Max(
                    DistanceToBreakPoint(particleSystemState, cp, breakControlPoint),
                    DistanceToBreakPoint(particleSystemState, cp, breakControlPoint2)),
                ParticlePinDistance.PARTICLE_PIN_DISTANCE_CP_PAIR_BOTH => MathF.Min(
                    DistanceToBreakPoint(particleSystemState, cp, breakControlPoint),
                    DistanceToBreakPoint(particleSystemState, cp, breakControlPoint2)),
                _ => Vector3.Distance(pinned, positions[referenceParticle].Position),
            };
        }

        private static float DistanceToBreakPoint(ParticleSystemState particleSystemState, ControlPoint cp, int breakPoint)
            => breakPoint < 0 ? 0f : Vector3.Distance(cp.Position, particleSystemState.GetControlPoint(breakPoint).Position);
    }
}
