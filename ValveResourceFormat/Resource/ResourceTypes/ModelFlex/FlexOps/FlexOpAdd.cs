namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that adds two values.
    /// </summary>
    public class FlexOpAdd : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpAdd"/> class.
        /// </summary>
        public FlexOpAdd(float data) : base(data) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Pops two values from the stack and pushes their sum.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            var v1 = context.Stack.Pop();
            var v2 = context.Stack.Pop();

            context.Stack.Push(v2 + v1);
        }
    }
}
