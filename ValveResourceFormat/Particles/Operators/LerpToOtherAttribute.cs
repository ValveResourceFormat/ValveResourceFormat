namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Lerps from one particle attribute toward another on the same particle, using a per-particle
    /// interpolation factor, and writes the result to the output attribute. The lerp starts from the
    /// output attribute itself unless a separate "from" attribute is set. A vector read into a scalar
    /// output contributes its length, a scalar mixed with a vector is broadcast to all three components,
    /// and a vector output with two scalar inputs is left unchanged.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LerpToOtherAttribute">C_OP_LerpToOtherAttribute</seealso>
    class LerpToOtherAttribute : ParticleFunctionOperator
    {
        private readonly ParticleField fieldInputFrom = ParticleField.NoneDisabled;
        private readonly ParticleField fieldInput = ParticleField.Position;
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1.0f);

        public LerpToOtherAttribute(ParticleDefinitionParser parse) : base(parse)
        {
            fieldInputFrom = parse.ParticleField("m_nFieldInputFrom", fieldInputFrom);
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);

            if (fieldInputFrom == ParticleField.NoneDisabled)
            {
                fieldInputFrom = fieldOutput;
            }
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var fromIsVector = fieldInputFrom.FieldType() == "vector";
            var toIsVector = fieldInput.FieldType() == "vector";

            if (fieldOutput.FieldType() == "vector")
            {
                if (!fromIsVector && !toIsVector)
                {
                    return;
                }

                foreach (ref var particle in particles.Current)
                {
                    var interp = MathUtils.Saturate(interpolation.NextNumber(ref particle, particleSystemState) * strength);
                    var from = fromIsVector ? particle.GetVector(fieldInputFrom) : new Vector3(particle.GetScalar(fieldInputFrom));
                    var to = toIsVector ? particle.GetVector(fieldInput) : new Vector3(particle.GetScalar(fieldInput));
                    particle.SetVector(fieldOutput, Vector3.Lerp(from, to, interp));
                }

                return;
            }

            foreach (ref var particle in particles.Current)
            {
                var interp = MathUtils.Saturate(interpolation.NextNumber(ref particle, particleSystemState) * strength);
                var from = fromIsVector ? particle.GetVector(fieldInputFrom).Length() : particle.GetScalar(fieldInputFrom);
                var to = toIsVector ? particle.GetVector(fieldInput).Length() : particle.GetScalar(fieldInput);
                particle.SetScalar(fieldOutput, float.Lerp(from, to, interp));
            }
        }
    }
}
