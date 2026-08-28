namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that takes an absolute value.
    /// </summary>
    internal class FlexOpAbs : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpAbs"/> class.
        /// </summary>
        public FlexOpAbs(float data) : base(data) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Pops one value and pushes its magnitude.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            context.Stack.Push(MathF.Abs(context.Pop()));
        }
    }
}
