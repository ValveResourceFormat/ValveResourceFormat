using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Places new particles at random points inside the hitboxes, or bones, of the model a control
    /// point carries that reach across a world height, preferring points along a direction, and
    /// can give them the velocity of their box. With no box reaching the height the particles get a
    /// life of 0. Measuring the height from the water surface is not supported.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateOnModelAtHeight">C_INIT_CreateOnModelAtHeight</seealso>
    class CreateOnModelAtHeight : ParticleFunctionInitializer
    {
        /// <summary>Where the direction bias of a box is measured from.</summary>
        /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleHitboxBiasType_t">ParticleHitboxBiasType_t</seealso>
        private enum HitboxBiasType
        {
            PARTICLE_HITBOX_BIAS_ENTITY = 0,
            PARTICLE_HITBOX_BIAS_HITBOX = 1,
        }

        private readonly bool useBones;
        private readonly bool forceZ;
        private readonly int controlPoint;
        private readonly int heightControlPoint = -1;
        private readonly INumberProvider desiredHeight = new LiteralNumberProvider(0f);
        private readonly IVectorProvider hitBoxScale = new LiteralVectorProvider(Vector3.One);
        private readonly IVectorProvider directionBias = new LiteralVectorProvider(Vector3.Zero);
        private readonly HitboxBiasType biasType = HitboxBiasType.PARTICLE_HITBOX_BIAS_ENTITY;
        private readonly bool localCoords;
        private readonly bool preferMovingBoxes;
        private readonly string hitboxSetName = "default";
        private readonly INumberProvider hitboxVelocityScale = new LiteralNumberProvider(0f);
        private readonly INumberProvider maxBoneVelocity = new LiteralNumberProvider(0f);

        private readonly List<int> candidates = [];

        public CreateOnModelAtHeight(ParticleDefinitionParser parse) : base(parse)
        {
            useBones = parse.Boolean("m_bUseBones", useBones);
            forceZ = parse.Boolean("m_bForceZ", forceZ);
            controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
            heightControlPoint = parse.Int32("m_nHeightCP", heightControlPoint);
            desiredHeight = parse.CollectionNumberProvider("m_flDesiredHeight", desiredHeight);
            hitBoxScale = parse.CollectionVectorProvider("m_vecHitBoxScale", hitBoxScale);
            directionBias = parse.CollectionVectorProvider("m_vecDirectionBias", directionBias);
            biasType = parse.Enum("m_nBiasType", biasType);
            localCoords = parse.Boolean("m_bLocalCoords", localCoords);
            preferMovingBoxes = parse.Boolean("m_bPreferMovingBoxes", preferMovingBoxes);
            hitboxSetName = parse.String("m_HitboxSetName", hitboxSetName);
            hitboxVelocityScale = parse.CollectionNumberProvider("m_flHitboxVelocityScale", hitboxVelocityScale);
            maxBoneVelocity = parse.CollectionNumberProvider("m_flMaxBoneVelocity", maxBoneVelocity);
        }

        public override ulong WrittenFields
            => ParticleField.Position.Mask() | ParticleField.LifeDuration.Mask() | ParticleField.PositionPrevious.Mask()
                | ParticleField.HitboxIndex.Mask() | ParticleField.HitboxOffsetPosition.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var cache = HitBoxCache.Refresh(particleSystemState, controlPoint, hitboxSetName, useBones);
            var point = particleSystemState.GetControlPoint(controlPoint);
            var height = (heightControlPoint >= 0 ? particleSystemState.GetControlPoint(heightControlPoint).Position.Z : point.Position.Z)
                + desiredHeight.NextNumber(particleSystemState);
            var scale = hitBoxScale.NextVector(particleSystemState);
            var bias = directionBias.NextVector(particleSystemState);

            if (localCoords)
            {
                bias = Vector3.TransformNormal(bias, point.GetBasisAtTime(particleSystemState.Age));
            }

            var velocityScale = hitboxVelocityScale.NextNumber(particleSystemState);
            var maxVelocity = maxBoneVelocity.NextNumber(particleSystemState);

            var positions = run.Vectors(ParticleField.Position);
            var lifetimes = run.Floats(ParticleField.LifeDuration);
            var previous = SpawnPreviousPositions(run, particleSystemState);
            var hitboxOffsets = UnwrittenVectors(run, particleSystemState, ParticleField.HitboxOffsetPosition);
            var hitboxIndices = UnwrittenIntegers(run, particleSystemState, ParticleField.HitboxIndex);

            for (var i = 0; i < run.Length; i++)
            {
                if (cache == null || !TryPick(cache, particleSystemState, point.Position, height, scale, bias, out var position, out var fraction, out var index))
                {
                    lifetimes[i] = 0f;
                    continue;
                }

                var placed = forceZ ? position with { Z = height } : position;
                positions[i] = placed;

                if (!hitboxOffsets.IsEmpty)
                {
                    hitboxOffsets[i] = fraction;
                }

                if (!hitboxIndices.IsEmpty)
                {
                    hitboxIndices[i] = index;
                }

                if (previous.IsEmpty)
                {
                    continue;
                }

                previous[i] = placed;

                if (velocityScale != 0f && cache.BoxVelocity(index) is { } velocity)
                {
                    velocity *= velocityScale;

                    if (maxVelocity > 0f)
                    {
                        velocity = MathUtils.ClampLength(velocity, maxVelocity);
                    }

                    previous[i] = placed - (velocity * particleSystemState.CurrentStep);
                }
            }
        }

        private bool TryPick(HitBoxCache cache, ParticleSystemState particleSystemState, Vector3 origin, float height, Vector3 scale, Vector3 bias,
            out Vector3 position, out Vector3 fraction, out int index)
        {
            position = origin;
            fraction = Vector3.Zero;
            index = 0;

            var boxes = cache.Current;
            candidates.Clear();

            for (var i = 0; i < boxes.Length; i++)
            {
                var low = boxes[i].TransformPoint(boxes[i].Mins).Z;
                var high = boxes[i].TransformPoint(boxes[i].Maxs).Z;

                if ((low <= height && height <= high) || (high <= height && height <= low))
                {
                    candidates.Add(i);
                }
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            var (lowFraction, highFraction) = HitBox.ScaledFractions(scale);
            var tries = HitBoxCache.HasDirectionBias(bias) ? 5 : 1;
            var random = particleSystemState.Random;
            var previous = cache.Previous;
            var best = -1e20f;

            for (var attempt = 0; attempt <= tries; attempt++)
            {
                var boxIndex = candidates[(int)(candidates.Count * random.Next())];
                var box = boxes[boxIndex];
                var candidateFraction = random.NextBetweenPerComponentSeededTwice(lowFraction, highFraction);
                var candidate = box.PointAt(candidateFraction);
                var reference = biasType == HitboxBiasType.PARTICLE_HITBOX_BIAS_HITBOX ? box.TransformPoint((box.Maxs - box.Mins) * 0.5f) : origin;
                var score = Vector3.Dot(candidate - reference, bias) + (random.Next() * 72f);

                if (preferMovingBoxes && cache.IsStable)
                {
                    score *= Vector3.Distance(box.Position, previous[boxIndex].Position);
                }

                if (score > best)
                {
                    best = score;
                    position = candidate;
                    fraction = candidateFraction;
                    index = boxIndex;
                }
            }

            return true;
        }
    }
}
