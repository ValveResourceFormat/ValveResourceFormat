using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Reduces two per-particle vector inputs to a number (dot product, distance, length or noise)
    /// and writes it to a float attribute by a set method, then through the output remap.
    /// </summary>
    /// <remarks>
    /// An angle output takes the number as degrees, unless the set method scales the value. An alpha
    /// output is clamped to [0, 1] before the remap rather than after it.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_SetFloatAttributeToVectorExpression">C_INIT_SetFloatAttributeToVectorExpression</seealso>
    class SetFloatAttributeToVectorExpression : ParticleFunctionInitializer
    {
        private readonly VectorFloatExpressionType expression = VectorFloatExpressionType.VECTOR_FLOAT_EXPRESSION_DOTPRODUCT;
        private readonly IVectorProvider input1 = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider input2 = new LiteralVectorProvider(Vector3.Zero);
        private readonly AttributeMapping? outputRemap;
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly float outputScale = 1f;

        public SetFloatAttributeToVectorExpression(ParticleDefinitionParser parse) : base(parse)
        {
            expression = parse.Enum("m_nExpression", expression);
            input1 = parse.VectorProvider("m_vInput1", input1);
            input2 = parse.VectorProvider("m_vInput2", input2);
            outputRemap = AttributeMapping.FromRemapInput(parse, "m_flOutputRemap");
            outputField = parse.ParticleField("m_nOutputField", outputField);
            setMethod = parse.Enum("m_nSetMethod", setMethod);
            outputScale = ParticleExpressions.AngleOutputScale(outputField, setMethod);
        }

        public override ulong WrittenFields => outputField.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            run.FillVectorInputs(input1, input2, particleSystemState, out var values1, out var values2);

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var outputs = run.Floats(outputField);
            var deltaTime = particleSystemState.CurrentStep;
            var clampsToUnit = outputField.IsAlpha();

            for (var i = 0; i < outputs.Length; i++)
            {
                if (expression.Evaluate(values1[i], values2[i]) is not { } result)
                {
                    continue;
                }

                var current = outputs[i];
                var value = ParticleFieldExtensions.ApplySetMethod(result * outputScale, current, current, deltaTime, setMethod);

                if (clampsToUnit)
                {
                    value = MathUtils.Saturate(value);
                }

                if (outputRemap != null)
                {
                    value = outputRemap.ApplyMapping(value);
                }

                outputs[i] = value;
            }
        }
    }
}
