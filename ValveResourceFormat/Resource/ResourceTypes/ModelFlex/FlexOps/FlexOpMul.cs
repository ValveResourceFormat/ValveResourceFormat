namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that multiplies two values.
    /// </summary>
    public class FlexOpMul : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpMul"/> class.
        /// </summary>
        public FlexOpMul(float data) : base(data) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Pops two values from the stack and pushes their product.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            var v1 = context.Stack.Pop();
            var v2 = context.Stack.Pop();

            context.Stack.Push(v2 * v1);
        }
    }
}
