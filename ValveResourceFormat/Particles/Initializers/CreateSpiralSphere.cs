namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Places particles one after another on a golden-angle spiral over a sphere around a transform,
    /// from the top down, and gives each an outward speed drawn between a minimum and a maximum. The
    /// density is how many particles one sweep of the sphere takes, multiplied by the number of
    /// particles in the emission when <c>m_bUseParticleCount</c> is set.
    /// </summary>
    /// <remarks>
    /// The sweep starts over at the top once more particles than the density have been placed. It
    /// restarts with the system. An emission of 16 or more particles is counted in up to three runs,
    /// as <see cref="ParticleFunctionInitializer.SplitIntoCalls"/> splits it. The previous position
    /// is left alone when an earlier initializer has already written it.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateSpiralSphere">C_INIT_CreateSpiralSphere</seealso>
    class CreateSpiralSphere : ParticleFunctionInitializer
    {
        private static readonly float GoldenAngle = (3f - MathF.Sqrt(5f)) * MathF.PI;

        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly INumberProvider density = new LiteralNumberProvider(0f);
        private readonly INumberProvider initialRadius = new LiteralNumberProvider(0f);
        private readonly INumberProvider initialSpeedMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider initialSpeedMax = new LiteralNumberProvider(0f);
        private readonly bool useParticleCount;

        private int index;
        private float azimuth;
        private float height = 1f;
        private float densityScale = 1f;

        public CreateSpiralSphere(ParticleDefinitionParser parse) : base(parse)
        {
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            density = parse.NumberProvider("m_flDensity", density);
            initialRadius = parse.NumberProvider("m_flInitialRadius", initialRadius);
            initialSpeedMin = parse.NumberProvider("m_flInitialSpeedMin", initialSpeedMin);
            initialSpeedMax = parse.NumberProvider("m_flInitialSpeedMax", initialSpeedMax);
            useParticleCount = parse.Boolean("m_bUseParticleCount", useParticleCount);
        }

        public override void Reset()
        {
            index = 0;
            azimuth = 0f;
            height = 1f;
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious);

        public override void Initialize(Span<Particle> batch, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var (head, blocks, tail) = SplitIntoCalls(batch);

            InitializeCall(batch[head], particles, particleSystemState);
            InitializeCall(batch[blocks], particles, particleSystemState);
            InitializeCall(batch[tail], particles, particleSystemState);
        }

        private void InitializeCall(Span<Particle> call, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            densityScale = useParticleCount ? call.Length : 1f;
            InitializeEach(call, particles, particleSystemState);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var radius = initialRadius.NextNumber(ref particle, particleSystemState);
            var speedMin = initialSpeedMin.NextNumber(ref particle, particleSystemState);
            var speedMax = initialSpeedMax.NextNumber(ref particle, particleSystemState);
            var pointCount = density.NextNumber(ref particle, particleSystemState) * densityScale;
            var heightStep = 2f / MathF.Max(1f, pointCount);

            if (index > pointCount || index == 0)
            {
                index = 0;
                azimuth = 0f;
                height = 1f - (heightStep * 0.5f);
            }

            var transform = transformInput.NextTransformAtTime(ref particle, particleSystemState, particle.CreationTime);
            var ring = MathF.Sqrt(MathF.Max(0f, 1f - (height * height)));
            var local = new Vector3(MathF.Cos(azimuth) * ring, MathF.Sin(azimuth) * ring, height) * radius;
            var position = Vector3.Transform(local, transform);

            var speed = particleSystemState.Random.NextBetween(speedMin, speedMax);
            var direction = MathUtils.SafeNormalize(position - transform.Translation);

            particle.Position = position;

            if ((particleSystemState.InitializedFields & FieldMask(ParticleField.PositionPrevious)) == 0)
            {
                particle.PositionPrevious = position - (direction * (speed * particles.PreviousFrameTime));
            }

            height -= heightStep;
            azimuth += GoldenAngle;
            index++;

            return particle;
        }
    }
}
