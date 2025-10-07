namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that divides two values.
    /// </summary>
    public class FlexOpDiv : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpDiv"/> class.
        /// </summary>
        public FlexOpDiv(float data) : base(data) { }

        /// <inheritdoc/>
        public override void Run(in FlexRuleContext context)
        {
            var v1 = context.Stack.Pop();
            var v2 = context.Stack.Pop();

            context.Stack.Push(v2 / v1);
        }
    }
}
