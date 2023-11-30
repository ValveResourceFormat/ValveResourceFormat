using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.ResourceTypes.ModelFlex
{
    public struct FlexRuleContext
    {
        public float[] ControllerValues { get; }
        public Stack<float> Stack { get; }

        public FlexRuleContext(Stack<float> stack, float[] controllerValues)
        {
            ControllerValues = controllerValues;
            Stack = stack;
        }
    }
}
