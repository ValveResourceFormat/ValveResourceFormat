using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    public class FlexOpMax : FlexOp
    {
        public FlexOpMax(float data) : base(data) { }

        public override void Run(FlexRuleContext context)
        {
            var v1 = context.Stack.Pop();
            var v2 = context.Stack.Pop();

            context.Stack.Push(Math.Max(v1, v2));
        }
    }
}
