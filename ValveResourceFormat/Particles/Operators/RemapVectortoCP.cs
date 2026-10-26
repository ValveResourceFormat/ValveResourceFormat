namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Copies a vector attribute of one particle into a control point's position, leaving its
    /// orientation alone.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapVectortoCP">C_OP_RemapVectortoCP</seealso>
    class RemapVectortoCP : ParticleFunctionOperator
    {
        private readonly int outControlPoint = -1;
        private readonly ParticleField fieldInput = ParticleField.Position;
        private readonly int particleNumber;

        public RemapVectortoCP(ParticleDefinitionParser parse) : base(parse)
        {
            outControlPoint = parse.Int32("m_nOutControlPointNumber", outControlPoint);
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            particleNumber = Math.Max(0, parse.Int32("m_nParticleNumber", particleNumber));
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (outControlPoint < 0 || particleNumber >= particles.Count)
            {
                return;
            }

            particleSystemState.SetControlPointValue(outControlPoint, particles.Current.ReadVector(fieldInput, particleNumber));
        }
    }
}
