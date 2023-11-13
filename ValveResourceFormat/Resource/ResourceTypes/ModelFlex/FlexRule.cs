using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps;

namespace ValveResourceFormat.ResourceTypes.ModelFlex
{
    public class FlexRule
    {
        public int FlexID { get; }
        public FlexOp[] FlexOps { get; }

        public FlexRule(int flexID, FlexOp[] flexOps)
        {
            if (flexOps.Length == 0)
            {
                throw new ArgumentException("Flex ops array cannot be empty");
            }

            FlexID = flexID;
            FlexOps = flexOps;
        }

        public float Evaluate(float value)
        {
            var context = new FlexRuleContext(value);

            foreach (var item in FlexOps)
            {
                item.Run(context);
            }

            return context.Stack.Pop();
        }
    }
}
