using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Picks a random hitbox, or bone, of the model a control point carries for each new particle,
    /// and a point in it, recording both for the functions that move particles onto the model. The
    /// particle itself is not moved.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_SetHitboxToModel">C_INIT_SetHitboxToModel</seealso>
    class SetHitboxToModel : ParticleFunctionInitializer
    {
        private const int BatchSize = 100;

        private readonly int controlPoint;
        private readonly int forceInModel;
        private readonly bool evenDistribution;
        private readonly int desiredHitbox = -1;
        private readonly IVectorProvider hitBoxScale = new LiteralVectorProvider(Vector3.One);
        private readonly Vector3 directionBias;
        private readonly bool maintainHitbox;
        private readonly bool useBones;
        private readonly string hitboxSetName = "default";
        private readonly INumberProvider shellSize = new LiteralNumberProvider(1f);

        private int callParticles;
        private int batchHitbox;
        private int haltonIndex;

        public SetHitboxToModel(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
            forceInModel = parse.Int32("m_nForceInModel", forceInModel);
            evenDistribution = parse.Boolean("m_bEvenDistribution", evenDistribution);
            desiredHitbox = parse.Int32("m_nDesiredHitbox", desiredHitbox);
            hitBoxScale = parse.CollectionVectorProvider("m_vecHitBoxScale", hitBoxScale);
            directionBias = parse.Vector3("m_vecDirectionBias", directionBias);
            maintainHitbox = parse.Boolean("m_bMaintainHitbox", maintainHitbox);
            useBones = parse.Boolean("m_bUseBones", useBones);
            hitboxSetName = parse.String("m_HitboxSetName", hitboxSetName);
            shellSize = parse.CollectionNumberProvider("m_flShellSize", shellSize);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.HitboxIndex) | FieldMask(ParticleField.HitboxOffsetPosition);

        protected override void BeginCall(Particle first, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            callParticles = 0;
            haltonIndex = first.UniqueParticleId;
        }

        public override Particle Initialize(Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (callParticles++ % BatchSize == 0)
            {
                batchHitbox = particle.HitboxIndex;
            }

            if (HitBoxCache.Refresh(particleSystemState, controlPoint, hitboxSetName, useBones) is not { } cache)
            {
                return particle;
            }

            var scale = hitBoxScale.NextVector(particle, particleSystemState);
            var shell = shellSize.NextNumber(particle, particleSystemState);
            var desired = maintainHitbox ? batchHitbox : desiredHitbox;
            var sampling = new HitBoxSampling(forceInModel, desired, scale, directionBias, shell, evenDistribution, WeightByVolume: false);
            var origin = particleSystemState.GetControlPoint(controlPoint).Position;
            var (_, fraction, index) = cache.RandomPoint(particleSystemState, origin, sampling, ref haltonIndex);

            if (IsUnwritten(particleSystemState, ParticleField.HitboxOffsetPosition))
            {
                particle.HitboxOffsetPosition = fraction;
            }

            if (IsUnwritten(particleSystemState, ParticleField.HitboxIndex))
            {
                particle.HitboxIndex = index;
            }

            return particle;
        }
    }
}
