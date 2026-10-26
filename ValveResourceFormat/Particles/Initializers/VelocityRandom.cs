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

        public override ulong WrittenFields => ParticleField.PositionPrevious.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            // The local speeds are evaluated for every live particle, and each particle of the run reads its own value
            // rather than the value of the particle as far into the collection as it is into the run
            var vecMins = vectorMin.Prepare(run, particles.Current, particleSystemState);
            var vecMaxes = vectorMax.Prepare(run, particles.Current, particleSystemState);
            var speedMins = speedMin.Fill(run, particleSystemState);
            var speedMaxes = speedMax.Fill(run, particleSystemState);

            var creationTimes = run.ReadFloats(ParticleField.CreationTime);
            var previousPositions = run.Vectors(ParticleField.PositionPrevious);

            // The stratified sample index advances only for particles that take a local speed
            var sampleIndex = run.Start;

            for (var i = 0; i < previousPositions.Length; i++)
            {
                // Local speeds drawing per block draw at the start of each block, between the draws of its particles
                if (i % 4 == 0)
                {
                    var block = run.Slice(i, Math.Min(4, run.Length - i));
                    vectorMin.FillPerBlock(block, 0, particleSystemState, vecMins[i..]);
                    vectorMax.FillPerBlock(block, 0, particleSystemState, vecMaxes[i..]);
                }

                var vecMin = vecMins[i];
                var vecMax = vecMaxes[i];
                var velocity = Vector3.Zero;

                // The authored speed vector is expressed in the control point's local coordinate system.
                if (vecMin != Vector3.Zero || vecMax != Vector3.Zero)
                {
                    var localSpeed = rangeSampler.NextVectorBetween(sampleIndex++, particleSystemState, vecMin, vecMax);
                    var basis = particleSystemState.GetControlPoint(controlPoint).GetBasisAtTime(creationTimes[i]);
                    velocity = Vector3.TransformNormal(localSpeed, basis);
                }

                var speedmin = speedMins[i];
                var speedmax = speedMaxes[i];

                if (speedmin != 0f || speedmax != 0f)
                {
                    var direction = MathUtils.SafeNormalize(particleSystemState.Random.NextInUnitBall(out _));
                    velocity += direction * particleSystemState.Random.NextBetween(speedmin, speedmax);
                }

                previousPositions[i] -= VelocityStep(particleSystemState, velocity, ignoreDT);
            }
        }
    }
}
