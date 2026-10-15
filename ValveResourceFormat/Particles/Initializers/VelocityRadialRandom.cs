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
            controlPoint = Math.Clamp(parse.Int32("m_nControlPointNumber", controlPoint), 0, 63);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.PositionPrevious);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var center = perParticleCenter
                ? centerPosition.NextVector(ref particle, particleSystemState)
                : ParticlePath.GetControlPointAtTime(particleSystemState, controlPoint, particle.CreationTime);

            var direction = MathUtils.SafeNormalize(particle.Position - center);

            if (localSpeedScale != Vector3.One)
            {
                direction *= WorldSpeedScale(ref particle, particleSystemState);
            }

            var speed = particleSystemState.Random.NextBetween(
                speedMin.NextNumber(ref particle, particleSystemState),
                speedMax.NextNumber(ref particle, particleSystemState));

            var velocity = direction * speed;

            // With the flag set the authored value is a raw per-step displacement, not units/second.
            particle.PositionPrevious -= ignoreDelta ? velocity : velocity * particles.PreviousFrameTime;

            return particle;
        }

        /// <summary>The local speed scale rotated into world space by the centre's basis.</summary>
        private Vector3 WorldSpeedScale(ref Particle particle, ParticleSystemState particleSystemState)
        {
            Vector3 forward, left, up;

            if (perParticleCenter)
            {
                forward = centerForward.NextVector(ref particle, particleSystemState);
                (left, up) = BasisFromForward(forward);
            }
            else
            {
                var rotation = particleSystemState.GetControlPoint(controlPoint).GetRotation();
                forward = Vector3.Transform(Vector3.UnitX, rotation);
                left = Vector3.Transform(Vector3.UnitY, rotation);
                up = Vector3.Transform(Vector3.UnitZ, rotation);
            }

            return (forward * localSpeedScale.X) + (left * localSpeedScale.Y) + (up * localSpeedScale.Z);
        }

        /// <summary>Completes a left and up axis for <paramref name="forward"/>, with world up as the hint.</summary>
        private static (Vector3 Left, Vector3 Up) BasisFromForward(Vector3 forward)
        {
            var left = Vector3.Cross(Vector3.UnitZ, forward);
            left = left.LengthSquared() < 1.1920929e-7f * 1.1920929e-7f ? Vector3.UnitY : Vector3.Normalize(left);

            return (left, MathUtils.SafeNormalize(Vector3.Cross(forward, left)));
        }
    }
}
