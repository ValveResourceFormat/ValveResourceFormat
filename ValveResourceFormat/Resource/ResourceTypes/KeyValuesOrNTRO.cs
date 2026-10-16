using System.Diagnostics.CodeAnalysis;
using System.IO;
using ValveKeyValue;
using ValveKeyValue.KeyValues3;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Block that can contain either <see cref="KVObject"/> or <see cref="NTRO"/> data.
    /// </summary>
    public class KeyValuesOrNTRO : Block
    {
        private readonly string? IntrospectionStructName;
        private readonly BlockType KVBlockType;
        /// <inheritdoc/>
        public override BlockType Type => KVBlockType;

        /// <summary>
        /// Gets or sets the parsed data as a <see cref="KVObject"/>.
        /// </summary>
        /// <remarks>Can be set to null to release KV3 data. Next probe will deserialize again.</remarks>
        [AllowNull]
        public KVObject Data
        {
            get => BackingData is BinaryKV3 kv3 ? kv3.Data : ((NTRO)BackingData).Output;
            set
            {
                if (BackingData is BinaryKV3 kv3)
                {
                    kv3.Data = value == null ? null : new KVDocument(kv3.Data.Header, kv3.Data.Name, value);
                }
                else if (value != null)
                {
                    BackingData = new BinaryKV3(value, KV3IDLookup.Get("generic"), KVBlockType) { Resource = Resource };
                }
            }
        }

        /// <summary>
        /// Gets the KV3 format id the data was serialized with, or null for NTRO-backed data.
        /// </summary>
        public KV3ID? Format => (BackingData as BinaryKV3)?.Data.Header?.Format;

        private Block BackingData = null!;

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
        /// <param name="introspectionStructName">The introspection struct name for <see cref="NTRO"/> parsing.</param>
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
        /// <remarks>
        /// Writes the data as KV3 text format, regardless of the backing data source.
        /// </remarks>
        public override void WriteText(IndentedTextWriter writer)
        {
            if (BackingData is BinaryKV3 dataKv3)
            {
                dataKv3.Data.WriteKV3Text(writer);
                return;
            }

            BackingData.WriteText(writer);
        }
    }
}
