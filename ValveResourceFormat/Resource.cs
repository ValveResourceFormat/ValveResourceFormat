using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ValveResourceFormat
{
    /// <summary>
    /// Represents a Valve resource.
    /// </summary>
    public class Resource
    {
        private BinaryReader Reader;

        /// <summary>
        /// Resource name.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Resource size.
        /// </summary>
        public uint FileSize { get; private set; }

        public uint Unknown1 { get; private set; }
        public uint Unknown2 { get; private set; } // Always appears to be 8

        /// <summary>
        /// A list of blocks this resource has.
        /// </summary>
        public readonly Dictionary<BlockType, Block> Blocks;

        /// <summary>
        /// Gets or sets the type of the resource.
        /// </summary>
        /// <value>The type of the resource.</value>
        public ResourceType ResourceType { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Resource"/> class.
        /// </summary>
        public Resource()
        {
            Blocks = new Dictionary<BlockType, Block>();
        }

        /// <summary>
        /// Releases binary reader.
        /// </summary>
        ~Resource()
        {
            if (Reader != null)
            {
                Reader.Dispose();

                Reader = null;
            }
        }

        /// <summary>
        /// Reads the given <see cref="Stream"/>.
        /// </summary>
        /// <param name="input">The input <see cref="Stream"/> to read from.</param>
        public void Read(Stream input)
        {
            Reader = new BinaryReader(input);

            FileSize = Reader.ReadUInt32();

            // TODO: Some real files seem to have different file size
            if (FileSize != Reader.BaseStream.Length)
            {
                //throw new Exception(string.Format("File size does not match size specified in file. {0} != {1}", FileSize, Reader.BaseStream.Length));
            }

            Unknown1 = Reader.ReadUInt32();
            Unknown2 = Reader.ReadUInt32();

            var blockCount = Reader.ReadUInt32();

            while (blockCount-- > 0)
            {
                var blockType = Encoding.UTF8.GetString(Reader.ReadBytes(4));
                var block = Block.ConstructFromType(blockType);

                // Offset is relative to current position
                block.Offset = (uint)Reader.BaseStream.Position + Reader.ReadUInt32();
                block.Size = Reader.ReadUInt32();

                Blocks.Add(block.GetChar(), block);
            }

            foreach (var block in Blocks.Values)
            {
                block.Read(Reader);
            }
        }

        /// <summary>
        /// Opens and reads the given filename.
        /// </summary>
        /// <param name="filename">The file to open and read.</param>
        public void Read(string filename)
        {
            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                Read(fs);
            }
        }
    }
}
