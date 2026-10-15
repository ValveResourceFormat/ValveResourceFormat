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
            => FieldMask(ParticleField.Position) | FieldMask(ParticleField.LifeDuration) | FieldMask(ParticleField.PositionPrevious)
                | FieldMask(ParticleField.HitboxIndex) | FieldMask(ParticleField.HitboxOffsetPosition);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var cache = useBones
                ? HitBoxCache.Refresh(particleSystemState, controlPoint, string.Empty, useBones: true)
                : HitBoxCache.Refresh(particleSystemState, controlPoint, hitboxSetName, useBones: false);

            var point = particleSystemState.GetControlPoint(controlPoint);
            var height = (heightControlPoint >= 0 ? particleSystemState.GetControlPoint(heightControlPoint).Position.Z : point.Position.Z)
                + desiredHeight.NextNumber(ref particle, particleSystemState);
            var scale = hitBoxScale.NextVector(ref particle, particleSystemState);
            var bias = directionBias.NextVector(ref particle, particleSystemState);

            if (localCoords)
            {
                bias = Vector3.TransformNormal(bias, point.GetBasisAtTime(particleSystemState.Age));
            }

            var velocityScale = hitboxVelocityScale.NextNumber(ref particle, particleSystemState);
            var maxVelocity = maxBoneVelocity.NextNumber(ref particle, particleSystemState);

            if (cache == null || !TryPick(cache, particleSystemState, point.Position, height, scale, bias, out var position, out var fraction, out var index))
            {
                particle.Lifetime = 0f;
                return particle;
            }

            particle.Position = forceZ ? position with { Z = height } : position;
            particle.HitboxOffsetPosition = fraction;
            particle.HitboxIndex = index;
            particle.PositionPrevious = particle.Position;

            if (velocityScale != 0f && cache.BoxVelocity(index) is { } velocity)
            {
                velocity *= velocityScale;

                if (maxVelocity > 0f && velocity.LengthSquared() > maxVelocity * maxVelocity)
                {
                    velocity = MathUtils.SafeNormalize(velocity) * maxVelocity;
                }

                particle.PositionPrevious = particle.Position - (velocity * particles.CurrentFrameTime);
            }

            return particle;
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

            var highFraction = (scale * 0.5f) + new Vector3(0.5f);
            var lowFraction = Vector3.One - highFraction;
            var tries = MathF.Abs(bias.X) >= 1e-4f || MathF.Abs(bias.Y) >= 1e-4f || MathF.Abs(bias.Z) >= 1e-4f ? 5 : 1;
            var random = particleSystemState.Random;
            var previous = cache.Previous;
            var best = -1e20f;

            for (var attempt = 0; attempt < tries; attempt++)
            {
                var boxIndex = candidates[(int)(candidates.Count * random.Next())];
                var box = boxes[boxIndex];
                var drawn = new Vector3(random.NextSeededTwice(), random.NextSeededTwice(), random.NextSeededTwice());
                var candidateFraction = ((highFraction - lowFraction) * drawn) + lowFraction;
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
