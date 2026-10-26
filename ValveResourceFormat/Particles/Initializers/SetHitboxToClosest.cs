using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Gives each new particle the hitbox, or bone, of the model a control point carries that is
    /// closest to it, and a place in that box, optionally moving the particle there. Without the
    /// closest point option the box is the one whose origin is nearest and the place is random.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_SetHitboxToClosest">C_INIT_SetHitboxToClosest</seealso>
    class SetHitboxToClosest : ParticleFunctionInitializer
    {
        private readonly int controlPoint;
        private readonly int desiredHitbox = -1;
        private readonly IVectorProvider hitBoxScale = new LiteralVectorProvider(Vector3.One);
        private readonly string hitboxSetName = "default";
        private readonly bool useBones;
        private readonly bool useClosestPointOnHitbox;
        private readonly ClosestPointTestType testType = ClosestPointTestType.PARTICLE_CLOSEST_TYPE_BOX;
        private readonly INumberProvider hybridRatio = new LiteralNumberProvider(0.5f);
        private readonly bool updatePosition;

        public SetHitboxToClosest(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
            desiredHitbox = parse.Int32("m_nDesiredHitbox", desiredHitbox);
            hitBoxScale = parse.CollectionVectorProvider("m_vecHitBoxScale", hitBoxScale);
            hitboxSetName = parse.String("m_HitboxSetName", hitboxSetName);
            useBones = parse.Boolean("m_bUseBones", useBones);
            useClosestPointOnHitbox = parse.Boolean("m_bUseClosestPointOnHitbox", useClosestPointOnHitbox);
            testType = parse.Enum("m_nTestType", testType);
            hybridRatio = parse.CollectionNumberProvider("m_flHybridRatio", hybridRatio);
            updatePosition = parse.Boolean("m_bUpdatePosition", updatePosition);
        }

        public override ulong WrittenFields
            => ParticleField.HitboxIndex.Mask() | ParticleField.HitboxOffsetPosition.Mask()
                | (updatePosition ? ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask() : 0);

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (HitBoxCache.Refresh(particleSystemState, controlPoint, hitboxSetName, useBones) is not { } cache)
            {
                return;
            }

            if (particleSystemState.GetControlPoint(controlPoint).ChangeTime == -1f)
            {
                return;
            }

            var scale = hitBoxScale.NextVector(particleSystemState);
            var ratio = hybridRatio.NextNumber(particleSystemState);
            var readPositions = run.ReadVectors(ParticleField.Position);
            var offsets = UnwrittenVectors(run, particleSystemState, ParticleField.HitboxOffsetPosition);
            var indices = UnwrittenIntegers(run, particleSystemState, ParticleField.HitboxIndex);
            var positions = updatePosition ? run.Vectors(ParticleField.Position) : default;
            var previousPositions = updatePosition ? run.Vectors(ParticleField.PositionPrevious) : default;

            for (var i = 0; i < run.Length; i++)
            {
                var (fraction, index) = cache.Closest(particleSystemState, readPositions[i], desiredHitbox, scale, useClosestPointOnHitbox, testType, ratio);

                if (!offsets.IsEmpty)
                {
                    offsets[i] = fraction;
                }

                if (!indices.IsEmpty)
                {
                    indices[i] = index;
                }

                if (updatePosition && (uint)index < (uint)cache.Count)
                {
                    var position = cache.Current[index].PointAt(fraction);
                    positions[i] = position;
                    previousPositions[i] = position;
                }
            }
        }
    }
}
