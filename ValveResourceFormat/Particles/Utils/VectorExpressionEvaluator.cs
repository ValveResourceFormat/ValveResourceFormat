using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace ValveResourceFormat.Particles.Utils
{
    static class VectorExpressionEvaluator
    {
        public static Vector3 Evaluate(VectorExpression expression, Vector3 input1, Vector3 input2, float lerp, bool operatorPath)
            => expression switch
            {
                VectorExpression.VECTOR_EXPRESSION_UNINITIALIZED => Vector3.Zero,
                VectorExpression.VECTOR_EXPRESSION_ADD => input1 + input2,
                VectorExpression.VECTOR_EXPRESSION_SUBTRACT => input1 - input2,
                VectorExpression.VECTOR_EXPRESSION_MUL => input1 * input2,
                VectorExpression.VECTOR_EXPRESSION_DIVIDE => operatorPath ? DivideOperator(input1, input2) : input1 / input2,
                VectorExpression.VECTOR_EXPRESSION_INPUT_1 => input1,
                VectorExpression.VECTOR_EXPRESSION_MIN => Vector3.Min(input1, input2),
                VectorExpression.VECTOR_EXPRESSION_MAX => Vector3.Max(input1, input2),
                VectorExpression.VECTOR_EXPRESSION_CROSSPRODUCT => Vector3.Cross(input1, input2),
                VectorExpression.VECTOR_EXPRESSION_LERP => Vector3.Lerp(input1, input2, operatorPath ? MathUtils.Saturate(lerp) : lerp),
                _ => throw new NotImplementedException($"Unrecognized vector expression type ({expression})")
            };

        private static Vector3 DivideOperator(Vector3 numerator, Vector3 divisor) => new(
            DivideOperatorComponent(numerator.X, divisor.X),
            DivideOperatorComponent(numerator.Y, divisor.Y),
            DivideOperatorComponent(numerator.Z, divisor.Z));

        private static float DivideOperatorComponent(float numerator, float divisor)
        {
            var bits = BitConverter.SingleToUInt32Bits(divisor);
            if ((bits & 0x7FFFFFFFu) < 0x00800000u)
            {
                bits |= 0x34000000u;
                divisor = BitConverter.UInt32BitsToSingle(bits);
            }

            if (!Sse.IsSupported)
            {
                return numerator / divisor;
            }

            var recip = Sse.Reciprocal(Vector128.CreateScalarUnsafe(divisor)).ToScalar();
            recip = (recip + recip) - (recip * recip) * divisor;
            return recip * numerator;
        }
    }
}
