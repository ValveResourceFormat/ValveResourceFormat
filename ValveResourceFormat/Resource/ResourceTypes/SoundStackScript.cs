using System.IO;
using System.Text;

#nullable disable

namespace ValveResourceFormat.ResourceTypes
{
    public class SoundStackScript : Block
    {
        public override BlockType Type => BlockType.DATA;

        public Dictionary<string, string> SoundStackScriptValue { get; private set; } // TODO: be Dictionary<string, SomeKVObject>

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = Offset;

            var version = reader.ReadInt32();

            if (version != 8)
            {
                throw new UnexpectedMagicException("Unknown version", version, nameof(version));
            }

            SoundStackScriptValue = [];

            var count = reader.ReadInt32();
            var offset = reader.BaseStream.Position;

            for (var i = 0; i < count; i++)
            {
                var offsetToName = offset + reader.ReadInt32();
                offset += 4;
                var offsetToValue = offset + reader.ReadInt32();
                offset += 4;

                reader.BaseStream.Position = offsetToName;
                var name = reader.ReadNullTermString(Encoding.UTF8);

                reader.BaseStream.Position = offsetToValue;
                var value = reader.ReadNullTermString(Encoding.UTF8);

                reader.BaseStream.Position = offset;

                // Valve have duplicates, assume last is correct?
                SoundStackScriptValue.Remove(name);

                SoundStackScriptValue.Add(name, value);
            }
        }

        public override void Serialize(Stream stream)
        {
            throw new NotImplementedException("Serializing this block is not yet supported. If you need this, send us a pull request!");
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            foreach (var entry in SoundStackScriptValue)
            {
                writer.WriteLine($"// {entry.Key}");
                writer.Write(entry.Value);
                writer.WriteLine(string.Empty);
            }
        }
    }
}
