using System.IO;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class CustomDependencies : REDIBlock
    {
        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

            if (Size > 0)
            {
                throw new NotImplementedException("CustomDependencies block is not handled. Please report this on https://github.com/ValveResourceFormat/ValveResourceFormat and provide the file that caused this exception.");
            }
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("Struct m_CustomDependencies[{0}] =", 0);
            writer.WriteLine("[");
            writer.Indent++;

            // TODO

            writer.Indent--;
            writer.WriteLine("]");
        }
    }
}
