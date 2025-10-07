using System.IO;
using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Block that can contain either KeyValues or NTRO data.
    /// </summary>
    public class KeyValuesOrNTRO : Block
    {
        private readonly string IntrospectionStructName;
        private readonly BlockType KVBlockType;
        /// <inheritdoc/>
        public override BlockType Type => KVBlockType;

        /// <summary>
        /// Gets the parsed data as a KVObject.
        /// </summary>
        public KVObject Data { get; private set; }

        private Block BackingData;

        /// <summary>
        /// Initializes a new instance of the <see cref="KeyValuesOrNTRO"/> class.
        /// </summary>
        public KeyValuesOrNTRO()
        {
            KVBlockType = BlockType.DATA;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="KeyValuesOrNTRO"/> class with a specific block type and introspection struct name.
        /// </summary>
        /// <param name="type">The block type.</param>
        /// <param name="introspectionStructName">The introspection struct name for NTRO parsing.</param>
        public KeyValuesOrNTRO(BlockType type, string introspectionStructName)
        {
            KVBlockType = type;
            IntrospectionStructName = introspectionStructName;
        }

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            // It is possible to have MDAT block with NTRO in a file, but it will be KV3 anyway.
            if (!Resource.ContainsBlockType(BlockType.NTRO) || KVBlockType == BlockType.MDAT)
            {
                var kv3 = new BinaryKV3(KVBlockType)
                {
                    Offset = Offset,
                    Size = Size,
                    Resource = Resource,
                };
                kv3.Read(reader);
                Data = kv3.Data;
                BackingData = kv3;
            }
            else
            {
                var ntro = new NTRO
                {
                    StructName = IntrospectionStructName,
                    Offset = Offset,
                    Size = Size,
                    Resource = Resource,
                };
                ntro.Read(reader);
                Data = ntro.Output;
                BackingData = ntro;
            }
        }

        /// <inheritdoc/>
        public override void Serialize(Stream stream)
        {
            if (BackingData is BinaryKV3 dataKv3)
            {
                dataKv3.Serialize(stream);
                return;
            }

            throw new NotImplementedException("Serializing this block is not yet supported. If you need this, send us a pull request!");
        }

        /// <inheritdoc/>
        public override void WriteText(IndentedTextWriter writer)
        {
            if (BackingData is BinaryKV3 dataKv3)
            {
                dataKv3.GetKV3File().WriteText(writer);
                return;
            }

            BackingData.WriteText(writer);
        }
    }
}
