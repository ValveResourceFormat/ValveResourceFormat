using System.IO;
using System.Text;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class ArgumentDependency
    {
        public string ParameterName { get; set; }
        public string ParameterType { get; set; }
        public uint Fingerprint { get; set; }
        public uint FingerprintDefault { get; set; }

        public ArgumentDependency(BinaryReader reader)
        {
            ParameterName = reader.ReadOffsetString(Encoding.UTF8);
            ParameterType = reader.ReadOffsetString(Encoding.UTF8);
            Fingerprint = reader.ReadUInt32();
            FingerprintDefault = reader.ReadUInt32();
        }

        public ArgumentDependency(KVObject data)
        {
            ParameterName = data.GetProperty<string>("m_ParameterName");
            ParameterType = data.GetProperty<string>("m_ParameterType");
            Fingerprint = data.GetUInt32Property("m_nFingerprint");
            FingerprintDefault = data.GetUInt32Property("m_nFingerprintDefault");
        }
    }
}
