using System.IO;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.ResourceTypes
{
    public class ResourceManifest : ResourceData
    {
        public List<List<string>> Resources { get; private set; } = [];

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = Offset;

            if (Resource.ContainsBlockType(BlockType.NTRO))
            {
                var ntro = new NTRO
                {
                    StructName = "ResourceManifest_t",
                    Offset = Offset,
                    Size = Size,
                    Resource = Resource,
                };
                ntro.Read(reader);

                Resources =
                [
                    new(ntro.Output.GetArray<string>("m_ResourceFileNameList")),
                ];

                return;
            }

            if (Size < 8)
            {
                throw new UnexpectedMagicException("Unknown size", Size, nameof(Size));
            }

            var version = reader.ReadInt32();

            if (version != 8)
            {
                if (version == 0 && reader.ReadInt32() == 0)
                {
                    return;
                }

                throw new UnexpectedMagicException("Unknown version", version, nameof(version));
            }

            var blockCount = reader.ReadInt32();

            for (var block = 0; block < blockCount; block++)
            {
                var originalOffset = reader.BaseStream.Position;
                var offset = reader.ReadInt32();
                var count = reader.ReadInt32();
                var strings = new List<string>(count);

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

        public override void WriteText(IndentedTextWriter writer)
        {
            GetPrintabaleObject().WriteText(writer);
        }

        private KV3File GetPrintabaleObject()
        {
            var root = new KVObject(null);
            var index = 0;

            foreach (var resource in Resources)
            {
                var arr = new KVObject(null, isArray: true);

                foreach (var file in resource)
                {
                    arr.AddItem(file);
                }

                var key = index > 0 ? $"resourceManifest{index}" : "resourceManifest";
                root.AddProperty(key, arr);
                index++;
            }

            return new KV3File(root);
        }
    }
}
