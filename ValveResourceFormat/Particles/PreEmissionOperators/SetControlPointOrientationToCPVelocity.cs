using ValveResourceFormat.Particles.Utils;

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
            cpInput = Math.Clamp(parse.Int32("m_nCPInput", cpInput), 0, ParticleSystemState.MaxControlPoint);
            cpOutput = Math.Clamp(parse.Int32("m_nCPOutput", cpOutput), 0, ParticleSystemState.MaxControlPoint);
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            var delta = particleSystemState.GetControlPoint(cpInput).StepDelta;

            if (delta == Vector3.Zero)
            {
                return;
            }

            var forward = Vector3.Normalize(delta);
            var (left, up) = ParticleMath.VectorVectors(forward);

            particleSystemState.SetControlPointOrientation(cpOutput, forward, left, up);
        }
    }
}
