namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Turns a control point to face along another control point's movement over the last step. A
    /// move shorter than 0.001 units faces it along world +X.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetCPOrientationToDirection">C_OP_SetCPOrientationToDirection</seealso>
    class SetCPOrientationToDirection : ParticleFunctionOperator
    {
        private readonly int inputControlPoint;
        private readonly int outputControlPoint;

        public SetCPOrientationToDirection(ParticleDefinitionParser parse) : base(parse)
        {
            inputControlPoint = parse.Int32("m_nInputControlPoint", inputControlPoint);
            outputControlPoint = parse.Int32("m_nOutputControlPoint", outputControlPoint);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var forward = MathUtils.SafeNormalize(particleSystemState.GetControlPoint(inputControlPoint).StepDelta, Vector3.UnitX, 0.001f * 0.001f);
            particleSystemState.SetControlPointOrientation(outputControlPoint, forward);
        }
    }
}
