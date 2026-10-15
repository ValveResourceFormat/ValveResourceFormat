using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Gives each new particle the hitbox, or bone, of the model a control point carries that is
    /// closest to it, and its place in that box, optionally moving the particle there. The box is
    /// measured as a box whatever closest point test is authored.
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
        private readonly bool updatePosition;

        public SetHitboxToClosest(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
            desiredHitbox = parse.Int32("m_nDesiredHitbox", desiredHitbox);
            hitBoxScale = parse.CollectionVectorProvider("m_vecHitBoxScale", hitBoxScale);
            hitboxSetName = parse.String("m_HitboxSetName", hitboxSetName);
            useBones = parse.Boolean("m_bUseBones", useBones);
            useClosestPointOnHitbox = parse.Boolean("m_bUseClosestPointOnHitbox", useClosestPointOnHitbox);
            updatePosition = parse.Boolean("m_bUpdatePosition", updatePosition);
        }

        public override ulong WrittenFields
            => FieldMask(ParticleField.HitboxIndex) | FieldMask(ParticleField.HitboxOffsetPosition)
                | (updatePosition ? FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious) : 0);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var cache = useBones
                ? HitBoxCache.Refresh(particleSystemState, controlPoint, string.Empty, useBones: true)
                : HitBoxCache.Refresh(particleSystemState, controlPoint, hitboxSetName, useBones: false);

            if (cache == null)
            {
                return particle;
            }

            var scale = hitBoxScale.NextVector(ref particle, particleSystemState);
            var (fraction, index) = cache.Closest(particle.Position, desiredHitbox, scale, useClosestPointOnHitbox);

            particle.HitboxOffsetPosition = fraction;
            particle.HitboxIndex = index;

            if (updatePosition && (uint)index < (uint)cache.Count)
            {
                var position = cache.Current[index].PointAt(fraction);
                particle.Position = position;
                particle.PositionPrevious = position;
            }

            return particle;
        }
    }
}
