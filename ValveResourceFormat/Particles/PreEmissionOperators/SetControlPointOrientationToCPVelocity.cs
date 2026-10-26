namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Points a control point's forward axis along the direction another control point moved over
    /// the last step. A control point that did not move leaves the orientation as it was.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointOrientationToCPVelocity">C_OP_SetControlPointOrientationToCPVelocity</seealso>
    class SetControlPointOrientationToCPVelocity : ParticleFunctionPreEmissionOperator
    {
        private readonly int cpInput;
        private readonly int cpOutput;

        public SetControlPointOrientationToCPVelocity(ParticleDefinitionParser parse) : base(parse)
        {
            cpInput = parse.ControlPoint("m_nCPInput", cpInput);
            cpOutput = parse.ControlPoint("m_nCPOutput", cpOutput);
        }

        public override void Operate(ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            var delta = particleSystemState.GetControlPoint(cpInput).StepDelta;

            if (delta != Vector3.Zero)
            {
                particleSystemState.SetControlPointOrientation(cpOutput, delta);
            }
        }
    }
}
