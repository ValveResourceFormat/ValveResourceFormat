using System;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.PreEmissionOperators
{
    class SetControlPointToVectorExpression : IParticlePreEmissionOperator
    {
        private readonly int cp = 1;
        private readonly IVectorProvider input1 = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider input2 = new LiteralVectorProvider(Vector3.Zero);
        private readonly string expression = "VECTOR_EXPRESSION_ADD";

        public SetControlPointToVectorExpression(ParticleDefinitionParser parse)
        {
            cp = parse.Int32("m_nOutputCP", cp);

            input1 = parse.VectorProvider("m_vInput1", input1);

            input2 = parse.VectorProvider("m_vInput2", input2);

            if (parse.Data.ContainsKey("m_nExpression"))
            {
                expression = parse.Data.GetProperty<string>("m_nExpression");
            }
        }

        public void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            var vec1 = input1.NextVector(particleSystemState);
            var vec2 = input2.NextVector(particleSystemState);

            var output = expression switch
            {
                "VECTOR_EXPRESSION_UNINITIALIZED"
                    => Vector3.Zero,
                "VECTOR_EXPRESSION_ADD"
                    => vec1 + vec2,
                "VECTOR_EXPRESSION_SUBTRACT"
                    => vec1 - vec2,
                "VECTOR_EXPRESSION_MUL"
                    => vec1 * vec2,
                "VECTOR_EXPRESSION_DIVIDE"
                    => vec1 / vec2,
                "VECTOR_EXPRESSION_INPUT_1"
                    => vec1,
                "VECTOR_EXPRESSION_MIN"
                    => Vector3.Min(vec1, vec2),
                "VECTOR_EXPRESSION_MAX"
                    => Vector3.Max(vec1, vec2),
                "VECTOR_EXPRESSION_NORMALIZE_INPUT_1" // Not in latest dota version
                    => Vector3.Normalize(vec1),
                "VECTOR_EXPRESSION_CROSSPRODUCT" // Only in latest dota version, along with VECTOR_FLOAT_EXPRESSION types
                    => Vector3.Cross(vec1, vec2),
                _ => throw new NotImplementedException($"Unrecognized vector expression type ({expression})")
            };

            particleSystemState.SetControlPointValue(cp, output);
        }
    }
}
