using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes
{
    public class ResourceManifest : ResourceData
    {
        public List<List<string>> Resources { get; private set; }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

            if (resource.ContainsBlockType(BlockType.NTRO))
            {
                var ntro = new NTRO
                {
                    StructName = "ResourceManifest_t",
                    Offset = Offset,
                    Size = Size,
                };
                ntro.Read(reader, resource);

                Resources = new List<List<string>>
                {
                    new List<string>(ntro.Output.GetArray<string>("m_ResourceFileNameList")),
                };

                return;
            }

            var version = reader.ReadInt32();

            if (version != 8)
            {
                throw new UnexpectedMagicException("Unknown version", version, nameof(version));
            }

            Resources = new List<List<string>>();

            var blockCount = reader.ReadInt32();

            for (var block = 0; block < blockCount; block++)
            {
                var strings = new List<string>();
                var originalOffset = reader.BaseStream.Position;
                var offset = reader.ReadInt32();
                var count = reader.ReadInt32();

                reader.BaseStream.Position = originalOffset + offset;

                for (var i = 0; i < count; i++)
                {
                    var returnOffset = reader.BaseStream.Position;
                    var stringOffset = reader.ReadInt32();
                    reader.BaseStream.Position = returnOffset + stringOffset;

                    var value = reader.ReadNullTermString(Encoding.UTF8);
                    strings.Add(value);

                    reader.BaseStream.Position = returnOffset + 4;
                }

                reader.BaseStream.Position = originalOffset + 8;

                Resources.Add(strings);
            }
        }

        public override string ToString()
        {
            using var writer = new IndentedTextWriter();
            foreach (var block in Resources)
            {
                foreach (var entry in block)
                {
                    writer.WriteLine(entry);
                }

                writer.WriteLine();
            }

            return writer.ToString();
        }
    }
}
