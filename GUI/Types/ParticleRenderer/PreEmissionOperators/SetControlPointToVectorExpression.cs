using System;
using System.Numerics;

namespace GUI.Types.ParticleRenderer.PreEmissionOperators
{
    class SetControlPointToVectorExpression : ParticleFunctionPreEmissionOperator
    {
        private readonly int OutputCP = 1;
        private readonly IVectorProvider Input1 = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider Input2 = new LiteralVectorProvider(Vector3.Zero);
        private readonly VectorExpression Expression = VectorExpression.Add;

        public SetControlPointToVectorExpression(ParticleDefinitionParser parse) : base(parse)
        {
            OutputCP = parse.Int32("m_nOutputCP", OutputCP);
            Input1 = parse.VectorProvider("m_vInput1", Input1);
            Input2 = parse.VectorProvider("m_vInput2", Input2);
            Expression = parse.EnumNormalized<VectorExpression>("m_nExpression", Expression);
        }

        public override void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            var vec1 = Input1.NextVector(particleSystemState);
            var vec2 = Input2.NextVector(particleSystemState);

            var output = Expression switch
            {
                VectorExpression.Uninitialized => Vector3.Zero,
                VectorExpression.Add => vec1 + vec2,
                VectorExpression.Substract => vec1 - vec2,
                VectorExpression.Mul => vec1 * vec2,
                VectorExpression.Divide => vec1 / vec2,
                VectorExpression.Input_1 => vec1,
                VectorExpression.Min => Vector3.Min(vec1, vec2),
                VectorExpression.Max => Vector3.Max(vec1, vec2),
                VectorExpression.Crossproduct => Vector3.Cross(vec1, vec2),
                // "VECTOR_EXPRESSION_NORMALIZE_INPUT_1" // Not in latest dota version
                _ => throw new NotImplementedException($"Unrecognized vector expression type ({Expression})")
            };

            particleSystemState.SetControlPointValue(OutputCP, output);
        }
    }
}
