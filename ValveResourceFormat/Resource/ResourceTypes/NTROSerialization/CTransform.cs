using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.ResourceTypes.NTROSerialization
{
    class CTransform
    {
        public float field0;
        public float field1;
        public float field2;
        public float field3;
        public float field4;
        public float field5;
        public float field6;
        public float field7;

        public CTransform(float field0, float field1, float field2, float field3, float field4, float field5, float field6, float field7)
        {
            this.field0 = field0;
            this.field1 = field1;
            this.field2 = field2;
            this.field3 = field3;
            this.field4 = field4;
            this.field5 = field5;
            this.field6 = field6;
            this.field7 = field7;
        }

        public override string ToString()
        {
            // http://stackoverflow.com/a/15085178/2200891
            return String.Format("q={{{0:F}, {1:F}, {2:F}; w={3}}} p={{{4:F}, {5:F}, {6}}}", field4, field5, field6, field7.ToString("F"), field0, field1, field2.ToString("F"));
        }
    }
}
