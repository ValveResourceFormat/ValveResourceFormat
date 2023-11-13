using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.ResourceTypes.ModelFlex
{
    public class FlexRuleContext
    {
        public float FetchValue { get; }
        public Stack<float> Stack { get; } = new();

        public FlexRuleContext(float value)
        {
            FetchValue = value;
        }
    }
}
