namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    public class FlexOpConst : FlexOp
    {
        public FlexOpConst(float data) : base(data) { }

        public override void Run(in FlexRuleContext context)
        {
            context.Stack.Push(Data);
        }
    }
}
