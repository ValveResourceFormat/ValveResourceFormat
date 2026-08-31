using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that reads the negative half of a controller.
    /// </summary>
    internal class FlexOpTwoWay0 : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpTwoWay0"/> class.
        /// </summary>
        public FlexOpTwoWay0(float data) : base(data) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Pushes the controller ramped from one down to zero across its negative half.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            var value = context.GetControllerValue((int)MathF.Round(Data));
            context.Stack.Push(MathUtils.RemapValClamped(value, -1f, 0f, 1f, 0f));
        }
    }
}
