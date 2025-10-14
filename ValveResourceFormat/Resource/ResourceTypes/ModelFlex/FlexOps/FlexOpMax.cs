namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that computes the maximum of two values.
    /// </summary>
    public class FlexOpMax : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpMax"/> class.
        /// </summary>
        public FlexOpMax(float data) : base(data) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Pops two values from the stack and pushes the maximum value.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            var v1 = context.Stack.Pop();
            var v2 = context.Stack.Pop();

            context.Stack.Push(Math.Max(v1, v2));
        }
    }
}
