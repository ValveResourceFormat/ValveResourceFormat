using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Positions particles in a ring pattern around a transform, with configurable initial radius, thickness, and even or random angular distribution.
    /// </summary>
    /// <remarks>
    /// "Position Along Ring" in the particle editor. Like "Position Within Sphere Random", it can
    /// also impart radial force to particles via the min/max initial speed. Thickness spreads
    /// particles through a ball around the ring, not just along the radius.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RingWave">C_INIT_RingWave</seealso>
    class RingWave : ParticleFunctionInitializer
    {
        private readonly bool evenDistribution;
        private readonly INumberProvider initialRadius = new LiteralNumberProvider(0);
        private readonly INumberProvider thickness = new LiteralNumberProvider(0);
        private readonly INumberProvider particlesPerOrbit = new LiteralNumberProvider(-1);
        private readonly INumberProvider initialSpeedMin = new LiteralNumberProvider(0);
        private readonly INumberProvider initialSpeedMax = new LiteralNumberProvider(0);
        private readonly INumberProvider roll = new LiteralNumberProvider(0);
        private readonly INumberProvider pitch = new LiteralNumberProvider(0);
        private readonly INumberProvider yaw = new LiteralNumberProvider(0);

        /// <summary>Transform the ring is centred on, and whose rotation the ring plane lies in.</summary>
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();

        /// <summary>Drops the world Z part of the outward speed.</summary>
        private readonly bool xyVelocityOnly = true;

        private uint orbitCount;

        public RingWave(ParticleDefinitionParser parse) : base(parse)
        {
            evenDistribution = parse.Boolean("m_bEvenDistribution", evenDistribution);
            particlesPerOrbit = parse.CollectionNumberProvider("m_flParticlesPerOrbit", particlesPerOrbit);
            initialRadius = parse.NumberProvider("m_flInitialRadius", initialRadius);
            thickness = parse.NumberProvider("m_flThickness", thickness);
            initialSpeedMin = parse.NumberProvider("m_flInitialSpeedMin", initialSpeedMin);
            initialSpeedMax = parse.NumberProvider("m_flInitialSpeedMax", initialSpeedMax);
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            xyVelocityOnly = parse.Boolean("m_bXYVelocityOnly", xyVelocityOnly);
            roll = parse.NumberProvider("m_flRoll", roll);
            pitch = parse.NumberProvider("m_flPitch", pitch);
            yaw = parse.NumberProvider("m_flYaw", yaw);
        }

        public override void Reset()
        {
            orbitCount = 0;
        }

        public override ulong WrittenFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var thicknesses = thickness.Fill(run, particleSystemState);
            var perOrbit = particlesPerOrbit.NextNumber(particleSystemState);
            var radii = initialRadius.Fill(run, particleSystemState);
            var pitches = pitch.Fill(run, particleSystemState);
            var yaws = yaw.Fill(run, particleSystemState);
            var rolls = roll.Fill(run, particleSystemState);
            var speedMins = initialSpeedMin.Fill(run, particleSystemState);
            var speedMaxes = initialSpeedMax.Fill(run, particleSystemState);

            var creationTimes = run.ReadFloats(ParticleField.CreationTime);
            var positions = run.Vectors(ParticleField.Position);
            var previousPositions = SpawnPreviousPositions(run, particleSystemState);
            var activeParticles = particles.Count;
            var previousFrameTime = particleSystemState.PreviousStep;

            for (var i = 0; i < run.Length; i++)
            {
                var thicknessOffset = particleSystemState.Random.NextInUnitBall(out _) * thicknesses[i];

                var radius = radii[i];

                var transform = transformInput.NextTransformAtTime(particleSystemState, creationTimes[i]);

                var angle = GetNextAngle(perOrbit, activeParticles, particleSystemState);
                var (sin, cos) = MathF.SinCos(angle);
                var ringAngles = new Vector3(pitches[i], yaws[i], rolls[i]);
                var ring = Vector3.TransformNormal(new Vector3(radius * cos, radius * sin, 0f), EntityTransformHelper.EulerAnglesToRotationMatrix(ringAngles));

                var position = Vector3.Transform(ring + thicknessOffset, transform);

                // Initial speed pushes outward, along the line from the transform to where the particle
                // actually landed, so the thickness offset tilts the direction as well as the position.
                var outward = MathUtils.SafeNormalize(position - transform.Translation);

                var speed = particleSystemState.Random.NextBetween(speedMins[i], speedMaxes[i]);

                var previous = position - (outward * (speed * previousFrameTime));

                if (xyVelocityOnly)
                {
                    previous.Z = position.Z;
                }

                positions[i] = position;

                if (!previousPositions.IsEmpty)
                {
                    previousPositions[i] = previous;
                }
            }
        }

        /// <summary>
        /// The particle's index along the ring times the angle step. The index is never wrapped to the
        /// orbit and counts before use, so the first particle is one step past angle zero. Without even
        /// distribution the step is a fresh random angle, still multiplied by the index.
        /// </summary>
        private float GetNextAngle(float particlesPerOrbit, int activeParticles, ParticleSystemState particleSystemState)
        {
            orbitCount++;

            if (evenDistribution)
            {
                // -1 spreads one orbit over the particles alive right now, so a continuously emitted
                // ring stays closed instead of crawling around as an arc of the max particle count.
                var perOrbit = particlesPerOrbit == -1f ? activeParticles : particlesPerOrbit;

                return orbitCount * (MathF.Tau / MathF.Max(1f, perOrbit));
            }

            return orbitCount * (particleSystemState.Random.Next() * MathF.Tau);
        }
    }
}
