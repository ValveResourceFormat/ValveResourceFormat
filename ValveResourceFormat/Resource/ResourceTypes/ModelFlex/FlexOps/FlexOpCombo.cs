namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that multiplies several values together.
    /// </summary>
    internal class FlexOpCombo : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpCombo"/> class.
        /// </summary>
        public FlexOpCombo(float data) : base(data) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Pops as many values as the operand names and pushes their product.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            var count = (int)MathF.Round(Data);
            var product = 1f;

            for (var i = 0; i < count; i++)
            {
                product *= context.Pop();
            }

            context.Stack.Push(product);
        }
    }
}
