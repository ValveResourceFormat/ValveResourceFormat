namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Extends <see cref="CreateWithinSphere"/> by positioning particles relative to a transform
    /// input rather than the origin, and supports a per-axis distance bias to skew the spherical
    /// distribution. When local coordinates are enabled, the offset and velocity are rotated into
    /// world space by the transform orientation.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateWithinSphereTransform">C_INIT_CreateWithinSphereTransform</seealso>
    class CreateWithinSphereTransform : CreateWithinSphere
    {
        private readonly IVectorProvider distanceBias = new LiteralVectorProvider(Vector3.One);
        private readonly Vector3 distanceBiasAbs = Vector3.Zero;
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly bool localCoords;

        public CreateWithinSphereTransform(ParticleDefinitionParser parse) : base(parse)
        {
            distanceBias = parse.VectorProvider("m_vecDistanceBias", distanceBias);
            distanceBiasAbs = parse.Vector3("m_vecDistanceBiasAbs", distanceBiasAbs);
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            localCoords = parse.Boolean("m_bLocalCoords", localCoords);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var transform = transformInput.NextTransform(ref particle, particleSystemState);
            var position = transform.Translation;

            var randomVector = SampleUnitSphereDirection(particle.ParticleID);

            // Absolute value per axis folds the sphere into a hemisphere/ovoid.
            if (distanceBiasAbs.X != 0)
            {
                randomVector.X = MathF.Abs(randomVector.X);
            }
            if (distanceBiasAbs.Y != 0)
            {
                randomVector.Y = MathF.Abs(randomVector.Y);
            }
            if (distanceBiasAbs.Z != 0)
            {
                randomVector.Z = MathF.Abs(randomVector.Z);
            }

            var bias = distanceBias.NextVector(ref particle, particleSystemState);

            var biasedDirection = Vector3.Normalize(randomVector * bias);

            // A cube root over the radius fraction spreads particles evenly through the sphere's
            // volume; a linear draw would bunch them toward the centre
            var distance = ParticleCollection.RandomWithExponentBetween(
                particle.ParticleID + 2,
                1f / 3f,
                radiusMin.NextNumber(ref particle, particleSystemState),
                radiusMax.NextNumber(ref particle, particleSystemState));

            var speed = ParticleCollection.RandomWithExponentBetween(
                particle.ParticleID + 3,
                speedRandExp,
                speedMin.NextNumber(ref particle, particleSystemState),
                speedMax.NextNumber(ref particle, particleSystemState));

            var localCoordinateSystemSpeed = ParticleCollection.RandomBetweenPerComponent(
                particle.ParticleID + 4,
                localCoordinateSystemSpeedMin.NextVector(ref particle, particleSystemState),
                localCoordinateSystemSpeedMax.NextVector(ref particle, particleSystemState));

            var offset = biasedDirection * distance;

            Vector3 worldOffset;
            if (localCoords)
            {
                // Transform offset into world space
                worldOffset = Vector3.TransformNormal(offset, transform) + position;
            }
            else
            {
                worldOffset = position + offset;
            }

            particle.Position = worldOffset;

            var velocityDirection = localCoords
                ? Vector3.TransformNormal(biasedDirection, transform)
                : biasedDirection;

            // The local coordinate speed is named for the transform's frame, so it rotates whether
            // or not the spawn offset does
            var velocityLocal = Vector3.TransformNormal(localCoordinateSystemSpeed, transform);

            particle.Velocity = (velocityDirection * speed) + velocityLocal;

            return particle;
        }
    }
}
