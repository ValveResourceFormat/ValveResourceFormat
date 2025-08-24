using System.IO;
using System.Text;

#nullable disable

namespace ValveResourceFormat.ResourceTypes
{
    public class Plaintext : Block
    {
        public override BlockType Type => BlockType.DATA;

        public string Data { get; private set; }

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = Offset;

            Data = Encoding.UTF8.GetString(reader.ReadBytes((int)Size));
        }

        public override void Serialize(Stream stream)
        {
            stream.Write(Encoding.UTF8.GetBytes(Data));
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.Write(Data);
        }
    }
}
