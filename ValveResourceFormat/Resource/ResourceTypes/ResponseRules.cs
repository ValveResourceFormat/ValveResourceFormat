using System.IO;
using System.Text;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a response rule include.
    /// </summary>
    public struct ResponseRuleInclude
    {
        /// <summary>
        /// Gets or sets the name of the included response rule.
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Gets or sets the first flags value.
        /// </summary>
        public int Flags1 { get; set; }
        /// <summary>
        /// Gets or sets the second flags value.
        /// </summary>
        public int Flags2 { get; set; }
    }

    /// <summary>
    /// Represents response rules resource.
    /// </summary>
    public class ResponseRules : Block
    {
        /// <inheritdoc/>
        public override BlockType Type => BlockType.DATA;

        /// <summary>
        /// Gets the first header byte read from the response rules block.
        /// </summary>
        public byte Arg1 { get; private set; }
        /// <summary>
        /// Gets the second header byte read from the response rules block.
        /// </summary>
        public byte Arg2 { get; private set; }
        /// <summary>
        /// Gets the third header byte read from the response rules block.
        /// </summary>
        public byte Arg3 { get; private set; }
        /// <summary>
        /// Gets the fourth header byte read from the response rules block.
        /// </summary>
        public byte Arg4 { get; private set; }
        /// <summary>
        /// Gets the included response rules.
        /// </summary>
        public ResponseRuleInclude[] Includes { get; private set; } = [];
        /// <summary>
        /// Gets the response rules file content.
        /// </summary>
        public string? File { get; private set; }

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = Offset;

            Arg1 = reader.ReadByte();
            Arg2 = reader.ReadByte();
            Arg3 = reader.ReadByte();
            Arg4 = reader.ReadByte();

            var includeCount = reader.ReadUInt16();
            Includes = new ResponseRuleInclude[includeCount];

            for (var i = 0; i < includeCount; i++)
            {
                var dependency = reader.ReadNullTermString(Encoding.UTF8);
                var flags1 = reader.ReadInt32();
                var flags2 = reader.ReadInt32();

                Includes[i] = new ResponseRuleInclude
                {
                    Name = dependency,
                    Flags1 = flags1,
                    Flags2 = flags2,
                };
            }

            var relativeOffsetHere = reader.BaseStream.Position - Offset;
            File = Encoding.UTF8.GetString(reader.ReadBytes((int)(Size - relativeOffsetHere)));
        }

        /// <inheritdoc/>
        public override void Serialize(Stream stream)
        {
            throw new NotImplementedException("Serializing this block is not yet supported. If you need this, send us a pull request!");
        }

        /// <inheritdoc/>
        public override void WriteText(IndentedTextWriter writer)
        {
            writer.Write(File);
        }
    }
}
