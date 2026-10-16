namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation with no implementation, so it leaves the stack untouched.
    /// </summary>
    internal class FlexOpNop : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpNop"/> class.
        /// </summary>
        public FlexOpNop(float data) : base(data) { }

        /// <inheritdoc/>
        public override void Run(in FlexRuleContext context)
        {
        }
    }
}
