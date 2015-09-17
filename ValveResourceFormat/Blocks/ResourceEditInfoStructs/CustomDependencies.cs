using System;
using System.IO;
using System.Text;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class CustomDependencies : REDIBlock
    {
        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset;

            if (this.Size > 0)
            {
                throw new NotImplementedException("CustomDependencies block is not handled. Please report this on https://github.com/SteamDatabase/ValveResourceFormat and provide the file that caused this exception.");
            }
        }

        public override string ToString()
        {
            return ToStringIndent("");
        }

        public override string ToStringIndent(string indent)
        {
            var str = new StringBuilder();

            str.AppendFormat("{0}Struct m_CustomDependencies[{1}] = \n", indent, 0);
            str.AppendFormat("{0}[\n", indent);

            // TODO

            str.AppendFormat("{0}]\n", indent);

            return str.ToString();
        }
    }
}
