namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that fetches a controller value.
    /// </summary>
    public class FlexOpFetch1 : FlexOp
    {
        /// <summary>
        /// Gets the controller ID to fetch.
        /// </summary>
        public int ControllerId { get; }
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpFetch1"/> class.
        /// </summary>
        public FlexOpFetch1(float data) : base(data)
        {
            //This is the only flexop with a non-float data so far.
            //If there's more, it might make sense to change the type of Data from float to something else
            ControllerId = (int)MathF.Round(data);
        }

        /// <inheritdoc/>
        public override void Run(in FlexRuleContext context)
        {
            context.Stack.Push(context.ControllerValues[ControllerId]);
        }
    }
}
