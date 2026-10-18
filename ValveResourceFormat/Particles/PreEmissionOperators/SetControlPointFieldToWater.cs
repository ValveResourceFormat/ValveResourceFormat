namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Writes 1 into one component of a control point while another control point is under water,
    /// and 0 otherwise. Without a game, water is everything below z = 0. A field of -1 writes nothing.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointFieldToWater">C_OP_SetControlPointFieldToWater</seealso>
    class SetControlPointFieldToWater : ParticleFunctionPreEmissionOperator
    {
        private readonly int sourceCP;
        private readonly int destCP;
        private readonly int cpField;

        public SetControlPointFieldToWater(ParticleDefinitionParser parse) : base(parse)
        {
            sourceCP = parse.ControlPoint("m_nSourceCP", sourceCP);
            destCP = parse.ControlPoint("m_nDestCP", destCP);
            cpField = Math.Clamp(parse.Int32("m_nCPField", cpField), -1, 2);
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            if (cpField < 0)
            {
                return;
            }

            var underWater = particleSystemState.GetControlPoint(sourceCP).Position.Z < 0f;
            particleSystemState.SetControlPointValueComponent(destCP, cpField, underWater ? 1f : 0f);
        }
    }
}
