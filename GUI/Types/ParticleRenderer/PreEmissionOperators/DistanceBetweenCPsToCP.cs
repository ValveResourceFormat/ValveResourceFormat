using GUI.Utils;

namespace GUI.Types.ParticleRenderer.PreEmissionOperators
{
    class DistanceBetweenCPsToCP : ParticleFunctionPreEmissionOperator
    {
        private readonly float distanceMin;
        private readonly float distanceMax = 128;
        private readonly float outputMin;
        private readonly float outputMax = 1;

        private readonly int startCP;
        private readonly int endCP = 1;
        private readonly int outputCP = 2;
        private readonly int outputCPField;

        public DistanceBetweenCPsToCP(ParticleDefinitionParser parse) : base(parse)
        {
            distanceMin = parse.Float("m_flInputMin", distanceMin);
            distanceMax = parse.Float("m_flInputMax", distanceMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            startCP = parse.Int32("m_nStartCP", startCP);
            endCP = parse.Int32("m_nEndCP", endCP);
            outputCP = parse.Int32("m_nOutputCP", outputCP);
            outputCPField = parse.Int32("m_nOutputCPField", outputCPField);

            // Unsupported features: LOS test
        }

        public override void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            var startCP = particleSystemState.GetControlPoint(this.startCP);
            var endCP = particleSystemState.GetControlPoint(this.endCP);

            var distance = Vector3.Distance(startCP.Position, endCP.Position);

            var remappedDistance = MathUtils.Remap(distance, distanceMin, distanceMax);

            // always clamped to output min/max
            remappedDistance = MathUtils.Saturate(remappedDistance);

            var finalValue = float.Lerp(outputMin, outputMax, remappedDistance);

            particleSystemState.SetControlPointValueComponent(outputCP, outputCPField, finalValue);
        }
    }
}
