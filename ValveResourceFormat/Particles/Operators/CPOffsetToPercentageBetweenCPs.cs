using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Moves an output control point away from an offset control point along an offset vector,
    /// scaled by how far an input control point lies between a start and an end control point.
    /// Only the output position is written; its orientation is left alone.
    /// </summary>
    /// <remarks>
    /// With the radial check the fraction is the input's distance from the start over the start to
    /// end distance, so the input can lie on any side of the start. Otherwise it is the input's
    /// unclamped projection onto the start to end line. The remapped fraction goes through a bias
    /// curve. Operator strength is not read.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_CPOffsetToPercentageBetweenCPs">C_OP_CPOffsetToPercentageBetweenCPs</seealso>
    class CPOffsetToPercentageBetweenCPs : ParticleFunctionOperator
    {
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly float inputBias = 0.5f;
        private readonly int startCP;
        private readonly int endCP = 1;
        private readonly int offsetCP = 2;
        private readonly int outputCP = 4;
        private readonly int inputCP = 3;
        private readonly bool radialCheck = true;
        private readonly bool scaleOffset;
        private readonly Vector3 offset;

        public CPOffsetToPercentageBetweenCPs(ParticleDefinitionParser parse) : base(parse)
        {
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            inputBias = parse.Float("m_flInputBias", inputBias);
            startCP = parse.ControlPoint("m_nStartCP", startCP);
            endCP = parse.ControlPoint("m_nEndCP", endCP);
            offsetCP = parse.ControlPoint("m_nOffsetCP", offsetCP);
            outputCP = parse.ControlPoint("m_nOuputCP", outputCP);
            inputCP = parse.ControlPoint("m_nInputCP", inputCP);
            radialCheck = parse.Boolean("m_bRadialCheck", radialCheck);
            scaleOffset = parse.Boolean("m_bScaleOffset", scaleOffset);
            offset = parse.Vector3("m_vecOffset", offset);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var start = particleSystemState.GetControlPoint(startCP).Position;
            var end = particleSystemState.GetControlPoint(endCP).Position;
            var offsetOrigin = particleSystemState.GetControlPoint(offsetCP).Position;
            var input = particleSystemState.GetControlPoint(inputCP).Position;

            var length = Vector3.Distance(start, end);
            var scaledOffset = scaleOffset ? offset * length : offset;

            float percentage;

            if (radialCheck)
            {
                percentage = (Vector3.Distance(start, input) + ParticleMath.FloatEpsilon) / length;
            }
            else
            {
                var axis = end - start;
                var lengthSquared = axis.LengthSquared();
                percentage = lengthSquared < 1e-5f ? 0f : Vector3.Dot(input - start, axis) / lengthSquared;
            }

            var fraction = ParticleMath.BiasClamped(MathUtils.RemapValClamped(percentage, inputMin, inputMax, 0f, 1f), inputBias);

            particleSystemState.SetControlPointValue(outputCP, (scaledOffset * fraction) + offsetOrigin);
        }
    }
}
