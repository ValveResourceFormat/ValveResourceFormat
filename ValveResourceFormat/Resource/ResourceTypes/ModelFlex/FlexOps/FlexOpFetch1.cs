using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    public class FlexOpFetch1 : FlexOp
    {
        public FlexOpFetch1(float data) : base(data) { }

        public override void Run(FlexRuleContext context)
        {
            context.Stack.Push(context.FetchValue);
        }
    }
}
