using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Pulls particles toward the point in a hitbox, or bone, of the model its model input reads
    /// that was recorded at creation: over a window of each particle's life with a pull growing
    /// through it, or by a constant fraction every step.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_MoveToHitbox">C_OP_MoveToHitbox</seealso>
    class MoveToHitbox : ParticleFunctionOperator
    {
        private enum HitboxLerpType
        {
            HITBOX_LERP_LIFETIME = 0,
            HITBOX_LERP_CONSTANT = 1,
        }

        private readonly int modelControlPoint;
        private readonly float lifeTimeLerpStart;
        private readonly float lifeTimeLerpEnd = 1f;
        private readonly float prevPosScale = 1f;
        private readonly string hitboxSetName = "default";
        private readonly bool useBones;
        private readonly HitboxLerpType lerpType = HitboxLerpType.HITBOX_LERP_LIFETIME;
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1f);

        public MoveToHitbox(ParticleDefinitionParser parse) : base(parse)
        {
            modelControlPoint = ModelInput.ControlPoint(parse);
            lifeTimeLerpStart = parse.Float("m_flLifeTimeLerpStart", lifeTimeLerpStart);
            lifeTimeLerpEnd = parse.Float("m_flLifeTimeLerpEnd", lifeTimeLerpEnd);
            prevPosScale = parse.Float("m_flPrevPosScale", prevPosScale);
            hitboxSetName = parse.String("m_HitboxSetName", hitboxSetName);
            useBones = parse.Boolean("m_bUseBones", useBones);
            lerpType = parse.Enum("m_nLerpType", lerpType);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (modelControlPoint < 0 || HitBoxCache.Refresh(particleSystemState, modelControlPoint, hitboxSetName, useBones) is not { IsStable: true } cache)
            {
                return;
            }

            var boxes = cache.Current;
            var end = lifeTimeLerpEnd > 0f ? lifeTimeLerpEnd : ParticleMath.NoFadeEnd;
            var inverseWindow = lifeTimeLerpEnd > lifeTimeLerpStart ? 1f / (lifeTimeLerpEnd - lifeTimeLerpStart) : 0f;

            var current = particles.Current;
            var positions = current.Vectors(ParticleField.Position);
            var previousPositions = current.Vectors(ParticleField.PositionPrevious);
            var hitboxIndices = current.ReadIntegers(ParticleField.HitboxIndex);
            var hitboxOffsets = current.ReadVectors(ParticleField.HitboxOffsetPosition);
            var ages = current.ReadAges();

            var interpolations = default(Span<float>);

            if (lerpType == HitboxLerpType.HITBOX_LERP_CONSTANT)
            {
                interpolations = interpolation.Fill(current, particleSystemState);
            }

            for (var i = 0; i < positions.Length; i++)
            {
                var hitboxIndex = hitboxIndices[i];

                if ((uint)hitboxIndex >= (uint)boxes.Length)
                {
                    continue;
                }

                var target = boxes[hitboxIndex].PointAt(hitboxOffsets[i]);
                float fraction;

                if (lerpType == HitboxLerpType.HITBOX_LERP_CONSTANT)
                {
                    fraction = strength * interpolations[i];
                }
                else
                {
                    var age = ages[i];

                    if (!(end > age && age > lifeTimeLerpStart))
                    {
                        continue;
                    }

                    fraction = (age - lifeTimeLerpStart) * inverseWindow * strength;
                }

                positions[i] = MathUtils.Lerp(positions[i], target, fraction);
                previousPositions[i] = MathUtils.Lerp(previousPositions[i], target, fraction * prevPosScale);
            }
        }
    }
}
