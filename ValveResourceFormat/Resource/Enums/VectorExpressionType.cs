namespace ValveResourceFormat
{
    /// <summary>
    /// Vector expression types used for particles.
    /// </summary>
    public enum VectorExpressionType
    {
        /// <summary>Uninitialized expression.</summary>
        VECTOR_EXPRESSION_UNINITIALIZED = -1,
        /// <summary>Adds input 1 and input 2.</summary>
        VECTOR_EXPRESSION_ADD = 0,
        /// <summary>Subtracts input 2 from input 1.</summary>
        VECTOR_EXPRESSION_SUBTRACT = 1,
        /// <summary>Multiplies input 1 by input 2.</summary>
        VECTOR_EXPRESSION_MUL = 2,
        /// <summary>Divides input 1 by input 2.</summary>
        VECTOR_EXPRESSION_DIVIDE = 3,
        /// <summary>Passes through input 1 unchanged.</summary>
        VECTOR_EXPRESSION_INPUT_1 = 4,
        /// <summary>Returns the component-wise minimum of input 1 and input 2.</summary>
        VECTOR_EXPRESSION_MIN = 5,
        /// <summary>Returns the component-wise maximum of input 1 and input 2.</summary>
        VECTOR_EXPRESSION_MAX = 6,
        /// <summary>Returns the cross product of input 1 and input 2.</summary>
        VECTOR_EXPRESSION_CROSSPRODUCT = 7,
        /// <summary>Linearly interpolates between input 1 and input 2.</summary>
        VECTOR_EXPRESSION_LERP = 8,
    }
}
