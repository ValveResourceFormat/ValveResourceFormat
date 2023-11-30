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
        private Stack<float> stack = new();

        public FlexRule(int flexID, FlexOp[] flexOps)
        {
            if (flexOps.Length == 0)
            {
                throw new ArgumentException("Flex ops array cannot be empty");
            }

            FlexID = flexID;
            FlexOps = flexOps;
        }

        public float Evaluate(float[] flexControllerValues)
        {
            var context = new FlexRuleContext(stack, flexControllerValues);

            foreach (var item in FlexOps)
            {
                item.Run(context);
            }

            if (stack.Count > 1)
            {
                throw new Exception("FlexRule stack had multiple values after evaluation");
            }
            else if (stack.Count == 0)
            {
                throw new Exception("FlexRule stack was empty after evaluation");
            }

            return context.Stack.Pop();
        }
    }
}
