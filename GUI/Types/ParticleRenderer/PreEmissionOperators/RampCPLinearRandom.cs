using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.PreEmissionOperators
{
    class RampCPLinearRandom : IParticlePreEmissionOperator
    {
        private readonly Vector3 rampRate = Vector3.Zero;
        private readonly int cp;

        public RampCPLinearRandom(ParticleDefinitionParser parse)
        {
            cp = parse.Int32("m_nOutputControlPointNumber", cp);
            var rateMin = parse.Vector3("m_vecRateMin", Vector3.Zero);
            var rateMax = parse.Vector3("m_vecRateMax", Vector3.Zero);

            rampRate = MathUtils.RandomBetweenPerComponent(rateMin, rateMax);
        }

        public void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            var cpPos = particleSystemState.GetControlPoint(cp).Position;
            particleSystemState.SetControlPointValue(cp, cpPos + (rampRate * frameTime));
        }
    }
}
