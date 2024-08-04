namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    public class FlexOpFetch1 : FlexOp
    {
        public int ControllerId { get; }
        public FlexOpFetch1(float data) : base(data)
        {
            //This is the only flexop with a non-float data so far.
            //If there's more, it might make sense to change the type of Data from float to something else
            ControllerId = (int)MathF.Round(data);
        }

        public override void Run(in FlexRuleContext context)
        {
            context.Stack.Push(context.ControllerValues[ControllerId]);
        }
    }
}
