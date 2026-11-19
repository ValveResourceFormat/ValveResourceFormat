namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Writes a render attribute of the entity on one control point into one component of another
    /// control point's position, keeping the other components.
    /// </summary>
    /// <remarks>
    /// The game resolves the attribute named by <c>m_pszAttributeName</c> on the entity of <c>m_nCPInput</c>.
    /// There are no entity render attributes here, so the default value is always written.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/dota2/particles/C_OP_SetControlPointFromRenderAttribute">C_OP_SetControlPointFromRenderAttribute</seealso>
    class SetControlPointFromRenderAttribute : ParticleFunctionPreEmissionOperator
    {
        private readonly int outputControlPoint = 1;
        private readonly int component;
        private readonly float defaultValue;

        public SetControlPointFromRenderAttribute(ParticleDefinitionParser parse) : base(parse)
        {
            outputControlPoint = parse.Int32("m_nCPOutput", outputControlPoint);
            component = parse.Int32("m_nComponent", component);
            defaultValue = parse.Float("m_flDefaultValue", defaultValue);
        }

        public override void Operate(ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            particleSystemState.GetControlPoint(outputControlPoint).SetComponent(component, defaultValue);
        }
    }
}
