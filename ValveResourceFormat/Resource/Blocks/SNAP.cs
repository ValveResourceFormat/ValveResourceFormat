using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ValveResourceFormat.Compression;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "SNAP" block.
    /// </summary>
    public class SNAP : Block
    {
        public override BlockType Type => BlockType.SNAP;

        public uint NumParticles { get; private set; }

        public IReadOnlyDictionary<string, IEnumerable> AttributeData { get; private set; }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("{0:X8}", Offset);

            writer.WriteLine($"{NumParticles} particles with {AttributeData.Count} attributes:");
            writer.WriteLine();

            foreach (var (attribute, data) in AttributeData)
            {
                writer.WriteLine($"- Attribute {attribute} -");
                foreach (var d in data)
                {
                    writer.WriteLine(d);
                }
                writer.WriteLine();
            }
        }

        public override void Read(BinaryReader resourceReader, Resource resource)
        {
            resourceReader.BaseStream.Position = Offset;

            // Decompress SNAP block compression
            using var decompressedStream = BlockCompress.Decompress(resourceReader, Size);
            using var reader = new BinaryReader(decompressedStream);

            // Get DATA block to know how to read SNAP data
            var data = resource.DataBlock.AsKeyValueCollection();

            var numParticles = data.GetIntegerProperty("num_particles");
            var attributes = data.GetArray("attributes");
            var stringList = data.GetArray<string>("string_list");

            var attributeData = new Dictionary<string, IEnumerable>();

            foreach (var attribute in attributes)
            {
                var attributeName = attribute.GetProperty<string>("name");
                var attributeType = attribute.GetProperty<string>("type");

                var attributeArray = attributeType switch
                {
                    "skinning" => ReadSkinningData(reader, numParticles, stringList),
                    "string" => ReadStringArray(reader, numParticles, stringList),
                    "bone" => ReadStringArray(reader, numParticles, stringList),
                    _ => ReadArrayOfType(reader, numParticles, attributeType),
                };

                attributeData.Add(attributeName, attributeArray);
            }

            NumParticles = (uint)numParticles;
            AttributeData = attributeData;
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
                result[i] = stringList[reader.ReadInt16()];
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
    }
}
