namespace ValveResourceFormat.Renderer.Particles.PreEmissionOperators
{
    /// <summary>
    /// Rotates a control point's orientation around a configurable axis at a specified angular rate.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointRotation">C_OP_SetControlPointRotation</seealso>
    class SetControlPointRotation : ParticleFunctionPreEmissionOperator
    {
        private readonly IVectorProvider axis = new LiteralVectorProvider(new Vector3(0, 0, 1));
        private readonly int cp;
        private readonly int localCP = -1;
        private readonly INumberProvider rotationRate = new LiteralNumberProvider(180);

        public SetControlPointRotation(ParticleDefinitionParser parse) : base(parse)
        {
            axis = parse.VectorProvider("m_vecRotAxis", axis);
            rotationRate = parse.NumberProvider("m_flRotRate", rotationRate);
            cp = parse.Int32("m_nCP", cp);
            localCP = parse.Int32("m_nLocalCP", localCP);
        }

        public override void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            var axis = this.axis.NextVector(particleSystemState);

            if (axis == Vector3.Zero)
            {
                return;
            }

            axis = Vector3.Normalize(axis);

            if (localCP > -1)
            {
                axis = Vector3.Transform(axis, particleSystemState.GetControlPoint(localCP).GetRotation());
            }

            var angle = float.DegreesToRadians(rotationRate.NextNumber(particleSystemState)) * frameTime;

            // Accumulate this frame's increment onto the control point's current rotation so it spins continuously
            var controlPoint = particleSystemState.GetControlPoint(cp);
            var increment = Quaternion.CreateFromAxisAngle(axis, angle);
            var rotation = Quaternion.Normalize(controlPoint.GetRotation() * increment);

            particleSystemState.SetControlPointRotation(cp, rotation);
        }
    }
}
