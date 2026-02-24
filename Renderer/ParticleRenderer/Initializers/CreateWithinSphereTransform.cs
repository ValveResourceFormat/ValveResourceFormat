namespace ValveResourceFormat.Renderer.Particles.Initializers
{
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

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var transform = transformInput.NextTransform(ref particle, particleSystemState);
            var position = transform.Translation;

            var randomVector = ParticleCollection.RandomBetweenPerComponent(
                particle.ParticleID,
                new Vector3(-1),
                new Vector3(1));

            var direction = Vector3.Normalize(randomVector);

            var bias = distanceBias.NextVector(ref particle, particleSystemState);

            if (distanceBiasAbs != Vector3.Zero)
            {
                bias = new Vector3(
                    Math.Abs(bias.X) * (distanceBiasAbs.X != 0 ? Math.Sign(distanceBiasAbs.X) : 1),
                    Math.Abs(bias.Y) * (distanceBiasAbs.Y != 0 ? Math.Sign(distanceBiasAbs.Y) : 1),
                    Math.Abs(bias.Z) * (distanceBiasAbs.Z != 0 ? Math.Sign(distanceBiasAbs.Z) : 1)
                );
            }

            var biasedDirection = direction * bias;
            if (bias != Vector3.One)
            {
                biasedDirection = Vector3.Normalize(biasedDirection);
            }

            var distance = ParticleCollection.RandomBetween(
                particle.ParticleID,
                radiusMin.NextNumber(ref particle, particleSystemState),
                radiusMax.NextNumber(ref particle, particleSystemState));

            var speed = ParticleCollection.RandomBetween(
                particle.ParticleID,
                speedMin.NextNumber(ref particle, particleSystemState),
                speedMax.NextNumber(ref particle, particleSystemState));

            var localCoordinateSystemSpeed = ParticleCollection.RandomBetweenPerComponent(
                particle.ParticleID,
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
            particle.PositionPrevious = particle.Position;

            Vector3 velocityDirection;
            Vector3 velocityLocal;
            if (localCoords)
            {
                velocityDirection = Vector3.TransformNormal(biasedDirection, transform);
                velocityLocal = Vector3.TransformNormal(localCoordinateSystemSpeed, transform);
            }
            else
            {
                velocityDirection = biasedDirection;
                velocityLocal = localCoordinateSystemSpeed;
            }

            particle.Velocity = (velocityDirection * speed) + velocityLocal;

            return particle;
        }
    }
}
