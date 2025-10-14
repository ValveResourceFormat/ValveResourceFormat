using System.IO;
using System.Text;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    /// <summary>
    /// Represents a special dependency.
    /// </summary>
    public class SpecialDependency
    {
        /// <summary>
        /// Gets or sets the string value.
        /// </summary>
        public string String { get; set; }

        /// <summary>
        /// Gets or sets the compiler identifier.
        /// </summary>
        public string CompilerIdentifier { get; set; }

        /// <summary>
        /// Gets or sets the fingerprint.
        /// </summary>
        public uint Fingerprint { get; set; }

        /// <summary>
        /// Gets or sets the user data.
        /// </summary>
        public uint UserData { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SpecialDependency"/> class from a binary reader.
        /// </summary>
        public SpecialDependency(BinaryReader reader)
        {
            String = reader.ReadOffsetString(Encoding.UTF8);
            CompilerIdentifier = reader.ReadOffsetString(Encoding.UTF8);
            Fingerprint = reader.ReadUInt32();
            UserData = reader.ReadUInt32();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SpecialDependency"/> class from a KV object.
        /// </summary>
        public SpecialDependency(KVObject data)
        {
            String = data.GetProperty<string>("m_String");
            CompilerIdentifier = data.GetProperty<string>("m_CompilerIdentifier");
            Fingerprint = data.GetUInt32Property("m_nFingerprint");
            UserData = data.GetUInt32Property("m_nUserData");
        }
    }
}
