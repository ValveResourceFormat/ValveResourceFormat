using System;
using System.IO;
using System.Text;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class ExtraStringData : REDIBlock
    {
        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset;


        }

        public override string ToString()
        {
            return ToStringIndent("");
        }

        public override string ToStringIndent(string indent)
        {
            var str = new StringBuilder();

            str.AppendFormat("{0}Struct m_ExtraStringData[{1}] = \n", indent, 0);
            str.AppendFormat("{0}[\n", indent);

            // TODO

            str.AppendFormat("{0}]\n", indent);

            return str.ToString();
        }
    }
}
