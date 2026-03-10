namespace ValveResourceFormat
{
    /// <summary>
    /// Scalar expression types used for particles.
    /// </summary>
    public enum ScalarExpressionType
    {
        /// <summary>Uninitialized expression; outputs zero.</summary>
        SCALAR_EXPRESSION_UNINITIALIZED = -1,
        /// <summary>Adds input 1 and input 2.</summary>
        SCALAR_EXPRESSION_ADD = 0,
        /// <summary>Subtracts input 2 from input 1.</summary>
        SCALAR_EXPRESSION_SUBTRACT = 1,
        /// <summary>Multiplies input 1 by input 2.</summary>
        SCALAR_EXPRESSION_MUL = 2,
        /// <summary>Divides input 1 by input 2.</summary>
        SCALAR_EXPRESSION_DIVIDE = 3,
        /// <summary>Passes through input 1 unchanged.</summary>
        SCALAR_EXPRESSION_INPUT_1 = 4,
        /// <summary>Returns the minimum of input 1 and input 2.</summary>
        SCALAR_EXPRESSION_MIN = 5,
        /// <summary>Returns the maximum of input 1 and input 2.</summary>
        SCALAR_EXPRESSION_MAX = 6,
        /// <summary>Returns the remainder of input 1 divided by input 2.</summary>
        SCALAR_EXPRESSION_MOD = 7,
        /// <summary>Returns 1 if input 1 equals input 2, otherwise 0.</summary>
        SCALAR_EXPRESSION_EQUAL = 8,
        /// <summary>Returns 1 if input 1 is greater than input 2, otherwise 0.</summary>
        SCALAR_EXPRESSION_GT = 9,
        /// <summary>Returns 1 if input 1 is less than input 2, otherwise 0.</summary>
        SCALAR_EXPRESSION_LT = 10,
    }
}
