using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Gives new particles a random speed along the line between their point in their hitbox and
    /// the hitbox's centre, measured in the hitbox's own axes. A positive speed moves them toward
    /// the centre.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitialVelocityFromHitbox">C_INIT_InitialVelocityFromHitbox</seealso>
    class InitialVelocityFromHitbox : ParticleFunctionInitializer
    {
        private readonly float velocityMin;
        private readonly float velocityMax;
        private readonly int controlPoint;
        private readonly string hitboxSetName = "default";
        private readonly bool useBones;

        public InitialVelocityFromHitbox(ParticleDefinitionParser parse) : base(parse)
        {
            velocityMin = parse.Float("m_flVelocityMin", velocityMin);
            velocityMax = parse.Float("m_flVelocityMax", velocityMax);
            controlPoint = parse.ControlPoint("m_nControlPointNumber", controlPoint);
            hitboxSetName = parse.String("m_HitboxSetName", hitboxSetName);
            useBones = parse.Boolean("m_bUseBones", useBones);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.PositionPrevious);

        public override Particle Initialize(Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (HitBoxCache.Refresh(particleSystemState, controlPoint, hitboxSetName, useBones) is not { IsStable: true } cache
                || (uint)particle.HitboxIndex >= (uint)cache.Count)
            {
                return particle;
            }

            var box = cache.Current[particle.HitboxIndex];
            var extent = box.Maxs - box.Mins;
            var offset = (particle.HitboxOffsetPosition * extent) - (extent * 0.5f);
            var speed = particleSystemState.Random.NextBetween(velocityMin, velocityMax);
            var direction = ParticleMath.NormalizeWithEpsilon(offset);

            particle.PositionPrevious += direction * speed * particles.PreviousFrameTime;
            return particle;
        }
    }
}
