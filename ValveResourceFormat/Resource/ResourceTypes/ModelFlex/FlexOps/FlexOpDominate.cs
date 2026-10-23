namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that suppresses a value by the product of several others.
    /// </summary>
    internal class FlexOpDominate : FlexOp
    {
        private readonly int Count;

        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpDominate"/> class.
        /// </summary>
        public FlexOpDominate(float data) : base(data)
        {
            Count = (int)MathF.Round(data);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Pops as many dominators as the operand names and scales the value below them by one minus their product.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            var dominators = 1f;

            for (var i = 0; i < Count; i++)
            {
                dominators *= context.Pop();
            }

            context.Stack.Push(context.Pop() * (1f - dominators));
        }
    }
}
