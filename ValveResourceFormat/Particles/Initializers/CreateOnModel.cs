using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Places new particles at random points inside the hitboxes, or the bones, of the model its model
    /// input reads, recording the box and the point's place in it for the functions that keep
    /// particles on the model. Particles can inherit the velocity of their box.
    /// </summary>
    /// <remarks>
    /// <c>m_bUseMesh</c>, which places particles on the triangles of the render mesh, is not supported:
    /// such particles are placed on the hitboxes instead.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateOnModel">C_INIT_CreateOnModel</seealso>
    class CreateOnModel : ParticleFunctionInitializer
    {
        private readonly int modelControlPoint;
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly int forceInModel;
        private readonly bool scaleToVolume;
        private readonly bool evenDistribution;
        private readonly INumberProvider desiredHitbox = new LiteralNumberProvider(-1f);
        private readonly int hitboxValueFromControlPoint = -1;
        private readonly IVectorProvider hitBoxScale = new LiteralVectorProvider(Vector3.One);
        private readonly float boneVelocity;
        private readonly float maxBoneVelocity;
        private readonly IVectorProvider directionBias = new LiteralVectorProvider(Vector3.Zero);
        private readonly string hitboxSetName = "default";
        private readonly bool localCoords;
        private readonly bool useBones;
        private readonly INumberProvider shellSize = new LiteralNumberProvider(1f);

        private ParticleRandom.EvaluationKey? callKey;
        private int haltonIndex;

        public CreateOnModel(ParticleDefinitionParser parse) : base(parse)
        {
            modelControlPoint = ModelInput.ControlPoint(parse);
            transformInput = parse.TransformInput("m_transformInput", transformInput);
            forceInModel = parse.Int32("m_nForceInModel", forceInModel);
            scaleToVolume = parse.Boolean("m_bScaleToVolume", scaleToVolume);
            evenDistribution = parse.Boolean("m_bEvenDistribution", evenDistribution);
            desiredHitbox = parse.CollectionNumberProvider("m_nDesiredHitbox", desiredHitbox);
            hitboxValueFromControlPoint = parse.Int32("m_nHitboxValueFromControlPointIndex", hitboxValueFromControlPoint);
            hitBoxScale = parse.CollectionVectorProvider("m_vecHitBoxScale", hitBoxScale);
            boneVelocity = parse.Float("m_flBoneVelocity", boneVelocity);
            maxBoneVelocity = parse.Float("m_flMaxBoneVelocity", maxBoneVelocity);
            directionBias = parse.CollectionVectorProvider("m_vecDirectionBias", directionBias);
            hitboxSetName = parse.String("m_HitboxSetName", hitboxSetName);
            localCoords = parse.Boolean("m_bLocalCoords", localCoords);
            useBones = parse.Boolean("m_bUseBones", useBones);
            shellSize = parse.CollectionNumberProvider("m_flShellSize", shellSize);
        }

        public override void Reset()
        {
            callKey = null;
        }

        public override ulong WrittenFields
            => FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious) | FieldMask(ParticleField.CreationTime)
                | FieldMask(ParticleField.HitboxIndex) | FieldMask(ParticleField.HitboxOffsetPosition);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var shell = shellSize.NextNumber(ref particle, particleSystemState);
            var desired = hitboxValueFromControlPoint >= 0
                ? (int)particleSystemState.GetControlPoint(hitboxValueFromControlPoint).Position.X
                : (int)desiredHitbox.NextNumber(ref particle, particleSystemState);
            var scale = hitBoxScale.NextVector(ref particle, particleSystemState);
            var bias = directionBias.NextVector(ref particle, particleSystemState);
            var transform = transformInput.NextTransformAtTime(ref particle, particleSystemState, particle.CreationTime);

            if (localCoords)
            {
                bias = Vector3.TransformNormal(bias, transform);
            }

            if (modelControlPoint < 0 || HitBoxCache.Refresh(particleSystemState, modelControlPoint, hitboxSetName, useBones, scaleToVolume && !useBones) is not { } cache)
            {
                return particle;
            }

            var sampling = new HitBoxSampling(forceInModel, desired, scale, bias, shell, evenDistribution, scaleToVolume && !useBones);
            var key = particleSystemState.Random.Evaluation(-1);
            if (callKey != key)
            {
                callKey = key;
                haltonIndex = particle.UniqueParticleId;
            }

            var (position, fraction, index) = cache.RandomPoint(particleSystemState, transform.Translation, sampling, ref haltonIndex);

            particle.Position = position;
            particle.HitboxIndex = index;
            particle.PositionPrevious = position;
            particle.HitboxOffsetPosition = fraction;

            if (boneVelocity != 0f && cache.BoxVelocity(index) is { } velocity)
            {
                velocity *= boneVelocity;

                if (maxBoneVelocity > 0f && velocity.LengthSquared() > maxBoneVelocity * maxBoneVelocity)
                {
                    velocity = Vector3.Normalize(velocity) * maxBoneVelocity;
                }

                particle.PositionPrevious -= velocity * particles.PreviousFrameTime;
            }

            return particle;
        }
    }
}
