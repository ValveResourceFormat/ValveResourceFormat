using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Evaluates a scalar expression (add, subtract, multiply, divide, min, max, mod, input passthrough,
    /// or comparisons such as equal/greater-than/less-than) on two per-particle float inputs and writes
    /// the result, mapped through the output remap, to a scalar particle attribute.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetAttributeToScalarExpression">C_OP_SetAttributeToScalarExpression</seealso>
    class SetAttributeToScalarExpression : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly INumberProvider input1 = new LiteralNumberProvider(0f);
        private readonly INumberProvider input2 = new LiteralNumberProvider(0);
        private readonly ScalarExpressionType expression = ScalarExpressionType.SCALAR_EXPRESSION_ADD;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly AttributeMapping? outputRemap;

        public SetAttributeToScalarExpression(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nOutputField", outputField);
            input1 = parse.NumberProvider("m_flInput1", input1);
            input2 = parse.NumberProvider("m_flInput2", input2);
            expression = parse.Enum<ScalarExpressionType>("m_nExpression", expression);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            outputRemap = AttributeMapping.FromRemapInput(parse, "m_flOutputRemap");

            AddInitialReadOf(parse, setMethod, outputField);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var (min, max) = outputField.SetFloatRange();
            var current = particles.Current;

            var values1 = input1.Fill(current, particleSystemState);
            var values2 = input2.Fill(current, particleSystemState);

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var outputs = current.Floats(outputField);
            var initial = particles.Initial.ReadFloats(outputField);
            var deltaTime = particleSystemState.CurrentStep;
            var scalesByStrength = !expression.IsComparison();

            for (var i = 0; i < outputs.Length; i++)
            {
                if (expression.Evaluate(values1[i], values2[i], truncatedRemainder: true) is not { } output)
                {
                    continue;
                }

                // Strength scales arithmetic results before the set method; comparisons stay 0/1
                if (scalesByStrength)
                {
                    output *= strength;
                }

                if (outputRemap != null)
                {
                    output = outputRemap.ApplyMapping(output);
                }

                var result = ParticleFieldExtensions.ApplySetMethod(output, initial[i], outputs[i], deltaTime, setMethod);
                outputs[i] = Math.Clamp(result, min, max);
            }
        }
    }
}
