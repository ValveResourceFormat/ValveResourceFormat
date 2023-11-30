using System;

namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    public class FlexOpMin : FlexOp
    {
        public FlexOpMin(float data) : base(data) { }

        public override void Run(FlexRuleContext context)
        {
            var v1 = context.Stack.Pop();
            var v2 = context.Stack.Pop();

            context.Stack.Push(Math.Min(v1, v2));
        }
    }
}
