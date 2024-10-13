using System.IO;
using System.Text;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class SpecialDependency
    {
        public string String { get; set; }
        public string CompilerIdentifier { get; set; }
        public long Fingerprint { get; set; } // why long?
        public long UserData { get; set; }

        public SpecialDependency(BinaryReader reader)
        {
            String = reader.ReadOffsetString(Encoding.UTF8);
            CompilerIdentifier = reader.ReadOffsetString(Encoding.UTF8);
            Fingerprint = reader.ReadUInt32();
            UserData = reader.ReadUInt32();
        }

        public SpecialDependency(KVObject data)
        {
            String = data.GetProperty<string>("m_String");
            CompilerIdentifier = data.GetProperty<string>("m_CompilerIdentifier");
            Fingerprint = data.GetIntegerProperty("m_nFingerprint");
            UserData = data.GetIntegerProperty("m_nUserData");
        }
    }
}
