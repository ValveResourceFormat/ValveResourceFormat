using System.IO;
using System.Text;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    /// <summary>
    /// Represents an argument dependency.
    /// </summary>
    public class ArgumentDependency
    {
        /// <summary>
        /// Gets or sets the parameter name.
        /// </summary>
        public string ParameterName { get; set; }

        /// <summary>
        /// Gets or sets the parameter type.
        /// </summary>
        public string ParameterType { get; set; }

        /// <summary>
        /// Gets or sets the fingerprint.
        /// </summary>
        public uint Fingerprint { get; set; }

        /// <summary>
        /// Gets or sets the default fingerprint.
        /// </summary>
        public uint FingerprintDefault { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArgumentDependency"/> class from a binary reader.
        /// </summary>
        public ArgumentDependency(BinaryReader reader)
        {
            ParameterName = reader.ReadOffsetString(Encoding.UTF8);
            ParameterType = reader.ReadOffsetString(Encoding.UTF8);
            Fingerprint = reader.ReadUInt32();
            FingerprintDefault = reader.ReadUInt32();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArgumentDependency"/> class from a KV object.
        /// </summary>
        public ArgumentDependency(KVObject data)
        {
            ParameterName = data.GetProperty<string>("m_ParameterName");
            ParameterType = data.GetProperty<string>("m_ParameterType");
            Fingerprint = data.GetUInt32Property("m_nFingerprint");
            FingerprintDefault = data.GetUInt32Property("m_nFingerprintDefault");
        }
    }
}
