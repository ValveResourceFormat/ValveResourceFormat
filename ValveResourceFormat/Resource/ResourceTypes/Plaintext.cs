using System.IO;
using System.Text;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Plain text resource.
    /// </summary>
    public class Plaintext : Block
    {
        /// <inheritdoc/>
        public override BlockType Type => BlockType.DATA;

        /// <summary>Gets the text data.</summary>
        public string Data { get; private set; } = string.Empty;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        public Plaintext()
        {
            //
        }

        /// <summary>
        /// Initializes a new instance with the specified data.
        /// </summary>
        public Plaintext(string data) : this()
        {
            Data = data;
        }

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = Offset;

            Data = Encoding.UTF8.GetString(reader.ReadBytes((int)Size));
        }

        /// <inheritdoc/>
        public override void Serialize(Stream stream)
        {
            stream.Write(Encoding.UTF8.GetBytes(Data));
        }

        /// <inheritdoc/>
        public override void WriteText(IndentedTextWriter writer)
        {
            writer.Write(Data);
        }
    }
}
