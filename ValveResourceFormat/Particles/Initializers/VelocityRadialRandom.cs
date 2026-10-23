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

        public override ulong WrittenFields => FieldMask(ParticleField.PositionPrevious);

        public override Particle Initialize(Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var center = perParticleCenter
                ? centerPosition.NextVector(particle, particleSystemState)
                : ParticlePath.GetControlPointAtTime(particleSystemState, controlPoint, particle.CreationTime);

            var direction = MathUtils.SafeNormalize(particle.Position - center);

            if (localSpeedScale != Vector3.One)
            {
                direction *= WorldSpeedScale(particle, particleSystemState);
            }

            var speed = particleSystemState.Random.NextBetween(
                speedMin.NextNumber(particle, particleSystemState),
                speedMax.NextNumber(particle, particleSystemState));

            AddVelocity(particle, particles, direction * speed, ignoreDelta);

            return particle;
        }

        /// <summary>The local speed scale rotated into world space by the centre's basis.</summary>
        private Vector3 WorldSpeedScale(Particle particle, ParticleSystemState particleSystemState)
        {
            if (!perParticleCenter)
            {
                var basis = particleSystemState.GetControlPoint(controlPoint).GetBasisAtTime(particle.CreationTime);
                return Vector3.TransformNormal(localSpeedScale, basis);
            }

            var forward = centerForward.NextVector(particle, particleSystemState);
            var (left, up) = BasisFromForward(forward);

            return (forward * localSpeedScale.X) + (left * localSpeedScale.Y) + (up * localSpeedScale.Z);
        }

        /// <summary>
        /// Completes a left and up axis for <paramref name="forward"/>, with world up as the hint. A
        /// forward too close to the hint to cross with it falls back to <see cref="ParticleMath.VectorVectors"/>.
        /// </summary>
        private static (Vector3 Left, Vector3 Up) BasisFromForward(Vector3 forward)
        {
            var left = Vector3.Cross(Vector3.UnitZ, forward);

            if (left.LengthSquared() < ParticleMath.Square(ParticleMath.FloatEpsilon))
            {
                return ParticleMath.VectorVectors(forward);
            }

            left = Vector3.Normalize(left);

            return (left, MathUtils.SafeNormalize(Vector3.Cross(forward, left)));
        }
    }
}
