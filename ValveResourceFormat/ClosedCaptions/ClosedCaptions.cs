using System.Collections;
using System.IO;
using System.IO.Hashing;
using System.Text;
using ValveKeyValue;

#nullable disable

namespace ValveResourceFormat.ClosedCaptions
{
    public class ClosedCaptions : IEnumerable<ClosedCaption>
    {
        public const int MAGIC = 0x44434356; // "VCCD"

        public List<ClosedCaption> Captions { get; private set; }

        private string FileName;

        public IEnumerator<ClosedCaption> GetEnumerator()
        {
            return ((IEnumerable<ClosedCaption>)Captions).GetEnumerator();
        }

        public ClosedCaption this[string key]
        {
            get
            {
                var hash = Crc32.HashToUInt32(Encoding.UTF8.GetBytes(key));
                return Captions.Find(caption => caption.Hash == hash);
            }
        }

        /// <summary>
        /// Opens and reads the given filename.
        /// The file is held open until the object is disposed.
        /// </summary>
        /// <param name="filename">The file to open and read.</param>
        public void Read(string filename)
        {
            var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            Read(filename, fs);
        }

        /// <summary>
        /// Reads the given <see cref="Stream"/>.
        /// </summary>
        /// <param name="filename">The filename <see cref="string"/>.</param>
        /// <param name="input">The input <see cref="Stream"/> to read from.</param>
        public void Read(string filename, Stream input)
        {
            FileName = Path.GetFileName(filename);

            if (!filename.StartsWith("subtitles_", StringComparison.Ordinal))
            {
                // TODO: Throw warning?
            }

            using var reader = new BinaryReader(input, Encoding.UTF8, true);
            Captions = [];

            if (reader.ReadUInt32() != MAGIC)
            {
                throw new InvalidDataException("Given file is not a VCCD.");
            }

            var version = reader.ReadUInt32();

            if (version != 1 && version != 2)
            {
                throw new InvalidDataException("Unsupported VCCD version: " + version);
            }

            // numblocks, not actually required for hash lookups or populating entire list
            reader.ReadUInt32();
            var blocksize = reader.ReadUInt32();
            var directorysize = reader.ReadUInt32();
            var dataoffset = reader.ReadUInt32();

            for (uint i = 0; i < directorysize; i++)
            {
                var caption = new ClosedCaption { Hash = reader.ReadUInt32() };

                if (version >= 2)
                {
                    caption.HashText = reader.ReadUInt32();
                }

                caption.Blocknum = reader.ReadInt32();
                caption.Offset = reader.ReadUInt16();
                caption.Length = reader.ReadUInt16();

                Captions.Add(caption);
            }

            // Probably could be inside the for loop above, but I'm unsure what the performance costs are of moving the position head manually a bunch compared to reading sequentually
            foreach (var caption in Captions)
            {
                reader.BaseStream.Position = dataoffset + (caption.Blocknum * blocksize) + caption.Offset;
                caption.Text = reader.ReadNullTermString(Encoding.Unicode, bufferLengthHint: caption.Length);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<ClosedCaption>)Captions).GetEnumerator();
        }

        public override string ToString()
        {
            var captionsToExport = new Dictionary<uint, string>(Captions.Count);

            foreach (var caption in Captions)
            {
                captionsToExport.Add(caption.Hash, caption.Text);
            }

            using var ms = new MemoryStream();
            KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(ms, captionsToExport, FileName);
            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }
}
