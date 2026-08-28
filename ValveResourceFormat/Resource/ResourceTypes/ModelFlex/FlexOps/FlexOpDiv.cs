namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that divides two values.
    /// </summary>
    internal class FlexOpDiv : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpDiv"/> class.
        /// </summary>
        public FlexOpDiv(float data) : base(data) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Pops two values from the stack and pushes their quotient.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            var v1 = context.Pop();
            var v2 = context.Pop();

            // The engine yields zero rather than an infinity for a divisor at or below its epsilon.
            context.Stack.Push(MathF.Abs(v1) <= 0.0001f ? 0f : v2 / v1);
        }
    }
}
