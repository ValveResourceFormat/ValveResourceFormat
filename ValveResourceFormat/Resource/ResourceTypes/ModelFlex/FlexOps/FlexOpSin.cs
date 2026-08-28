namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that takes a sine.
    /// </summary>
    internal class FlexOpSin : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpSin"/> class.
        /// </summary>
        public FlexOpSin(float data) : base(data) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Pops one value and pushes its sine.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            context.Stack.Push(MathF.Sin(context.Pop()));
        }
    }
}
