namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Writes 1 into one component of a control point while another control point is under water,
    /// and 0 otherwise. Without a game nothing is under water, so it always writes 0. A field of -1
    /// writes nothing.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointFieldToWater">C_OP_SetControlPointFieldToWater</seealso>
    class SetControlPointFieldToWater : ParticleFunctionPreEmissionOperator
    {
        private readonly int destCP;
        private readonly int cpField;

        public SetControlPointFieldToWater(ParticleDefinitionParser parse) : base(parse)
        {
            destCP = parse.ControlPoint("m_nDestCP", destCP);
            cpField = Math.Clamp(parse.Int32("m_nCPField", cpField), -1, 2);
        }

        public override void Operate(ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            if (cpField < 0)
            {
                return;
            }

            particleSystemState.SetControlPointValueComponent(destCP, cpField, 0f);
        }
    }
}
