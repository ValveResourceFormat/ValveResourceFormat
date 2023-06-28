using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.PreEmissionOperators
{
    class RampCPLinearRandom : IParticlePreEmissionOperator
    {
        private readonly Vector3 rampRate = Vector3.Zero;
        private readonly int cp;

        public RampCPLinearRandom(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nOutputControlPointNumber"))
            {
                cp = keyValues.GetInt32Property("m_nOutputControlPointNumber");
            }

            var rateMin = Vector3.Zero;
            var rateMax = Vector3.Zero;

            if (keyValues.ContainsKey("m_vecRateMin"))
            {
                rateMin = keyValues.GetArray<double>("m_vecRateMin").ToVector3();
            }

            if (keyValues.ContainsKey("m_vecRateMax"))
            {
                rateMin = keyValues.GetArray<double>("m_vecRateMax").ToVector3();
            }

            rampRate = MathUtils.RandomBetweenPerComponent(rateMin, rateMax);
        }

        public void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            var cpPos = particleSystemState.GetControlPoint(cp).Position;
            particleSystemState.SetControlPointValue(cp, cpPos + (rampRate * frameTime));
        }
    }
}
