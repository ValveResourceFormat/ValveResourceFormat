using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.ResourceTypes
{
    public class SoundStackScript : ResourceData
    {
        public Dictionary<string, string> SoundStackScriptValue { get; private set; } // TODO: be Dictionary<string, SomeKVObject>

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

            var version = reader.ReadInt32();

            if (version != 8)
            {
                throw new NotImplementedException($"Unknown soundstack version: {version}");
            }

            SoundStackScriptValue = new Dictionary<string, string>();

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

                SoundStackScriptValue.Add(name, value);
            }
        }

        public override string ToString()
        {
            using (var writer = new IndentedTextWriter())
            {
                foreach (var entry in SoundStackScriptValue)
                {
                    writer.WriteLine($"// {entry.Key}");
                    writer.Write(entry.Value);
                    writer.WriteLine(string.Empty);
                }

                return writer.ToString();
            }
        }
    }
}
