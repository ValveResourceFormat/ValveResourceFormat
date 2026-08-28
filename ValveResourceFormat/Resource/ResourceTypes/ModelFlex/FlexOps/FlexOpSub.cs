namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that subtracts two values.
    /// </summary>
    internal class FlexOpSub : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpSub"/> class.
        /// </summary>
        public FlexOpSub(float data) : base(data) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Pops two values from the stack and pushes their difference.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            var v1 = context.Pop();
            var v2 = context.Pop();

            context.Stack.Push(v2 - v1);
        }
    }
}
