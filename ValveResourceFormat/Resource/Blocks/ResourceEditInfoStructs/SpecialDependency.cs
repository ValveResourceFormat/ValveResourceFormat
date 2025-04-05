using System.IO;
using System.Text;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class SpecialDependency
    {
        public string String { get; set; }
        public string CompilerIdentifier { get; set; }
        public uint Fingerprint { get; set; }
        public uint UserData { get; set; }

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
            Fingerprint = data.GetUInt32Property("m_nFingerprint");
            UserData = data.GetUInt32Property("m_nUserData");
        }
    }
}
