namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Copies one component of a vector attribute into a scalar attribute, clamped to [0, 1] for a
    /// normalized output such as alpha.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapVectorComponentToScalar">C_OP_RemapVectorComponentToScalar</seealso>
    class RemapVectorComponentToScalar : ParticleFunctionOperator
    {
        private readonly ParticleField fieldInput = ParticleField.Position;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly int component;

        public RemapVectorComponentToScalar(ParticleDefinitionParser parse) : base(parse)
        {
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            component = parse.Int32("m_nComponent", component);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var clamp = fieldOutput.IsNormalizedField();

            foreach (var particle in particles.Current)
            {
                var value = particle.GetVectorComponent(fieldInput, component);
                particle.SetScalar(fieldOutput, clamp ? MathUtils.Saturate(value) : value);
            }
        }
    }
}
