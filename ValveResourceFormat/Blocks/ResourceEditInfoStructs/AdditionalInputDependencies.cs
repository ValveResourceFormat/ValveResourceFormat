using System.Text;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class AdditionalInputDependencies : InputDependencies
    {
        public override string ToString()
        {
            return ToStringIndent("");
        }

        public override string ToStringIndent(string indent)
        {
            var str = new StringBuilder();

            str.AppendFormat("{0}Struct m_AdditionalInputDependencies[{1}] = \n", indent, List.Count);
            str.AppendFormat("{0}[\n", indent);

            foreach (var dep in List)
            {
                str.AppendFormat("{0}\tResourceInputDependency_t\n", indent);
                str.AppendFormat("{0}\t{{\n", indent);
                str.AppendFormat("{0}\t\tCResourceString m_ContentRelativeFilename = \"{1}\"\n", indent, dep.ContentRelativeFilename);
                str.AppendFormat("{0}\t\tCResourceString m_ContentSearchPath = \"{1}\"\n", indent, dep.ContentSearchPath);
                str.AppendFormat("{0}\t\tuint32 m_nFileCRC = 0x{1:X8}\n", indent, dep.FileCRC);
                str.AppendFormat("{0}\t\tuint32 m_nFlags = 0x{1:X8}\n", indent, dep.Flags);
                str.AppendFormat("{0}\t}}\n", indent);
            }

            str.AppendFormat("{0}]\n", indent);

            return str.ToString();
        }
    }
}
