namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Remaps a float of the game's sound operator stacks, named by <c>m_StackName</c>,
    /// <c>m_OperatorName</c> and <c>m_FieldName</c>, into one component of a control point. Without a
    /// game every sound value reads as 0, so the control point receives the remapped 0.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_DriveCPFromGlobalSoundFloat">C_OP_DriveCPFromGlobalSoundFloat</seealso>
    class DriveCPFromGlobalSoundFloat : ParticleFunctionPreEmissionOperator
    {
        private readonly int outputControlPoint;
        private readonly int outputField;
        private readonly float output;

        public DriveCPFromGlobalSoundFloat(ParticleDefinitionParser parse) : base(parse)
        {
            outputControlPoint = parse.Int32("m_nOutputControlPoint", outputControlPoint);
            outputField = parse.Int32("m_nOutputField", outputField);

            output = MathUtils.RemapValClamped(0f,
                parse.Float("m_flInputMin", 0f),
                parse.Float("m_flInputMax", 1f),
                parse.Float("m_flOutputMin", 0f),
                parse.Float("m_flOutputMax", 1f));
        }

        public override void Operate(ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            if (outputField is < 0 or > 2)
            {
                return;
            }

            particleSystemState.SetControlPointValueComponent(outputControlPoint, outputField, output);
        }
    }
}
