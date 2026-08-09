using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.PreEmissionOperators
{
    /// <summary>
    /// Linearly ramps a control point position each frame by a rate chosen randomly between
    /// a minimum and maximum vector. The rate is read from a table position fixed by the system's seed
    /// and this operator's place in the pre-emission list, so it holds for the system's lifetime and no
    /// draw counter is consumed.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RampCPLinearRandom">C_OP_RampCPLinearRandom</seealso>
    class RampCPLinearRandom : ParticleFunctionPreEmissionOperator
    {
        private readonly Vector3 rateMin = Vector3.Zero;
        private readonly Vector3 rateMax = Vector3.Zero;
        private readonly int cp = 1;

        public RampCPLinearRandom(ParticleDefinitionParser parse) : base(parse)
        {
            cp = parse.Int32("m_nOutControlPointNumber", cp);
            rateMin = parse.Vector3("m_vecRateMin", rateMin);
            rateMax = parse.Vector3("m_vecRateMax", rateMax);
        }

        public override void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            // This operator counts the seed twice where the shared per-particle draw counts it once
            var sampleId = (2 * particleSystemState.Random.Seed) + particleSystemState.Random.OperatorOffset;
            var rampRate = ParticleRandom.ForSampleBetweenPerComponent(sampleId, rateMin, rateMax);

            var cpPos = particleSystemState.GetControlPoint(cp).Position;
            particleSystemState.SetControlPointValue(cp, cpPos + (rampRate * frameTime));
        }
    }
}
