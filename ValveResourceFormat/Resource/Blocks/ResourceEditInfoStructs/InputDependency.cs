using System.IO;
using System.Text;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class InputDependency
    {
        public string ContentRelativeFilename { get; set; }
        public string ContentSearchPath { get; set; }
        public uint FileCRC { get; set; }
        public bool Optional { get; set; }
        public bool FileExists { get; set; }
        public bool IsGameFile { get; set; }

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
