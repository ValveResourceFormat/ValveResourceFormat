namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Only marks <c>m_ControlPoint</c> as used by the system, so tools ask for it. It does nothing
    /// while the system runs.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ForceControlPointStub">C_OP_ForceControlPointStub</seealso>
    class ForceControlPointStub : ParticleFunctionPreEmissionOperator
    {
        public ForceControlPointStub(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
        }
    }
}
