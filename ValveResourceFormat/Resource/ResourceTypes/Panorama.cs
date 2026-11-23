using System.Diagnostics;
using System.IO;
using System.IO.Hashing;
using System.Text;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a Panorama UI resource.
    /// </summary>
    public class Panorama : Block
    {
        /// <summary>
        /// Represents an image referenced by this panorama file.
        /// </summary>
        public class ImageEntry
        {
            /// <summary>
            /// Gets or sets the image file name.
            /// </summary>
            public required string Name { get; set; }
            /// <summary>
            /// Gets or sets the original width of the image.
            /// </summary>
            public ushort Width { get; set; }
            /// <summary>
            /// Gets or sets the original height of the image.
            /// </summary>
            public ushort Height { get; set; }
            /// <summary>
            /// Gets or sets the CRC32 checksum of the image file.
            /// </summary>
            public uint CRC32 { get; set; }

            internal static ImageEntry Read(BinaryReader reader, bool hasCRC32)
            {
                var entry = new ImageEntry
                {
                    Name = reader.ReadNullTermString(Encoding.UTF8),
                    Width = reader.ReadUInt16(),
                    Height = reader.ReadUInt16(),
                };

                if (hasCRC32)
                {
                    entry.CRC32 = reader.ReadUInt32();
                }

                return entry;
            }

            internal void Write(BinaryWriter writer, bool hasCRC32)
            {
                writer.Write(Encoding.UTF8.GetBytes(Name));
                writer.Write((byte)0); // null terminator
                writer.Write(Width);
                writer.Write(Height);

                if (hasCRC32)
                {
                    writer.Write(CRC32);
                }
            }
        }

        /// <summary>
        /// Gets the referenced images with their original dimensions.
        /// </summary>
        public List<ImageEntry> Images { get; } = [];

        /// <summary>
        /// Gets the raw data.
        /// </summary>
        public byte[] Data { get; private set; } = [];
        /// <summary>
        /// Gets the CRC32 checksum.
        /// </summary>
        public uint CRC32 { get; private set; }

        /// <inheritdoc/>
        public override BlockType Type => BlockType.DATA;

        /// <summary>
        /// Initializes a new instance of the <see cref="Panorama"/> class.
        /// </summary>
        public Panorama() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="Panorama"/> class with the given content.
        /// The <see cref="CRC32"/> checksum is computed from the data.
        /// </summary>
        /// <param name="data">The content as UTF-8 encoded text, such as layout, style, or script source.</param>
        /// <param name="images">The image entries to store in the block header.</param>
        public Panorama(byte[] data, List<ImageEntry> images)
        {
            Data = data;
            Images = images;
            CRC32 = Crc32.HashToUInt32(data);
        }

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = Offset;

            Debug.Assert(Resource != null);

            if (IsPlaintext())
            {
                Data = reader.ReadBytes((int)Size);

                return;
            }

            CRC32 = reader.ReadUInt32();

            var imageCount = reader.ReadUInt16();

            for (var i = 0; i < imageCount; i++)
            {
                Images.Add(ImageEntry.Read(reader, Resource.Version >= 3));
            }

            var headerSize = reader.BaseStream.Position - Offset;

            Data = reader.ReadBytes((int)Size - (int)headerSize);
        }

        /// <inheritdoc/>
        public override void Serialize(Stream stream)
        {
            if (IsPlaintext())
            {
                stream.Write(Data);
                return;
            }

            Debug.Assert(Resource != null);

            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            writer.Write(CRC32);
            writer.Write((ushort)Images.Count);

            foreach (var entry in Images)
            {
                entry.Write(writer, Resource.Version >= 3);
            }

            writer.Write(Data);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Outputs the Panorama data as UTF-8 encoded text.
        /// </remarks>
        public override void WriteText(IndentedTextWriter writer)
        {
            writer.Write(Encoding.UTF8.GetString(Data));
        }

        private bool IsPlaintext()
        {
            Debug.Assert(Resource != null);

            if (Resource.ResourceType == ResourceType.PanoramaScript && Resource.Version >= 4)
            {
                return true;
            }

            if (Resource.ResourceType == ResourceType.PanoramaTypescript && Resource.Version >= 2)
            {
                return true;
            }

            return false;
        }
    }
}
