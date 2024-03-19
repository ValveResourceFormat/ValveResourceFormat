using System.IO;
using System.Text;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class AdditionalRelatedFile
    {
        public string ContentRelativeFilename { get; set; }
        public string ContentSearchPath { get; set; }
        public bool IsGameFile { get; set; }

        public AdditionalRelatedFile(BinaryReader reader)
        {
            ContentRelativeFilename = reader.ReadOffsetString(Encoding.UTF8);
            ContentSearchPath = reader.ReadOffsetString(Encoding.UTF8);
        }

        public AdditionalRelatedFile(KVObject data)
        {
            ContentRelativeFilename = data.GetProperty<string>("m_RelativeFilename");
            ContentSearchPath = data.GetProperty<string>("m_SearchPath");
            IsGameFile = data.GetProperty<bool>("m_bIsGameFile");
        }
    }
}
