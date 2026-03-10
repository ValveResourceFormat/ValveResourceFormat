namespace ValveResourceFormat
{
    /// <summary>
    /// Vector to float expression types used for particles.
    /// </summary>
    public enum VectorFloatExpressionType
    {
        /// <summary>Uninitialized expression.</summary>
        VECTOR_FLOAT_EXPRESSION_UNINITIALIZED = -1,
        /// <summary>Dot product of input 1 and input 2.</summary>
        VECTOR_FLOAT_EXPRESSION_DOTPRODUCT = 0,
        /// <summary>Distance between input 1 and input 2.</summary>
        VECTOR_FLOAT_EXPRESSION_DISTANCE = 1,
        /// <summary>Squared distance between input 1 and input 2.</summary>
        VECTOR_FLOAT_EXPRESSION_DISTANCESQR = 2,
        /// <summary>Length of input 1.</summary>
        VECTOR_FLOAT_EXPRESSION_INPUT1_LENGTH = 3,
        /// <summary>Squared length of input 1.</summary>
        VECTOR_FLOAT_EXPRESSION_INPUT1_LENGTHSQR = 4,
        /// <summary>Noise value derived from input 1.</summary>
        VECTOR_FLOAT_EXPRESSION_INPUT1_NOISE = 5,
    }
}
