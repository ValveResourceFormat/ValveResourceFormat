namespace ValveResourceFormat
{
    /// <summary>
    /// Vector to float expression types used for particles.
    /// </summary>
    public enum VectorFloatExpressionType
    {
#pragma warning disable CS1591
        VECTOR_FLOAT_EXPRESSION_UNINITIALIZED = -1,
        VECTOR_FLOAT_EXPRESSION_DOTPRODUCT = 0,
        VECTOR_FLOAT_EXPRESSION_DISTANCE = 1,
        VECTOR_FLOAT_EXPRESSION_DISTANCESQR = 2,
        VECTOR_FLOAT_EXPRESSION_INPUT1_LENGTH = 3,
        VECTOR_FLOAT_EXPRESSION_INPUT1_LENGTHSQR = 4,
        VECTOR_FLOAT_EXPRESSION_INPUT1_NOISE = 5,
#pragma warning restore CS1591
    }
}
