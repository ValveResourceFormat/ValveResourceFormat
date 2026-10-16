namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Remaps one position component of a control point, or with <c>m_bDerivative</c> its rate of
    /// change per second, into one component of another control point. A degenerate input range is a
    /// step at <c>m_flInputMax</c>.
    /// </summary>
    /// <remarks>
    /// An <c>m_flInterpRate</c> other than 1 blends toward the remapped value by that fraction every
    /// step, starting from the output control point's component at <c>m_nInputField</c>, not at
    /// <c>m_nOutputField</c>. Fields outside 0 to 2 write nothing.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapCPtoCP">C_OP_RemapCPtoCP</seealso>
    class RemapCPtoCP : ParticleFunctionPreEmissionOperator
    {
        private readonly int inputControlPoint;
        private readonly int outputControlPoint;
        private readonly int inputField;
        private readonly int outputField;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly bool derivative;
        private readonly float interpRate = 1f;

        public RemapCPtoCP(ParticleDefinitionParser parse) : base(parse)
        {
            inputControlPoint = parse.Int32("m_nInputControlPoint", inputControlPoint);
            outputControlPoint = parse.Int32("m_nOutputControlPoint", outputControlPoint);
            inputField = parse.Int32("m_nInputField", inputField);
            outputField = parse.Int32("m_nOutputField", outputField);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            derivative = parse.Boolean("m_bDerivative", derivative);
            interpRate = parse.Float("m_flInterpRate", interpRate);
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            if (inputField is < 0 or > 2 || outputField is < 0 or > 2)
            {
                return;
            }

            var input = particleSystemState.GetControlPoint(inputControlPoint);
            var value = input.Position.GetComponent(inputField);

            if (derivative)
            {
                if (frameTime <= 0f)
                {
                    return;
                }

                value = (value - input.PositionPrevious.GetComponent(inputField)) / frameTime;
            }

            value = MathUtils.RemapValClamped(value, inputMin, inputMax, outputMin, outputMax);

            if (interpRate != 1f)
            {
                var previous = particleSystemState.GetControlPoint(outputControlPoint).Position.GetComponent(inputField);
                value = (MathUtils.Saturate(interpRate) * (value - previous)) + previous;
            }

            particleSystemState.SetControlPointValueComponent(outputControlPoint, outputField, value);
        }
    }
}
