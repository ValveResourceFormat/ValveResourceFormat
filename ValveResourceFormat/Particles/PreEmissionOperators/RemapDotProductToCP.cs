namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Remaps the dot product of two control points' forward axes into one component of a third
    /// control point. A degenerate input range is a step at <c>m_flInputMax</c>.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapDotProductToCP">C_OP_RemapDotProductToCP</seealso>
    class RemapDotProductToCP : ParticleFunctionPreEmissionOperator
    {
        private readonly int inputCP1;
        private readonly int inputCP2 = 1;
        private readonly int outputCP = 2;
        private readonly int outVectorField;
        private readonly INumberProvider inputMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider inputMax = new LiteralNumberProvider(1f);
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0f);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1f);

        public RemapDotProductToCP(ParticleDefinitionParser parse) : base(parse)
        {
            inputCP1 = parse.Int32("m_nInputCP1", inputCP1);
            inputCP2 = parse.Int32("m_nInputCP2", inputCP2);
            outputCP = parse.Int32("m_nOutputCP", outputCP);
            outVectorField = parse.Int32("m_nOutVectorField", outVectorField);
            inputMin = parse.NumberProvider("m_flInputMin", inputMin);
            inputMax = parse.NumberProvider("m_flInputMax", inputMax);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            var low = inputMin.NextNumber(particleSystemState);
            var high = inputMax.NextNumber(particleSystemState);
            var outLow = outputMin.NextNumber(particleSystemState);
            var outHigh = outputMax.NextNumber(particleSystemState);

            if (outVectorField is < 0 or > 2)
            {
                return;
            }

            var dot = Vector3.Dot(particleSystemState.GetControlPoint(inputCP1).Forward, particleSystemState.GetControlPoint(inputCP2).Forward);
            var value = MathUtils.RemapValClamped(dot, low, high, outLow, outHigh);

            particleSystemState.SetControlPointValueComponent(outputCP, outVectorField, value);
        }
    }
}
