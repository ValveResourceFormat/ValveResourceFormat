using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.PreEmissionOperators
{
    class DistanceBetweenCPsToCP : IParticlePreEmissionOperator
    {
        private readonly float distanceMin;
        private readonly float distanceMax = 128;
        private readonly float outputMin;
        private readonly float outputMax = 1;

        private readonly int startCP;
        private readonly int endCP = 1;
        private readonly int outputCP = 2;
        private readonly int outputCPField;

        public DistanceBetweenCPsToCP(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flInputMin"))
            {
                distanceMin = keyValues.GetFloatProperty("m_flInputMin");
            }

            if (keyValues.ContainsKey("m_flInputMax"))
            {
                distanceMax = keyValues.GetFloatProperty("m_flInputMax");
            }

            if (keyValues.ContainsKey("m_flOutputMin"))
            {
                outputMin = keyValues.GetFloatProperty("m_flOutputMin");
            }

            if (keyValues.ContainsKey("m_flOutputMax"))
            {
                outputMax = keyValues.GetFloatProperty("m_flOutputMax");
            }

            if (keyValues.ContainsKey("m_nStartCP"))
            {
                startCP = keyValues.GetInt32Property("m_nStartCP");
            }

            if (keyValues.ContainsKey("m_nEndCP"))
            {
                endCP = keyValues.GetInt32Property("m_nEndCP");
            }

            if (keyValues.ContainsKey("m_nOutputCP"))
            {
                outputCP = keyValues.GetInt32Property("m_nOutputCP");
            }

            if (keyValues.ContainsKey("m_nOutputCPField"))
            {
                outputCPField = keyValues.GetInt32Property("m_nOutputCPField");
            }

            // Unsupported features: LOS test
        }

        public void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            var startCP = particleSystemState.GetControlPoint(this.startCP);
            var endCP = particleSystemState.GetControlPoint(this.endCP);

            var distance = Vector3.Distance(startCP.Position, endCP.Position);

            var remappedDistance = MathUtils.Remap(distance, distanceMin, distanceMax);

            // always clamped to output min/max
            remappedDistance = MathUtils.Saturate(remappedDistance);

            var finalValue = MathUtils.Lerp(remappedDistance, outputMin, outputMax);

            particleSystemState.SetControlPointValueComponent(outputCP, outputCPField, finalValue);
        }
    }
}
