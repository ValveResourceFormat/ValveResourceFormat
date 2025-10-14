using System.IO;
using System.Text;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    /// <summary>
    /// Represents an input file dependency.
    /// </summary>
    public class InputDependency
    {
        /// <summary>
        /// Gets or sets the content-relative filename.
        /// </summary>
        public string ContentRelativeFilename { get; set; }

        /// <summary>
        /// Gets or sets the content search path.
        /// </summary>
        public string ContentSearchPath { get; set; }

        /// <summary>
        /// Gets or sets the file CRC.
        /// </summary>
        public uint FileCRC { get; set; }

        /// <summary>
        /// Gets or sets whether the dependency is optional.
        /// </summary>
        public bool Optional { get; set; }

        /// <summary>
        /// Gets or sets whether the file exists.
        /// </summary>
        public bool FileExists { get; set; }

        /// <summary>
        /// Gets or sets whether this is a game file.
        /// </summary>
        public bool IsGameFile { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="InputDependency"/> class from a binary reader.
        /// </summary>
        public InputDependency(BinaryReader reader)
        {
            ContentRelativeFilename = reader.ReadOffsetString(Encoding.UTF8);
            ContentSearchPath = reader.ReadOffsetString(Encoding.UTF8);
            FileCRC = reader.ReadUInt32();

            var flags = reader.ReadUInt32();
            Optional = (flags & 1) != 0;
            FileExists = (flags & 2) != 0;
            IsGameFile = (flags & 4) != 0;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InputDependency"/> class from a KV object.
        /// </summary>
        public InputDependency(KVObject data)
        {
            ContentRelativeFilename = data.GetProperty<string>("m_RelativeFilename");
            ContentSearchPath = data.GetProperty<string>("m_SearchPath");
            FileCRC = data.GetUInt32Property("m_nFileCRC");
            Optional = data.GetProperty<bool>("m_bOptional");
            FileExists = data.GetProperty<bool>("m_bExists");
            IsGameFile = data.GetProperty<bool>("m_bIsGameFile");
        }
    }
}
