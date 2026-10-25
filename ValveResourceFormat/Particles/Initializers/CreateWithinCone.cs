using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Emits particles from a ring around a transform's forward axis, moving outward along a cone at the
    /// given speed. The full cone angle is drawn between the inner and outer angles. The ring's radius is
    /// where that cone crosses the offset distance, taken as at least one unit, so a small or zero offset
    /// still leaves a ring around the transform rather than a point. The ring sits the offset along the
    /// axis, or in the transform's own plane when the offset is collapsed.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateWithinCone">C_INIT_CreateWithinCone</seealso>
    class CreateWithinCone : ParticleFunctionInitializer
    {
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly INumberProvider innerAngle = new LiteralNumberProvider(0f);
        private readonly INumberProvider outerAngle = new LiteralNumberProvider(30f);
        private readonly INumberProvider speed = new LiteralNumberProvider(0f);
        private readonly INumberProvider offset = new LiteralNumberProvider(0f);
        private readonly bool collapseOffset;

        public CreateWithinCone(ParticleDefinitionParser parse) : base(parse)
        {
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            innerAngle = parse.NumberProvider("m_flInnerAngle", innerAngle);
            outerAngle = parse.NumberProvider("m_flOuterAngle", outerAngle);
            speed = parse.NumberProvider("m_flSpeed", speed);
            offset = parse.NumberProvider("m_flOffset", offset);
            collapseOffset = parse.Boolean("m_bCollapseOffset", collapseOffset);
        }

        public override ulong WrittenFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            InitializeRun(run, particles, particleSystemState, blockRun: false);
        }

        protected override void InitializeBlocks(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            InitializeRun(run, particles, particleSystemState, blockRun: true);
        }

        private void InitializeRun(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState, bool blockRun)
        {
            var creationTimes = run.ReadFloats(ParticleField.CreationTime);
            var positions = run.Vectors(ParticleField.Position);
            var previous = SpawnPreviousPositions(run, particleSystemState);

            var inners = innerAngle.Fill(run, particleSystemState);
            var outers = outerAngle.Fill(run, particleSystemState);
            var speeds = speed.Fill(run, particleSystemState);
            var offsets = offset.Fill(run, particleSystemState);

            var blockAngleDraws = ParticleScratch.Floats(blockRun ? run.Length : 0);
            var blockPhis = ParticleScratch.Floats(blockRun ? run.Length : 0);

            if (blockRun)
            {
                DrawBlocks(particleSystemState.ThreadRandom, blockAngleDraws, blockPhis);
            }

            for (var i = 0; i < run.Length; i++)
            {
                var particleOffset = offsets[i];
                var angleDraw = blockRun ? blockAngleDraws[i] : particleSystemState.Random.Next();
                var halfAngle = float.DegreesToRadians(MathUtils.Lerp(inners[i], outers[i], angleDraw)) * 0.5f;

                // The ring is placed at least one unit along the axis so a zero offset still yields a
                // direction at the drawn angle; a negative offset flips the cone backwards.
                var axialDistance = MathF.Max(MathF.Abs(particleOffset), 1f) * (particleOffset < 0f ? -1f : 1f);
                var radius = axialDistance * MathF.Tan(halfAngle);

                var transform = transformInput.NextTransformAtTime(particleSystemState, creationTimes[i]);

                var phi = blockRun ? blockPhis[i] : particleSystemState.Random.Next() * MathF.Tau;
                var (sin, cos) = MathF.SinCos(phi);
                var forward = Vector3.TransformNormal(Vector3.UnitX, transform);
                var ringOffset = Vector3.TransformNormal(new Vector3(0f, radius * cos, radius * sin), transform);

                var position = transform.Translation + ringOffset;

                if (!collapseOffset)
                {
                    position += forward * particleOffset;
                }

                var direction = Vector3.Normalize((forward * axialDistance) + ringOffset);

                positions[i] = position;

                if (!previous.IsEmpty)
                {
                    previous[i] = position - (direction * (speeds[i] * particleSystemState.PreviousStep));
                }
            }
        }

        /// <summary>
        /// Takes the block run's draws from the thread generator, block by block of four: the angle draw,
        /// then the angle around the axis.
        /// </summary>
        private static void DrawBlocks(ThreadRandom generator, Span<float> angleDraws, Span<float> phis)
        {
            for (var blockStart = 0; blockStart < angleDraws.Length; blockStart += 4)
            {
                var angles = generator.NextBetween(0f, 1f);
                var blockPhis = generator.NextBetween(0f, MathF.Tau);

                for (var lane = 0; lane < 4; lane++)
                {
                    angleDraws[blockStart + lane] = angles[lane];
                    phis[blockStart + lane] = blockPhis[lane];
                }
            }
        }
    }
}
