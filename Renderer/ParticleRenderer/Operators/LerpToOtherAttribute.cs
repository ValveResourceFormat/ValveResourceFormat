namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Lerps a particle attribute toward the value of another attribute on the same particle,
    /// using a per-particle interpolation factor. The input and output fields must be of the same type.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LerpToOtherAttribute">C_OP_LerpToOtherAttribute</seealso>
    class LerpToOtherAttribute : ParticleFunctionOperator
    {
        private readonly ParticleField fieldInput = ParticleField.Color;
        private readonly ParticleField fieldOutput = ParticleField.Color;
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1.0f);

        private readonly bool skip;
        public LerpToOtherAttribute(ParticleDefinitionParser parse) : base(parse)
        {
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);

            // If the two fields are different types, the operator does nothing.
            skip = fieldInput.FieldType() != fieldOutput.FieldType();
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            // We don't have to do weird stuff with this one because it doesn't have the option to set the initial.
            if (!skip)
            {
                if (fieldInput.FieldType() == "vector")
                {
                    foreach (ref var particle in particles.Current)
                    {
                        var interp = MathUtils.Saturate(interpolation.NextNumber(ref particle, particleSystemState) * strength);
                        var blend = Vector3.Lerp(particle.GetVector(fieldOutput), particle.GetVector(fieldInput), interp);
                        particle.SetVector(fieldOutput, blend);
                    }
                }
                else if (fieldInput.FieldType() == "float")
                {
                    foreach (ref var particle in particles.Current)
                    {
                        var interp = MathUtils.Saturate(interpolation.NextNumber(ref particle, particleSystemState) * strength);
                        var blend = float.Lerp(particle.GetScalar(fieldOutput), particle.GetScalar(fieldInput), interp);
                        particle.SetScalar(fieldOutput, blend);
                    }
                }
            }
        }
    }
}
