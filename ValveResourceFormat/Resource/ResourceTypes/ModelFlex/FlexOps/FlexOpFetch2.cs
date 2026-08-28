namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that fetches an already evaluated flex value.
    /// </summary>
    internal class FlexOpFetch2 : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpFetch2"/> class.
        /// </summary>
        public FlexOpFetch2(float data) : base(data) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Pushes the value of the flex the operand indexes.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            var flexId = (int)MathF.Round(Data);
            context.Stack.Push(context.GetFlexValue(flexId));
        }
    }
}
