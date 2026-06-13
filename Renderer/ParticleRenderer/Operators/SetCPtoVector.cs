namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Copies the position of a control point into a vector particle attribute each frame.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetCPtoVector">C_OP_SetCPtoVector</seealso>
    class SetCPtoVector : ParticleFunctionOperator
    {
        private readonly int inputCpNumber;
        private readonly ParticleField FieldOutput = ParticleField.Position;

        public SetCPtoVector(ParticleDefinitionParser parse) : base(parse)
        {
            inputCpNumber = parse.Int32("m_nCPInput", inputCpNumber);
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            var value = particleSystemState.GetControlPoint(inputCpNumber).Position;

            foreach (ref var particle in particles.Current)
            {
                particle.SetVector(FieldOutput, value);
            }
        }
    }
}
