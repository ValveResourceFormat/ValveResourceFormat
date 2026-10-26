using System.Numerics.Tensors;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps how much of each particle is visible on screen into a scalar attribute, lerped toward
    /// by the operator strength. Visibility always reads as full, so every particle moves toward the
    /// remap of 1.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapVisibilityScalar">C_OP_RemapVisibilityScalar</seealso>
    class RemapVisibilityScalar : ParticleFunctionOperator
    {
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;

        public RemapVisibilityScalar(ParticleDefinitionParser parse) : base(parse)
        {
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);

            if (fieldOutput.IsAlpha())
            {
                outputMin = MathUtils.Saturate(outputMin);
                outputMax = MathUtils.Saturate(outputMax);
            }
        }

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (!ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var value = MathUtils.RemapValClamped(RemapTransformVisibilityToScalar.FullyVisible, inputMin, inputMax, outputMin, outputMax);
            var output = particles.Current.Floats(fieldOutput);

            TensorPrimitives.Multiply(output, 1f - strength, output);
            TensorPrimitives.Add(output, value * strength, output);
        }
    }
}
