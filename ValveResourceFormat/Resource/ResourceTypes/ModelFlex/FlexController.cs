using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.ResourceTypes.ModelFlex
{
    public class FlexController
    {
        public string Name { get; private set; }
        public float Min { get; private set; }
        public float Max { get; private set; }

        public FlexController(string name, float min, float max)
        {
            Name = name;
            Min = min;
            Max = max;
        }
    }
}