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
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var scale = strength * outputScale;

            foreach (var particle in particles.Current)
            {
                var value1 = input1.NextVector(particle, particleSystemState);
                var value2 = input2.NextVector(particle, particleSystemState);

                if (expression.Evaluate(value1, value2) is not { } result)
                {
                    continue;
                }

                var value = particle.ModifyScalarBySetMethod(particles, outputField, result * scale, setMethod);

                if (outputRemap != null)
                {
                    value = outputRemap.ApplyMapping(value);
                }

                if (outputField.IsAlpha())
                {
                    value = MathUtils.Saturate(value);
                }

                particle.SetScalar(outputField, value);
            }
        }
    }
}
