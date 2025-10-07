namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that subtracts two values.
    /// </summary>
    public class FlexOpSub : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpSub"/> class.
        /// </summary>
        public FlexOpSub(float data) : base(data) { }

        /// <inheritdoc/>
        public override void Run(in FlexRuleContext context)
        {
            var v1 = context.Stack.Pop();
            var v2 = context.Stack.Pop();

            context.Stack.Push(v2 - v1);
        }
    }
}
