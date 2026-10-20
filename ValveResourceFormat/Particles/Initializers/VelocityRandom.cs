using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Initializes particle velocity to a random direction and speed, with independent per-axis speed ranges in a local coordinate system.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_VelocityRandom">C_INIT_VelocityRandom</seealso>
    class VelocityRandom : ParticleFunctionInitializer
    {
        private readonly IVectorProvider vectorMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider vectorMax = new LiteralVectorProvider(Vector3.Zero);
        private readonly INumberProvider speedMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider speedMax = new LiteralNumberProvider(0f);
        private readonly int controlPoint;
        private readonly bool ignoreDT;
        private readonly RangeSampler rangeSampler;

        public VelocityRandom(ParticleDefinitionParser parse) : base(parse)
        {
            vectorMin = parse.VectorProvider("m_LocalCoordinateSystemSpeedMin", vectorMin);
            vectorMax = parse.VectorProvider("m_LocalCoordinateSystemSpeedMax", vectorMax);
            speedMin = parse.NumberProvider("m_fSpeedMin", speedMin);
            speedMax = parse.NumberProvider("m_fSpeedMax", speedMax);
            controlPoint = parse.ControlPoint("m_nControlPointNumber", controlPoint);
            ignoreDT = parse.Boolean("m_bIgnoreDT", ignoreDT);
            rangeSampler = RangeSampler.Parse(parse);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.PositionPrevious);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var vecMin = vectorMin.NextVector(ref particle, particleSystemState);
            var vecMax = vectorMax.NextVector(ref particle, particleSystemState);

            var velocity = Vector3.Zero;

            // The authored speed vector is expressed in the control point's local coordinate system.
            if (vecMin != Vector3.Zero || vecMax != Vector3.Zero)
            {
                var localSpeed = rangeSampler.NextVectorBetween(ref particle, particleSystemState, vecMin, vecMax);
                var basis = particleSystemState.GetControlPoint(controlPoint).GetBasisAtTime(particle.CreationTime);
                velocity = Vector3.TransformNormal(localSpeed, basis);
            }

            var speedmin = speedMin.NextNumber(ref particle, particleSystemState);
            var speedmax = speedMax.NextNumber(ref particle, particleSystemState);

            if (speedmin != 0f || speedmax != 0f)
            {
                var direction = MathUtils.SafeNormalize(particleSystemState.Random.NextInUnitBall(out _));
                velocity += direction * particleSystemState.Random.NextBetween(speedmin, speedmax);
            }

            AddVelocity(ref particle, particles, velocity, ignoreDT);

            return particle;
        }
    }
}
