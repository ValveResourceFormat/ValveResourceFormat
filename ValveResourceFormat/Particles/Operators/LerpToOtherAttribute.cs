using System.Numerics.Tensors;

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

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;
            var fromIsVector = ParticleBuffer.IsVector(fieldInputFrom);
            var toIsVector = ParticleBuffer.IsVector(fieldInput);
            var fromVectors = fromIsVector ? current.ReadVectors(fieldInputFrom) : default;
            var toVectors = toIsVector ? current.ReadVectors(fieldInput) : default;
            var fromScalars = fromIsVector ? default : current.ReadFloats(fieldInputFrom);
            var toScalars = toIsVector ? default : current.ReadFloats(fieldInput);

            if (ParticleBuffer.IsVector(fieldOutput))
            {
                if (!fromIsVector && !toIsVector)
                {
                    return;
                }

                var vectorOutput = current.Vectors(fieldOutput);
                var vectorInterpolations = interpolation.Fill(current, particleSystemState);

                for (var i = 0; i < vectorOutput.Length; i++)
                {
                    var interp = MathUtils.Saturate(vectorInterpolations[i] * strength);
                    var from = fromIsVector ? fromVectors[i] : new Vector3(fromScalars[i]);
                    var to = toIsVector ? toVectors[i] : new Vector3(toScalars[i]);
                    vectorOutput[i] = Vector3.Lerp(from, to, interp);
                }

                return;
            }

            if (!ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var scalarOutput = current.Floats(fieldOutput);
            var interpolations = interpolation.Fill(current, particleSystemState);

            if (!fromIsVector && !toIsVector)
            {
                TensorPrimitives.Multiply(interpolations, strength, interpolations);
                TensorPrimitives.Clamp(interpolations, 0f, 1f, interpolations);
                TensorPrimitives.Lerp(fromScalars, toScalars, interpolations, scalarOutput);
                return;
            }

            for (var i = 0; i < scalarOutput.Length; i++)
            {
                var interp = MathUtils.Saturate(interpolations[i] * strength);
                var from = fromIsVector ? fromVectors[i].Length() : fromScalars[i];
                var to = toIsVector ? toVectors[i].Length() : toScalars[i];
                scalarOutput[i] = MathUtils.Lerp(from, to, interp);
            }
        }
    }
}
