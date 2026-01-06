using System.Numerics;
using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class CreateWithinSphereTransform : ParticleFunctionInitializer
    {
        private readonly INumberProvider radiusMin = new LiteralNumberProvider(0);
        private readonly INumberProvider radiusMax = new LiteralNumberProvider(0);
        private readonly IVectorProvider distanceBias = new LiteralVectorProvider(Vector3.One);
        private readonly Vector3 distanceBiasAbs = Vector3.Zero;
        private readonly ITransformProvider transformInput = new IdentityTransformProvider();
        private readonly INumberProvider speedMin = new LiteralNumberProvider(0);
        private readonly INumberProvider speedMax = new LiteralNumberProvider(0);
        private readonly float speedRandExp = 1f;
        private readonly bool localCoords;
        private readonly IVectorProvider localCoordinateSystemSpeedMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider localCoordinateSystemSpeedMax = new LiteralVectorProvider(Vector3.Zero);
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly ParticleField fieldVelocity = ParticleField.PositionPrevious;

        public CreateWithinSphereTransform(ParticleDefinitionParser parse) : base(parse)
        {
            radiusMin = parse.NumberProvider("m_fRadiusMin", radiusMin);
            radiusMax = parse.NumberProvider("m_fRadiusMax", radiusMax);
            distanceBias = parse.VectorProvider("m_vecDistanceBias", distanceBias);
            distanceBiasAbs = parse.Vector3("m_vecDistanceBiasAbs", distanceBiasAbs);
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            speedMin = parse.NumberProvider("m_fSpeedMin", speedMin);
            speedMax = parse.NumberProvider("m_fSpeedMax", speedMax);
            speedRandExp = parse.Float("m_fSpeedRandExp", speedRandExp);
            localCoords = parse.Boolean("m_bLocalCoords", localCoords);
            localCoordinateSystemSpeedMin = parse.VectorProvider("m_LocalCoordinateSystemSpeedMin", localCoordinateSystemSpeedMin);
            localCoordinateSystemSpeedMax = parse.VectorProvider("m_LocalCoordinateSystemSpeedMax", localCoordinateSystemSpeedMax);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            fieldVelocity = parse.ParticleField("m_nFieldVelocity", fieldVelocity);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var transform = transformInput.NextTransform(ref particle, particleSystemState);

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

            var minRadius = radiusMin.NextNumber(ref particle, particleSystemState);
            var maxRadius = radiusMax.NextNumber(ref particle, particleSystemState);
            var distance = ParticleCollection.RandomBetween(particle.ParticleID, minRadius, maxRadius);

            var localOffset = biasedDirection * distance;
            var worldOffset = Vector3.Transform(localOffset, transform);

            var position = worldOffset;
            particle.SetVector(fieldOutput, position);
            particle.PositionPrevious = position;

            var minSpeed = speedMin.NextNumber(ref particle, particleSystemState);
            var maxSpeed = speedMax.NextNumber(ref particle, particleSystemState);

            var speedT = ParticleCollection.RandomBetween(particle.ParticleID + 1, 0f, 1f);
            speedT = (float)Math.Pow(speedT, speedRandExp);
            var speed = float.Lerp(minSpeed, maxSpeed, speedT);

            var localSpeed = ParticleCollection.RandomBetweenPerComponent(
                particle.ParticleID + 2,
                localCoordinateSystemSpeedMin.NextVector(ref particle, particleSystemState),
                localCoordinateSystemSpeedMax.NextVector(ref particle, particleSystemState));

            Vector3 velocity;
            if (localCoords)
            {
                velocity = (biasedDirection * speed) + localSpeed;
            }
            else
            {
                var worldDirection = Vector3.TransformNormal(biasedDirection, transform);
                var worldLocalSpeed = Vector3.TransformNormal(localSpeed, transform);
                velocity = (worldDirection * speed) + worldLocalSpeed;
            }

            particle.SetVector(fieldVelocity, velocity);

            return particle;
        }
    }
}
