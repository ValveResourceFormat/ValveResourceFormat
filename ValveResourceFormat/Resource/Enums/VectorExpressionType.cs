namespace ValveResourceFormat
{
    /// <summary>
    /// Vector expression types used for particles.
    /// </summary>
    public enum VectorExpressionType
    {
#pragma warning disable CS1591
        VECTOR_EXPRESSION_UNINITIALIZED = -1,
        VECTOR_EXPRESSION_ADD = 0,
        VECTOR_EXPRESSION_SUBTRACT = 1,
        VECTOR_EXPRESSION_MUL = 2,
        VECTOR_EXPRESSION_DIVIDE = 3,
        VECTOR_EXPRESSION_INPUT_1 = 4,
        VECTOR_EXPRESSION_MIN = 5,
        VECTOR_EXPRESSION_MAX = 6,
        VECTOR_EXPRESSION_CROSSPRODUCT = 7,
        VECTOR_EXPRESSION_LERP = 8,
#pragma warning restore CS1591
    }
}
