namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that takes a cosine.
    /// </summary>
    internal class FlexOpCos : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpCos"/> class.
        /// </summary>
        public FlexOpCos(float data) : base(data) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Pops one value and pushes its cosine.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            context.Stack.Push(MathF.Cos(context.Pop()));
        }
    }
}
