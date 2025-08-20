using System.Buffers;
using System.Collections;
using System.IO;
using System.Linq;
using ValveResourceFormat.Compression;
using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "SNAP" block.
    /// </summary>
    public class ParticleSnapshot : Block
    {
        public override BlockType Type => BlockType.SNAP;

        public uint NumParticles { get; private set; }

        public IReadOnlyDictionary<(string Name, string Type), IEnumerable> AttributeData { get; private set; }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("{0:X8}", Offset);

            writer.WriteLine($"{NumParticles} particles with {AttributeData.Count} attributes:");
            writer.WriteLine();

            foreach (var (attribute, data) in AttributeData)
            {
                writer.WriteLine($"- Attribute {attribute.Name} ({attribute.Type}) -");
                foreach (var d in data)
                {
                    writer.WriteLine(d);
                }
                writer.WriteLine();
            }
        }

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = Offset;

            // Decompress SNAP block compression
            var info = BlockCompress.GetDecompressedSize(reader);
            var decompressed = ArrayPool<byte>.Shared.Rent(info.Size);

            try
            {
                BlockCompress.FastDecompress(info, reader, decompressed.AsSpan(0, info.Size));
                using var decompressedStream = new MemoryStream(decompressed);
                using var innerReader = new BinaryReader(decompressedStream);

                // Get DATA block to know how to read SNAP data
                var data = Resource.DataBlock.AsKeyValueCollection();

                var numParticles = data.GetIntegerProperty("num_particles");
                var attributes = data.GetArray("attributes");
                var stringList = data.GetArray<string>("string_list");

                var attributeData = new Dictionary<(string Name, string Type), IEnumerable>(attributes.Length);

                foreach (var attribute in attributes)
                {
                    var attributeName = attribute.GetProperty<string>("name");
                    var attributeType = attribute.GetProperty<string>("type");

                    var attributeArray = attributeType switch
                    {
                        "skinning" => ReadSkinningData(innerReader, numParticles, stringList),
                        "string" => ReadStringArray(innerReader, numParticles, stringList),
                        "bone" => ReadStringArray(innerReader, numParticles, stringList),
                        _ => ReadArrayOfType(innerReader, numParticles, attributeType),
                    };

                    attributeData.Add((attributeName, attributeType), attributeArray);
                }

                NumParticles = (uint)numParticles;
                AttributeData = attributeData;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(decompressed);
            }
        }

        private static IEnumerable ReadArrayOfType(BinaryReader reader, long count, string type)
        {
            if (type == "float3" || type == "vector")
            {
                var array = new Vector3[count];
                for (var i = 0; i < count; i++)
                {
                    array[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }
                return array;
            }
            else if (type == "float")
            {
                var array = new float[count];
                for (var i = 0; i < count; i++)
                {
                    array[i] = reader.ReadSingle();
                }
                return array;
            }
            else if (type == "int")
            {
                var array = new int[count];
                for (var i = 0; i < count; i++)
                {
                    array[i] = reader.ReadInt32();
                }
                return array;
            }

            throw new UnexpectedMagicException("Unsupported SNAP array type.", type, nameof(type));
        }

        private static string[] ReadStringArray(BinaryReader reader, long count, string[] stringList)
        {
            var result = new string[count];

            for (var i = 0; i < count; i++)
            {
                var index = reader.ReadInt32();
                if (stringList != null && index < stringList.Length)
                {
                    result[i] = stringList[index];
                }
                else
                {
                    result[i] = $"{index} <INVALID: string not in DATA string list>";
                }
            }

            return result;
        }

        public class SkinningData
        {
            public string[] JointNames { get; set; }
            public float[] Weights { get; set; }

            public override string ToString()
                => string.Join(' ', Enumerable.Range(0, 4)
                    .Select(i => $"({JointNames[i]}: {Weights[i]})"));
        }

        private static SkinningData[] ReadSkinningData(BinaryReader reader, long count, string[] stringList)
        {
            var result = new SkinningData[count];

            for (var i = 0; i < count; i++)
            {
                // Read 4 joints
                var joints = new string[4];
                for (var j = 0; j < 4; j++)
                {
                    joints[j] = stringList[reader.ReadInt16()];
                }

                // Read 4 weights
                var weights = new float[4];
                for (var j = 0; j < 4; j++)
                {
                    weights[j] = reader.ReadSingle();
                }

                result[i] = new SkinningData
                {
                    JointNames = joints,
                    Weights = weights
                };
            }

            return result;
        }

        public override void Serialize(Stream stream)
        {
            throw new NotImplementedException("Serializing this block is not yet supported. If you need this, send us a pull request!");
        }
    }
}
