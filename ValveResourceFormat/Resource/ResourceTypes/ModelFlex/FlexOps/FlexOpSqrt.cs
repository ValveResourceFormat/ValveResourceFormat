namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that takes a square root.
    /// </summary>
    internal class FlexOpSqrt : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpSqrt"/> class.
        /// </summary>
        public FlexOpSqrt(float data) : base(data) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Pops one value and pushes its square root. A negative input yields NaN, matching the engine.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            context.Stack.Push(MathF.Sqrt(context.Pop()));
        }
    }
}
