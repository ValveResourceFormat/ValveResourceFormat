using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Every step, reduces two per-particle vector inputs to a number (dot product, distance, length
    /// or noise) and writes it to a float attribute by a set method, then through the output remap.
    /// </summary>
    /// <remarks>
    /// The strength scales the number before the set method rather than blending toward it. An angle
    /// output takes the number as degrees, unless the set method scales the value. An alpha output is
    /// clamped to [0, 1] after the remap.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetFloatAttributeToVectorExpression">C_OP_SetFloatAttributeToVectorExpression</seealso>
    class SetFloatAttributeToVectorExpression : ParticleFunctionOperator
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

            AddInitialReadOf(parse, setMethod, outputField);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var scale = strength * outputScale;

            var current = particles.Current;
            current.FillVectorInputs(input1, input2, particleSystemState, out var values1, out var values2);

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var outputs = current.Floats(outputField);
            var initial = particles.Initial.ReadFloats(outputField);
            var deltaTime = particleSystemState.CurrentStep;
            var isAlpha = outputField.IsAlpha();

            for (var i = 0; i < outputs.Length; i++)
            {
                if (expression.Evaluate(values1[i], values2[i]) is not { } result)
                {
                    continue;
                }

                var value = ParticleFieldExtensions.ApplySetMethod(result * scale, initial[i], outputs[i], deltaTime, setMethod);

                if (outputRemap != null)
                {
                    value = outputRemap.ApplyMapping(value);
                }

                if (isAlpha)
                {
                    value = MathUtils.Saturate(value);
                }

                outputs[i] = value;
            }
        }
    }
}
