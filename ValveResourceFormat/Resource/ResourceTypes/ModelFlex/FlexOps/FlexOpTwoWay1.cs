using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that reads the positive half of a controller.
    /// </summary>
    internal class FlexOpTwoWay1 : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpTwoWay1"/> class.
        /// </summary>
        public FlexOpTwoWay1(float data) : base(data) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Pushes the controller ramped from zero up to one across its positive half.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            var value = context.GetControllerValue((int)MathF.Round(Data));
            context.Stack.Push(MathUtils.RemapValClamped(value, 0f, 1f, 0f, 1f));
        }
    }
}
