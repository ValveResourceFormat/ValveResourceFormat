using ValveResourceFormat.Particles.Constraints;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Places new particles on world hits picked at random from the traces
    /// <see cref="LifespanFromVelocity"/> has cached for the system tree, optionally moved by a random
    /// offset whose z runs along the hit normal and x along the up axis of that frame. A particle that
    /// picks a cached miss is given no life. Only a child system has a cache to read; anywhere without
    /// one, particles are placed at the world origin.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateFromPlaneCache">C_INIT_CreateFromPlaneCache</seealso>
    class CreateFromPlaneCache : ParticleFunctionInitializer
    {
        private readonly Vector3 offsetMin = Vector3.Zero;
        private readonly Vector3 offsetMax = Vector3.Zero;
        private readonly bool useOffset;
        private readonly bool useNormal;

        public CreateFromPlaneCache(ParticleDefinitionParser parse) : base(parse)
        {
            offsetMin = parse.Vector3("m_vecOffsetMin", offsetMin);
            offsetMax = parse.Vector3("m_vecOffsetMax", offsetMax);
            useNormal = parse.Boolean("m_bUseNormal", useNormal);
            useOffset = offsetMin != Vector3.Zero && offsetMax != Vector3.Zero;
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Position) | FieldMask(ParticleField.LifeDuration)
            | FieldMask(ParticleField.PositionPrevious) | FieldMask(ParticleField.Normal);

        public override Particle Initialize(Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var cache = particleSystemState.ParentSystem == null ? null : particleSystemState.CollisionPlaneSets[0];

            if (cache == null || cache.Count <= 0)
            {
                particle.Position = Vector3.Zero;
                particle.PositionPrevious = Vector3.Zero;
                return particle;
            }

            var random = particleSystemState.Random;
            var slot = (int)(cache.Count * random.Next());
            random.Next();

            var (point, normal) = cache.GetPlane(Math.Min(slot, CollisionPlaneSet.MaxPlanes - 1));

            if (normal == new Vector3(float.MaxValue) || point == new Vector3(float.MaxValue))
            {
                particle.Lifetime = 0f;
                return particle;
            }

            if (useOffset)
            {
                var offset = random.NextBetweenPerComponentSeededTwice(offsetMin, offsetMax);

                var frame = EntityTransformHelper.ForwardDirectionToRotationMatrix(normal);
                point += Vector3.TransformNormal(new Vector3(offset.Z, offset.Y, offset.X), frame);
            }

            particle.Position = point;
            particle.PositionPrevious = point;

            if (useNormal)
            {
                particle.Normal = normal;
            }

            return particle;
        }
    }
}
