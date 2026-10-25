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

        public override void Restart(ParticleSystemState particleSystemState) => Reset();

        public override ulong WrittenFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var densityScale = useParticleCount ? run.Length : 1f;

            var creationTimes = run.ReadFloats(ParticleField.CreationTime);
            var positions = run.Vectors(ParticleField.Position);
            var previous = SpawnPreviousPositions(run, particleSystemState);

            var radii = initialRadius.Fill(run, particleSystemState);
            var speedMins = initialSpeedMin.Fill(run, particleSystemState);
            var speedMaxs = initialSpeedMax.Fill(run, particleSystemState);
            var densities = density.Fill(run, particleSystemState);

            for (var i = 0; i < run.Length; i++)
            {
                var pointCount = densities[i] * densityScale;
                var heightStep = 2f / MathF.Max(1f, pointCount);

                if (index > pointCount || index == 0)
                {
                    index = 0;
                    azimuth = 0f;
                    height = 1f - (heightStep * 0.5f);
                }

                var transform = transformInput.NextTransformAtTime(particleSystemState, creationTimes[i]);
                var ring = MathF.Sqrt(MathF.Max(0f, 1f - (height * height)));
                var local = new Vector3(MathF.Cos(azimuth) * ring, MathF.Sin(azimuth) * ring, height) * radii[i];
                var position = Vector3.Transform(local, transform);

                var speed = particleSystemState.Random.NextBetween(speedMins[i], speedMaxs[i]);
                var direction = MathUtils.SafeNormalize(position - transform.Translation);

                positions[i] = position;

                if (!previous.IsEmpty)
                {
                    previous[i] = position - (direction * (speed * particleSystemState.PreviousStep));
                }

                height -= heightStep;
                azimuth += GoldenAngle;
                index++;
            }
        }
    }
}
