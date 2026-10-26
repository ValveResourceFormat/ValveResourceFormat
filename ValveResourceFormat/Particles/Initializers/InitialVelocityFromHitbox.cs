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

        public override ulong WrittenFields => ParticleField.PositionPrevious.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            // The cache never holds boxes from a snapshot, whose hitbox index table would leave the particles alone
            if (HitBoxCache.Refresh(particleSystemState, controlPoint, hitboxSetName, useBones) is not { IsStable: true } cache)
            {
                return;
            }

            var hitboxIndices = run.ReadIntegers(ParticleField.HitboxIndex);
            var hitboxOffsets = run.ReadVectors(ParticleField.HitboxOffsetPosition);
            var previousPositions = run.Vectors(ParticleField.PositionPrevious);

            for (var i = 0; i < previousPositions.Length; i++)
            {
                var hitboxIndex = hitboxIndices[i];

                if ((uint)hitboxIndex >= (uint)cache.Count)
                {
                    continue;
                }

                var box = cache.Current[hitboxIndex];
                var extent = box.Maxs - box.Mins;
                var offset = (hitboxOffsets[i] * extent) - (extent * 0.5f);
                var speed = particleSystemState.Random.NextBetween(velocityMin, velocityMax);
                var direction = ParticleMath.NormalizeWithEpsilon(offset);

                previousPositions[i] += direction * speed * particleSystemState.PreviousStep;
            }
        }
    }
}
