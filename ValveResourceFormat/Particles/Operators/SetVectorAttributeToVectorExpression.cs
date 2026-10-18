using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Every step, combines two per-particle vector inputs with a vector expression and writes the
    /// result to a vector attribute by a set method. A colour or alpha output is clamped to [0, 1],
    /// and the result can be normalized.
    /// </summary>
    /// <remarks>
    /// The strength scales the expression result before the set method rather than blending toward
    /// it, so a weak operator replacing the value pulls it toward zero. The lerp expression clamps
    /// <c>m_flLerp</c> to [0, 1].
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetVectorAttributeToVectorExpression">C_OP_SetVectorAttributeToVectorExpression</seealso>
    class SetVectorAttributeToVectorExpression : ParticleFunctionOperator
    {
        private readonly VectorExpressionType expression = VectorExpressionType.VECTOR_EXPRESSION_ADD;
        private readonly IVectorProvider input1 = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider input2 = new LiteralVectorProvider(Vector3.Zero);
        private readonly INumberProvider lerp = new LiteralNumberProvider(0f);
        private readonly ParticleField outputField = ParticleField.Normal;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool normalizedOutput;

        public SetVectorAttributeToVectorExpression(ParticleDefinitionParser parse) : base(parse)
        {
            expression = parse.Enum("m_nExpression", expression);
            input1 = parse.VectorProvider("m_vInput1", input1);
            input2 = parse.VectorProvider("m_vInput2", input2);
            lerp = parse.NumberProvider("m_flLerp", lerp);
            outputField = parse.ParticleField("m_nOutputField", outputField);
            setMethod = parse.Enum("m_nSetMethod", setMethod);
            normalizedOutput = parse.Boolean("m_bNormalizedOutput", normalizedOutput);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                var value1 = input1.NextVector(ref particle, particleSystemState);
                var value2 = input2.NextVector(ref particle, particleSystemState);
                var lerpAmount = lerp.NextNumber(ref particle, particleSystemState);

                if (expression.Evaluate(value1, value2, lerpAmount, clampLerp: true) is not { } result)
                {
                    continue;
                }

                var value = particle.ModifyVectorBySetMethod(particles, outputField, result * strength, setMethod);

                if (outputField.IsNormalizedField())
                {
                    value = Vector3.Clamp(value, Vector3.Zero, Vector3.One);
                }

                if (normalizedOutput)
                {
                    value = ParticleMath.NormalizeWithEpsilon(value);
                }

                particle.SetVector(outputField, value);
            }
        }
    }
}
