using System.IO;
using ValveKeyValue;

#nullable disable

namespace ValveResourceFormat.ResourceTypes
{
    public class BinaryKV1 : Block
    {
        public const int MAGIC = 0x564B4256; // VBKV

        public override BlockType Type => BlockType.DATA;

        public KVObject KeyValues { get; private set; }

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = Offset;

            KeyValues = KVSerializer.Create(KVSerializationFormat.KeyValues1Binary).Deserialize(reader.BaseStream);
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            using var ms = new MemoryStream();
            using var reader = new StreamReader(ms);

            KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(ms, KeyValues);

            ms.Seek(0, SeekOrigin.Begin);

            writer.Write(reader.ReadToEnd());
        }
    }
}
