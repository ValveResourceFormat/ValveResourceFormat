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

        public override ulong WrittenFields => ParticleField.HitboxIndex.Mask() | ParticleField.HitboxOffsetPosition.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (HitBoxCache.Refresh(particleSystemState, controlPoint, hitboxSetName, useBones) is not { } cache)
            {
                return;
            }

            var haltonIndex = particleSystemState.SpawnIndex(run.Start);
            var batchHitbox = 0;
            var scale = hitBoxScale.NextVector(particleSystemState);
            var shell = shellSize.NextNumber(particleSystemState);
            var origin = particleSystemState.GetControlPoint(controlPoint).Position;
            var currentIndices = run.ReadIntegers(ParticleField.HitboxIndex);
            var offsets = UnwrittenVectors(run, particleSystemState, ParticleField.HitboxOffsetPosition);
            var indices = UnwrittenIntegers(run, particleSystemState, ParticleField.HitboxIndex);

            for (var i = 0; i < run.Length; i++)
            {
                if (i % BatchSize == 0)
                {
                    batchHitbox = currentIndices[i];
                }

                var desired = maintainHitbox ? batchHitbox : desiredHitbox;
                var sampling = new HitBoxSampling(forceInModel, desired, scale, directionBias, shell, evenDistribution, WeightByVolume: false);
                var (_, fraction, index) = cache.RandomPoint(particleSystemState, origin, sampling, ref haltonIndex);

                if (!offsets.IsEmpty)
                {
                    offsets[i] = fraction;
                }

                if (!indices.IsEmpty)
                {
                    indices[i] = index;
                }
            }
        }
    }
}
