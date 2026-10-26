using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Places particles at random positions within a sphere centred on a transform, and assigns an
    /// initial outward velocity sampled from a speed range. A per-axis distance bias skews the
    /// spherical distribution, and a local coordinate system speed adds a directional component to
    /// the velocity.
    /// </summary>
    /// <remarks>
    /// The whole blocks of four of an emission of 16 or more particles draw from the thread generator
    /// instead of the random table, picking the point by rejection from a cube.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateWithinSphereTransform">C_INIT_CreateWithinSphereTransform</seealso>
    class CreateWithinSphereTransform : ParticleFunctionInitializer
    {
        /// <summary>Transform the sphere is centred on, and whose rotation local coordinates use.</summary>
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();

        /// <summary>Minimum distance to spawn from the center of the sphere.</summary>
        private readonly INumberProvider radiusMin = new LiteralNumberProvider(0);

        /// <summary>Maximum distance to spawn from the center of the sphere.</summary>
        private readonly INumberProvider radiusMax = new LiteralNumberProvider(0);

        /// <summary>Minimum initial speed of the particle emitted outward from the sphere.</summary>
        private readonly INumberProvider speedMin = new LiteralNumberProvider(0);

        /// <summary>Maximum initial speed of the particle emitted outward from the sphere.</summary>
        private readonly INumberProvider speedMax = new LiteralNumberProvider(0);

        /// <summary>
        /// Local space minimum initial speed of the particle in x y z. It is rotated by the
        /// transform whether or not local coordinates are enabled.
        /// </summary>
        private readonly IVectorProvider localCoordinateSystemSpeedMin = new LiteralVectorProvider(Vector3.Zero);

        /// <summary>
        /// Local space maximum initial speed of the particle in x y z. It is rotated by the
        /// transform whether or not local coordinates are enabled.
        /// </summary>
        private readonly IVectorProvider localCoordinateSystemSpeedMax = new LiteralVectorProvider(Vector3.Zero);

        /// <summary>Exponent biasing the radial speed draw. 1 is an unbiased uniform draw.</summary>
        private readonly float speedRandExp = 1f;

        /// <summary>Per-axis multiplier applied to the spawn direction before it is normalised.</summary>
        private readonly IVectorProvider distanceBias = new LiteralVectorProvider(Vector3.One);

        /// <summary>
        /// Per-axis flags. A non-zero component folds the spawn direction into the positive half of
        /// that axis, turning the sphere into a hemisphere or an octant of one.
        /// </summary>
        private readonly Vector3 distanceBiasAbs = Vector3.Zero;

        /// <summary>
        /// Whether the spawn direction is rotated by the transform. It is only rotated while the
        /// distance bias or its absolute axes shape the sphere.
        /// </summary>
        private readonly bool localCoords;

        /// <summary>Attribute the spawn position is written to.</summary>
        private readonly ParticleField outputField = ParticleField.Position;

        /// <summary>
        /// Attribute the spawn velocity is written to. <see cref="ParticleField.PositionPrevious"/> gives the
        /// particle that velocity; any other field receives the spawn position minus the velocity, unscaled
        /// by the step.
        /// </summary>
        private readonly ParticleField velocityField = ParticleField.PositionPrevious;

        public CreateWithinSphereTransform(ParticleDefinitionParser parse) : base(parse)
        {
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            radiusMin = parse.NumberProvider("m_fRadiusMin", radiusMin);
            radiusMax = parse.NumberProvider("m_fRadiusMax", radiusMax);
            speedMin = parse.NumberProvider("m_fSpeedMin", speedMin);
            speedMax = parse.NumberProvider("m_fSpeedMax", speedMax);
            speedRandExp = Math.Clamp(parse.Float("m_fSpeedRandExp", speedRandExp), -255f, 255f);
            localCoordinateSystemSpeedMin = parse.VectorProvider("m_LocalCoordinateSystemSpeedMin", localCoordinateSystemSpeedMin);
            localCoordinateSystemSpeedMax = parse.VectorProvider("m_LocalCoordinateSystemSpeedMax", localCoordinateSystemSpeedMax);
            distanceBias = parse.VectorProvider("m_vecDistanceBias", distanceBias);
            distanceBiasAbs = parse.Vector3("m_vecDistanceBiasAbs", distanceBiasAbs);
            localCoords = parse.Boolean("m_bLocalCoords", localCoords);
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            velocityField = parse.ParticleField("m_nFieldVelocity", velocityField);
        }

        public override ulong WrittenFields => outputField == ParticleField.Position
            ? ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask() | velocityField.Mask()
            : outputField.Mask() | velocityField.Mask();

        private bool WritesVelocity(ParticleSystemState particleSystemState)
        {
            if (velocityField is ParticleField.NoneDisabled || velocityField < 0)
            {
                return false;
            }

            return IsUnwritten(particleSystemState, velocityField);
        }

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
            var outputs = ParticleBuffer.IsVector(outputField) ? run.Vectors(outputField) : default;
            var writesVelocity = WritesVelocity(particleSystemState);
            var velocities = writesVelocity && ParticleBuffer.IsVector(velocityField) ? run.Vectors(velocityField) : default;
            var previous = writesVelocity && outputField == ParticleField.Position ? run.Vectors(ParticleField.PositionPrevious) : default;

            var radiusMins = radiusMin.Fill(run, particleSystemState);
            var radiusMaxs = radiusMax.Fill(run, particleSystemState);
            var speedMins = speedMin.Fill(run, particleSystemState);
            var speedMaxs = speedMax.Fill(run, particleSystemState);
            // The block run evaluates the vector inputs for every live particle, and each particle of the run reads its own value
            // rather than the value of the particle as far into the collection as it is into the run
            var evaluated = blockRun ? particles.Current : run;
            var biases = distanceBias.Prepare(run, evaluated, particleSystemState);
            var localSpeedMins = localCoordinateSystemSpeedMin.Prepare(run, evaluated, particleSystemState);
            var localSpeedMaxs = localCoordinateSystemSpeedMax.Prepare(run, evaluated, particleSystemState);

            foreach (var (offset, block, lane) in run.InputBlocks())
            {
                distanceBias.FillPerBlock(block, lane, particleSystemState, biases[offset..]);

                if (writesVelocity)
                {
                    localCoordinateSystemSpeedMin.FillPerBlock(block, lane, particleSystemState, localSpeedMins[offset..]);
                    localCoordinateSystemSpeedMax.FillPerBlock(block, lane, particleSystemState, localSpeedMaxs[offset..]);
                }
            }

            var blockPoints = ParticleScratch.Vectors(blockRun ? run.Length : 0);
            var blockSpeedDraws = ParticleScratch.Floats(blockRun ? run.Length : 0);
            var blockLocalSpeedDraws = ParticleScratch.Vectors(blockRun ? run.Length : 0);

            if (blockRun)
            {
                DrawBlocks(particleSystemState.ThreadRandom, writesVelocity, blockPoints, blockSpeedDraws, blockLocalSpeedDraws);
            }

            for (var i = 0; i < run.Length; i++)
            {
                Vector3 randomVector;
                float radiusFraction;

                if (blockRun)
                {
                    randomVector = blockPoints[i];
                    radiusFraction = randomVector.Length();
                }
                else
                {
                    randomVector = particleSystemState.Random.NextInUnitBall(out radiusFraction);
                }

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

                var bias = biases[i];

                var direction = MathUtils.SafeNormalize(randomVector * bias);

                var transform = transformInput.NextTransformAtTime(particleSystemState, creationTimes[i]);

                if (localCoords && (bias != Vector3.One || distanceBiasAbs != Vector3.Zero))
                {
                    direction = Vector3.TransformNormal(direction, transform);
                }

                var distance = MathUtils.Lerp(radiusMins[i], radiusMaxs[i], radiusFraction);

                // The centre is the transform interpolated to the creation time once. Outside the block run
                // the game interpolates the already interpolated centre again by the same fraction, which
                // is not reproduced.
                var worldOffset = transform.Translation + (direction * distance);

                if (!outputs.IsEmpty)
                {
                    outputs[i] = worldOffset;
                }

                if (!writesVelocity)
                {
                    continue;
                }

                float speed;
                Vector3 localCoordinateSystemSpeed;

                if (blockRun)
                {
                    speed = MathUtils.Lerp(speedMins[i], speedMaxs[i], ParticleMath.PowQuarterSteps(blockSpeedDraws[i], speedRandExp));
                    localCoordinateSystemSpeed = MathUtils.Lerp(localSpeedMins[i], localSpeedMaxs[i], blockLocalSpeedDraws[i]);
                }
                else
                {
                    speed = particleSystemState.Random.NextWithExponentBetween(speedRandExp, speedMins[i], speedMaxs[i]);
                    localCoordinateSystemSpeed = particleSystemState.Random.NextBetweenPerComponent(localSpeedMins[i], localSpeedMaxs[i]);
                }

                var velocityLocal = Vector3.TransformNormal(localCoordinateSystemSpeed, transform);
                var velocity = -(direction * speed) - velocityLocal;

                if (velocityField == ParticleField.PositionPrevious)
                {
                    velocity *= particleSystemState.PreviousStep;
                }
                else if (!previous.IsEmpty)
                {
                    previous[i] = worldOffset;
                }

                if (!velocities.IsEmpty)
                {
                    velocities[i] = worldOffset + velocity;
                }
            }
        }

        /// <summary>
        /// Takes the block run's draws from the thread generator, block by block of four: a point in the
        /// unit ball, whose length is the radius fraction, then with a velocity the speed draw and the
        /// three local speed draws.
        /// </summary>
        private static void DrawBlocks(ThreadRandom generator, bool writesVelocity, Span<Vector3> points, Span<float> speedDraws, Span<Vector3> localSpeedDraws)
        {
            for (var blockStart = 0; blockStart < points.Length; blockStart += 4)
            {
                generator.NextInUnitBall(points.Slice(blockStart, 4));

                if (!writesVelocity)
                {
                    continue;
                }

                var speeds = generator.Next();
                var localX = generator.Next();
                var localY = generator.Next();
                var localZ = generator.Next();

                for (var lane = 0; lane < 4; lane++)
                {
                    speedDraws[blockStart + lane] = speeds[lane];
                    localSpeedDraws[blockStart + lane] = new Vector3(localX[lane], localY[lane], localZ[lane]);
                }
            }
        }
    }
}
