namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that pushes a constant value.
    /// </summary>
    public class FlexOpConst : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpConst"/> class.
        /// </summary>
        public FlexOpConst(float data) : base(data) { }

        /// <inheritdoc/>
        public override void Run(in FlexRuleContext context)
        {
            context.Stack.Push(Data);
        }
    }
}
