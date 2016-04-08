using System.CodeDom.Compiler;
using System.IO;

namespace ValveResourceFormat.ResourceTypes.NTROSerialization
{
    public class Matrix3x4
    {
        public float field0 { get; }
        public float field1 { get; }
        public float field2 { get; }
        public float field3 { get; }
        public float field4 { get; }
        public float field5 { get; }
        public float field6 { get; }
        public float field7 { get; }
        public float field8 { get; }
        public float field9 { get; }
        public float field10 { get; }
        public float field11 { get; }

        public Matrix3x4(
            float field0,
            float field1,
            float field2,
            float field3,
            float field4,
            float field5,
            float field6,
            float field7,
            float field8,
            float field9,
            float field10,
            float field11)
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
            using (var writer = new IndentedTextWriter(output, "\t"))
            {
                WriteText(writer);

                return output.ToString();
            }
        }

        public void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine();
            writer.WriteLine("{0:F4} {1:F4} {2:F4} {3:F4}", field0, field1, field2, field3);
            writer.WriteLine("{0:F4} {1:F4} {2:F4} {3:F4}", field4, field5, field6, field7);
            writer.WriteLine("{0:F4} {1:F4} {2:F4} {3:F4}", field8, field9, field10, field11);
        }
    }
}
