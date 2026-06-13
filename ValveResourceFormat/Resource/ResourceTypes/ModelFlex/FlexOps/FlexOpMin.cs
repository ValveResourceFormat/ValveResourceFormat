namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that computes the minimum of two values.
    /// </summary>
    public class FlexOpMin : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpMin"/> class.
        /// </summary>
        public FlexOpMin(float data) : base(data) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Pops two values from the stack and pushes the minimum value.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            var v1 = context.Stack.Pop();
            var v2 = context.Stack.Pop();

            context.Stack.Push(Math.Min(v1, v2));
        }
    }
}
