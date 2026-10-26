using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Initializes particle velocity to a random speed directed radially outward from a control point,
    /// or from a per-particle centre.
    /// </summary>
    /// <remarks>
    /// The local speed scale is rotated into world space by the centre's basis and then multiplies the
    /// direction per world axis, so it only acts along the frame's axes when the frame is axis aligned.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_VelocityRadialRandom">C_INIT_VelocityRadialRandom</seealso>
    class VelocityRadialRandom : ParticleFunctionInitializer
    {
        private readonly bool perParticleCenter;
        private readonly IVectorProvider centerPosition = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider centerForward = new LiteralVectorProvider(Vector3.UnitX);
        private readonly Vector3 localSpeedScale = Vector3.One;
        private readonly INumberProvider speedMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider speedMax = new LiteralNumberProvider(0f);

        private readonly int controlPoint;
        private readonly bool ignoreDelta;

        public VelocityRadialRandom(ParticleDefinitionParser parse) : base(parse)
        {
            perParticleCenter = parse.Boolean("m_bPerParticleCenter", perParticleCenter);
            centerPosition = parse.VectorProvider("m_vecPosition", centerPosition);
            centerForward = parse.VectorProvider("m_vecFwd", centerForward);
            localSpeedScale = parse.Vector3("m_vecLocalCoordinateSystemSpeedScale", localSpeedScale);
            speedMin = parse.NumberProvider("m_fSpeedMin", speedMin);
            speedMax = parse.NumberProvider("m_fSpeedMax", speedMax);
            ignoreDelta = parse.Boolean("m_bIgnoreDelta", ignoreDelta);
            controlPoint = parse.ControlPoint("m_nControlPointNumber", controlPoint);
        }

        public override ulong WrittenFields => ParticleField.PositionPrevious.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var speedMins = speedMin.Fill(run, particleSystemState);
            var speedMaxes = speedMax.Fill(run, particleSystemState);

            // Without a per-particle centre the inputs are set up but never evaluated per block
            var centers = centerPosition.Prepare(run, particleSystemState);
            var forwards = centerForward.Prepare(run, particleSystemState);

            var hasLocalSpeedScale = localSpeedScale != Vector3.One;

            var creationTimes = run.ReadFloats(ParticleField.CreationTime);
            var positions = run.ReadVectors(ParticleField.Position);
            var previousPositions = run.Vectors(ParticleField.PositionPrevious);

            for (var i = 0; i < previousPositions.Length; i++)
            {
                // Centres drawing per block draw at the start of each block, between the speed draws of its particles
                var lane = (run.Start + i) & 3;
                if (perParticleCenter && (i == 0 || lane == 0))
                {
                    var block = run.Slice(i, Math.Min(4 - lane, run.Length - i));
                    centerPosition.FillPerBlock(block, lane, particleSystemState, centers[i..]);
                    centerForward.FillPerBlock(block, lane, particleSystemState, forwards[i..]);
                }

                var center = perParticleCenter
                    ? centers[i]
                    : ParticlePath.GetControlPointAtTime(particleSystemState, controlPoint, creationTimes[i]);

                var direction = MathUtils.SafeNormalize(positions[i] - center);

                if (hasLocalSpeedScale)
                {
                    var forward = perParticleCenter ? forwards[i] : Vector3.Zero;
                    direction *= WorldSpeedScale(creationTimes[i], forward, particleSystemState);
                }

                var speed = particleSystemState.Random.NextBetween(speedMins[i], speedMaxes[i]);

                previousPositions[i] -= VelocityStep(particleSystemState, direction * speed, ignoreDelta);
            }
        }

        /// <summary>The local speed scale rotated into world space by the centre's basis.</summary>
        private Vector3 WorldSpeedScale(float creationTime, Vector3 forward, ParticleSystemState particleSystemState)
        {
            if (!perParticleCenter)
            {
                var basis = particleSystemState.GetControlPoint(controlPoint).GetBasisAtTime(creationTime);
                return Vector3.TransformNormal(localSpeedScale, basis);
            }

            var (left, up) = BasisFromForward(forward);

            return (forward * localSpeedScale.X) + (left * localSpeedScale.Y) + (up * localSpeedScale.Z);
        }

        /// <summary>
        /// Completes a left and up axis for <paramref name="forward"/>, with world up as the hint. A
        /// forward too close to the hint to cross with it takes the world Y axis as its left axis, and the
        /// up axis is always the normalized cross product, so it stays a unit vector for any forward length.
        /// </summary>
        private static (Vector3 Left, Vector3 Up) BasisFromForward(Vector3 forward)
        {
            var left = Vector3.Cross(Vector3.UnitZ, forward);

            if (left.LengthSquared() < ParticleMath.Square(ParticleMath.FloatEpsilon))
            {
                left = Vector3.UnitY;
            }
            else
            {
                left = Vector3.Normalize(left);
            }

            return (left, MathUtils.SafeNormalize(Vector3.Cross(forward, left)));
        }
    }
}
