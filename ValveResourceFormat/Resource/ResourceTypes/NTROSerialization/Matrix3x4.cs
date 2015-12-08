using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.ResourceTypes.NTROSerialization
{
    class Matrix3x4
    {
        public float field0;
        public float field1;
        public float field2;
        public float field3;
        public float field4;
        public float field5;
        public float field6;
        public float field7;
        public float field8;
        public float field9;
        public float field10;
        public float field11;
        public Matrix3x4(
            float field0, float field1, float field2, float field3,
            float field4, float field5, float field6, float field7,
            float field8, float field9, float field10, float field11)
        {
            this.field0 = field0;
            this.field1 = field1;
            this.field2 = field2;
            this.field3 = field3;
            this.field4 = field4;
            this.field5 = field5;
            this.field6 = field6;
            this.field7 = field7;
            this.field8 = field8;
            this.field9 = field9;
            this.field10 = field10;
            this.field11 = field11;
        }
        public override string ToString()
        {
            using (var output = new StringWriter())
            using (var Writer = new IndentedTextWriter(output, "\t"))
            {
                WriteText(Writer);
                return output.ToString();
            }
        }
        public void WriteText(IndentedTextWriter Writer)
        {
                Writer.WriteLine();
                Writer.WriteLine("{0:F4} {1:F4} {2:F4} {3:F4}", field0, field1, field2, field3);
                Writer.WriteLine("{0:F4} {1:F4} {2:F4} {3:F4}", field4, field5, field6, field7);
                Writer.WriteLine("{0:F4} {1:F4} {2:F4} {3:F4}", field8, field9, field10, field11);
        }
    }
}
