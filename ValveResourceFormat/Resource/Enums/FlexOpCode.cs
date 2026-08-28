namespace ValveResourceFormat
{
    /// <summary>
    /// Opcodes of the postfix stack machine that evaluates a flex rule.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/FlexOpCode_t">FlexOpCode_t</seealso>
    public enum FlexOpCode
    {
        /// <summary>Pushes a constant. The operand is a float bit pattern.</summary>
        Const = 1,

        /// <summary>Pushes the value of the flex controller the operand indexes.</summary>
        Fetch1 = 2,

        /// <summary>Pushes the value of the flex the operand indexes.</summary>
        Fetch2 = 3,

        /// <summary>Adds the top two stack values.</summary>
        Add = 4,

        /// <summary>Subtracts the top stack value from the one below it.</summary>
        Sub = 5,

        /// <summary>Multiplies the top two stack values.</summary>
        Mul = 6,

        /// <summary>Divides the value below the top of the stack by the top one.</summary>
        Div = 7,

        /// <summary>Negates the top stack value.</summary>
        Neg = 8,

        /// <summary>Raises the value below the top of the stack to the power of the top one.</summary>
        Exp = 9,

        /// <summary>Opening parenthesis, consumed by the expression parser.</summary>
        Open = 10,

        /// <summary>Closing parenthesis, consumed by the expression parser.</summary>
        Close = 11,

        /// <summary>Argument separator, consumed by the expression parser.</summary>
        Comma = 12,

        /// <summary>Pushes the larger of the top two stack values.</summary>
        Max = 13,

        /// <summary>Pushes the smaller of the top two stack values.</summary>
        Min = 14,

        /// <summary>Lower half of a two way blend.</summary>
        TwoWay0 = 15,

        /// <summary>Upper half of a two way blend.</summary>
        TwoWay1 = 16,

        /// <summary>Multi way blend across a controller range.</summary>
        NWay = 17,

        /// <summary>Combination of several flexes.</summary>
        Combo = 18,

        /// <summary>Suppression of one flex by another.</summary>
        Dominate = 19,

        /// <summary>Lower eyelid tracking.</summary>
        DmeLowerEyelid = 20,

        /// <summary>Upper eyelid tracking.</summary>
        DmeUpperEyelid = 21,

        /// <summary>Square root of the top stack value.</summary>
        Sqrt = 22,

        /// <summary>Remaps the top stack value between two ranges, clamped.</summary>
        RemapValClamped = 23,

        /// <summary>Sine of the top stack value.</summary>
        Sin = 24,

        /// <summary>Cosine of the top stack value.</summary>
        Cos = 25,

        /// <summary>Absolute value of the top stack value.</summary>
        Abs = 26,
    }
}
