namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    public class FlexOpSub : FlexOp
    {
        public FlexOpSub(float data) : base(data) { }

        public override void Run(FlexRuleContext context)
        {
            var v1 = context.Stack.Pop();
            var v2 = context.Stack.Pop();

            context.Stack.Push(v2 - v1);
        }
    }
}
