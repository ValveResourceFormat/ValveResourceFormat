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

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (modelControlPoint < 0 || HitBoxCache.Refresh(particleSystemState, modelControlPoint, hitboxSetName, useBones) is not { IsStable: true } cache)
            {
                return;
            }

            var boxes = cache.Current;
            var end = lifeTimeLerpEnd > 0f ? lifeTimeLerpEnd : 1e20f;
            var inverseWindow = lifeTimeLerpEnd > lifeTimeLerpStart ? 1f / (lifeTimeLerpEnd - lifeTimeLerpStart) : 0f;

            foreach (ref var particle in particles.Current)
            {
                if ((uint)particle.HitboxIndex >= (uint)boxes.Length)
                {
                    continue;
                }

                var target = boxes[particle.HitboxIndex].PointAt(particle.HitboxOffsetPosition);
                float fraction;

                if (lerpType == HitboxLerpType.HITBOX_LERP_CONSTANT)
                {
                    fraction = strength * interpolation.NextNumber(ref particle, particleSystemState);
                }
                else
                {
                    var age = particleSystemState.Age - particle.CreationTime;

                    if (!(end > age && age > lifeTimeLerpStart))
                    {
                        continue;
                    }

                    fraction = (age - lifeTimeLerpStart) * inverseWindow * strength;
                }

                particle.Position += (target - particle.Position) * fraction;
                particle.PositionPrevious += (target - particle.PositionPrevious) * (fraction * prevPosScale);
            }
        }
    }
}
