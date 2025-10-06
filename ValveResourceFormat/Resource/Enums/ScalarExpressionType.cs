namespace ValveResourceFormat
{
    // used for particles
    public enum ScalarExpressionType
    {
#pragma warning disable CS1591
        SCALAR_EXPRESSION_UNINITIALIZED = -1,
        SCALAR_EXPRESSION_ADD = 0,
        SCALAR_EXPRESSION_SUBTRACT = 1,
        SCALAR_EXPRESSION_MUL = 2,
        SCALAR_EXPRESSION_DIVIDE = 3,
        SCALAR_EXPRESSION_INPUT_1 = 4,
        SCALAR_EXPRESSION_MIN = 5,
        SCALAR_EXPRESSION_MAX = 6,
        SCALAR_EXPRESSION_MOD = 7,
        SCALAR_EXPRESSION_EQUAL = 8,
        SCALAR_EXPRESSION_GT = 9,
        SCALAR_EXPRESSION_LT = 10,
#pragma warning restore CS1591
    }
}
