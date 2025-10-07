using System.IO;
using ValveKeyValue;

#nullable disable

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a binary KeyValues1 data block.
    /// </summary>
    public class BinaryKV1 : Block
    {
        /// <summary>
        /// The magic number for binary KeyValues1 format (VBKV).
        /// </summary>
        public const int MAGIC = 0x564B4256; // VBKV

        /// <inheritdoc/>
        public override BlockType Type => BlockType.DATA;

        /// <summary>
        /// Gets the deserialized KeyValues data.
        /// </summary>
        public KVObject KeyValues { get; private set; }

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = Offset;

            KeyValues = KVSerializer.Create(KVSerializationFormat.KeyValues1Binary).Deserialize(reader.BaseStream);
        }

        /// <inheritdoc/>
        public override void Serialize(Stream stream)
        {
            throw new NotImplementedException("Serializing this block is not yet supported. If you need this, send us a pull request!");
        }

        /// <inheritdoc/>
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
