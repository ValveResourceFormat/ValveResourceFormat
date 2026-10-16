namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Writes the scale of the object one control point is bound to, 1 when it is bound to none,
    /// into every component of another control point's position.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointFromObjectScale">C_OP_SetControlPointFromObjectScale</seealso>
    class SetControlPointFromObjectScale : ParticleFunctionPreEmissionOperator
    {
        private readonly int inputControlPoint;
        private readonly int outputControlPoint = 1;

        public SetControlPointFromObjectScale(ParticleDefinitionParser parse) : base(parse)
        {
            inputControlPoint = parse.Int32("m_nCPInput", inputControlPoint);
            outputControlPoint = parse.Int32("m_nCPOutput", outputControlPoint);
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            var scale = particleSystemState.GetControlPoint(inputControlPoint).ResolveModelObject()?.Transform.MaxAxisScale() ?? 1f;
            particleSystemState.SetControlPointValue(outputControlPoint, new Vector3(scale));
        }
    }
}
