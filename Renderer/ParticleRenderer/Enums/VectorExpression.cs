namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Mathematical operation types for vector expressions.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/VectorExpressionType_t">VectorExpressionType_t</seealso>
    public enum VectorExpression
    {
        /// <summary>Uninitialized expression; outputs zero.</summary>
        VECTOR_EXPRESSION_UNINITIALIZED = -1,
        /// <summary>Adds the two input vectors.</summary>
        VECTOR_EXPRESSION_ADD = 0,
        /// <summary>Subtracts input 2 from input 1.</summary>
        VECTOR_EXPRESSION_SUBTRACT = 1,
        /// <summary>Component-wise multiplies the two input vectors.</summary>
        VECTOR_EXPRESSION_MUL = 2,
        /// <summary>Component-wise divides input 1 by input 2.</summary>
        VECTOR_EXPRESSION_DIVIDE = 3,
        /// <summary>Passes through input 1 unchanged.</summary>
        VECTOR_EXPRESSION_INPUT_1 = 4,
        /// <summary>Component-wise minimum of the two input vectors.</summary>
        VECTOR_EXPRESSION_MIN = 5,
        /// <summary>Component-wise maximum of the two input vectors.</summary>
        VECTOR_EXPRESSION_MAX = 6,
        /// <summary>Cross product of the two input vectors.</summary>
        VECTOR_EXPRESSION_CROSSPRODUCT = 7,
    }
}
