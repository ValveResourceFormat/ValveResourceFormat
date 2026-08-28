namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that negates a value.
    /// </summary>
    internal class FlexOpNeg : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpNeg"/> class.
        /// </summary>
        public FlexOpNeg(float data) : base(data) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Pops one value and pushes its negation.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            context.Stack.Push(-context.Pop());
        }
    }
}
