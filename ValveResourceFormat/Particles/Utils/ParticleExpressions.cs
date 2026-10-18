namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// The binary expressions the expression operators and initializers combine their two inputs
    /// with.
    /// </summary>
    static class ParticleExpressions
    {
        /// <summary>
        /// Combines <paramref name="a"/> and <paramref name="b"/>, or returns null for an expression
        /// outside the known set. Division and remainder by zero give 0 and <paramref name="a"/>.
        /// </summary>
        public static float? Evaluate(this ScalarExpressionType expression, float a, float b) => expression switch
        {
            ScalarExpressionType.SCALAR_EXPRESSION_ADD => a + b,
            ScalarExpressionType.SCALAR_EXPRESSION_SUBTRACT => a - b,
            ScalarExpressionType.SCALAR_EXPRESSION_MUL => a * b,
            ScalarExpressionType.SCALAR_EXPRESSION_DIVIDE => b == 0f ? 0f : a / b,
            ScalarExpressionType.SCALAR_EXPRESSION_INPUT_1 => a,
            ScalarExpressionType.SCALAR_EXPRESSION_MIN => MathF.Min(a, b),
            ScalarExpressionType.SCALAR_EXPRESSION_MAX => MathF.Max(a, b),
            ScalarExpressionType.SCALAR_EXPRESSION_MOD => b == 0f ? a : a % b,
            ScalarExpressionType.SCALAR_EXPRESSION_EQUAL => a == b ? 1f : 0f,
            ScalarExpressionType.SCALAR_EXPRESSION_GT => a > b ? 1f : 0f,
            ScalarExpressionType.SCALAR_EXPRESSION_LT => a < b ? 1f : 0f,
            _ => null,
        };

        /// <summary>Whether the expression is a comparison, whose 0 or 1 result is never scaled.</summary>
        public static bool IsComparison(this ScalarExpressionType expression)
            => expression is ScalarExpressionType.SCALAR_EXPRESSION_EQUAL
                or ScalarExpressionType.SCALAR_EXPRESSION_GT
                or ScalarExpressionType.SCALAR_EXPRESSION_LT;

        /// <summary>
        /// Combines <paramref name="a"/> and <paramref name="b"/> per component, or returns null for an
        /// expression outside the known set. A divisor component of zero divides by
        /// <see cref="ParticleMath.FloatEpsilon"/> with its sign.
        /// </summary>
        /// <param name="expression">The expression.</param>
        /// <param name="a">The first input.</param>
        /// <param name="b">The second input.</param>
        /// <param name="lerp">How far the lerp expression moves from <paramref name="a"/> to <paramref name="b"/>.</param>
        /// <param name="clampLerp">Whether <paramref name="lerp"/> is clamped to [0, 1] first.</param>
        public static Vector3? Evaluate(this VectorExpressionType expression, Vector3 a, Vector3 b, float lerp, bool clampLerp) => expression switch
        {
            VectorExpressionType.VECTOR_EXPRESSION_ADD => a + b,
            VectorExpressionType.VECTOR_EXPRESSION_SUBTRACT => a - b,
            VectorExpressionType.VECTOR_EXPRESSION_MUL => a * b,
            VectorExpressionType.VECTOR_EXPRESSION_DIVIDE => a / new Vector3(NonZero(b.X), NonZero(b.Y), NonZero(b.Z)),
            VectorExpressionType.VECTOR_EXPRESSION_INPUT_1 => a,
            VectorExpressionType.VECTOR_EXPRESSION_MIN => Vector3.Min(a, b),
            VectorExpressionType.VECTOR_EXPRESSION_MAX => Vector3.Max(a, b),
            VectorExpressionType.VECTOR_EXPRESSION_CROSSPRODUCT => Vector3.Cross(a, b),
            VectorExpressionType.VECTOR_EXPRESSION_LERP => MathUtils.Lerp(a, b, clampLerp ? MathUtils.Saturate(lerp) : lerp),
            _ => null,
        };

        /// <summary>
        /// Reduces <paramref name="a"/> and <paramref name="b"/> to one number, or returns null for an
        /// expression outside the known set.
        /// </summary>
        public static float? Evaluate(this VectorFloatExpressionType expression, Vector3 a, Vector3 b) => expression switch
        {
            VectorFloatExpressionType.VECTOR_FLOAT_EXPRESSION_DOTPRODUCT => Vector3.Dot(a, b),
            VectorFloatExpressionType.VECTOR_FLOAT_EXPRESSION_DISTANCE => Vector3.Distance(a, b),
            VectorFloatExpressionType.VECTOR_FLOAT_EXPRESSION_DISTANCESQR => Vector3.DistanceSquared(a, b),
            VectorFloatExpressionType.VECTOR_FLOAT_EXPRESSION_INPUT1_LENGTH => a.Length(),
            VectorFloatExpressionType.VECTOR_FLOAT_EXPRESSION_INPUT1_LENGTHSQR => a.LengthSquared(),
            VectorFloatExpressionType.VECTOR_FLOAT_EXPRESSION_INPUT1_NOISE => Noise.Value3D(a),
            _ => null,
        };

        /// <summary>
        /// The factor a vector expression's number is scaled by before it is written to
        /// <paramref name="outputField"/>: degrees to radians for an angle output whose set method does
        /// not scale the value, else 1.
        /// </summary>
        public static float AngleOutputScale(ParticleField outputField, ParticleSetMethod setMethod)
            => outputField.IsAngleField() && setMethod is not (ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE or ParticleSetMethod.PARTICLE_SET_SCALE_CURRENT_VALUE)
                ? float.DegreesToRadians(1f)
                : 1f;

        private static float NonZero(float value)
            => MathF.Abs(value) < ParticleMath.FloatMin ? MathF.CopySign(ParticleMath.FloatEpsilon, value) : value;
    }
}
