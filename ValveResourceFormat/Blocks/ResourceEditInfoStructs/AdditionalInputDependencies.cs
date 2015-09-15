using System.Text;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class AdditionalInputDependencies : InputDependencies
    {
        public override string ToString()
        {
            var str = new StringBuilder();

            str.AppendFormat("\tStruct m_ArgumentDependencies[{0}]\n", List.Count);

            foreach (var dep in List)
            {
                str.AppendFormat(
                    "\t\tCResourceString m_ContentRelativeFilename = \"{0}\"\n" +
                    "\t\tCResourceString m_ContentSearchPath = \"{1}\"\n" +
                    "\t\tuint32 m_nFileCRC = 0x{2:X8}\n" +
                    "\t\tuint32 m_nFlags = 0x{3:X8}\n\n",
                    dep.ContentRelativeFilename, dep.ContentSearchPath, dep.FileCRC, dep.Flags
                );
            }

            return str.ToString();
        }
    }
}
