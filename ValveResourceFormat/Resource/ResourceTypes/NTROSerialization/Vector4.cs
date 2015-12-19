using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.ResourceTypes.NTROSerialization
{
    class Vector4
    {
        public float field0;
        public float field1;
        public float field2;
        public float field3;

        public Vector4(float field0, float field1, float field2, float field3)
        {
            this.field0 = field0;
            this.field1 = field1;
            this.field2 = field2;
            this.field3 = field3;
        }

        //Due to DataType needing to be known to do ToString() here, it is done elsewhere
    }
}
