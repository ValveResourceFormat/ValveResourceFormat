using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that remaps a value between two ranges.
    /// </summary>
    internal class FlexOpRemapValClamped : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpRemapValClamped"/> class.
        /// </summary>
        public FlexOpRemapValClamped(float data) : base(data) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Pops a value and the two ranges to map it between, and pushes the clamped result.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            var d = context.Pop();
            var c = context.Pop();
            var b = context.Pop();
            var a = context.Pop();
            var value = context.Pop();

            context.Stack.Push(MathUtils.RemapValClamped(value, a, b, c, d));
        }
    }
}
