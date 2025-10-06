using System.IO;

namespace ValveResourceFormat.NavMesh
{
    /// <summary>
    /// Represents a connection between navigation mesh areas.
    /// </summary>
    public class NavMeshConnection
    {
        /// <summary>
        /// Gets or sets the target area identifier.
        /// </summary>
        public uint AreaId { get; set; }

        /// <summary>
        /// Gets or sets the edge identifier.
        /// </summary>
        public uint EdgeId { get; set; }

        /// <summary>
        /// Reads the connection from a binary reader.
        /// </summary>
        public void Read(BinaryReader binaryReader)
        {
            AreaId = binaryReader.ReadUInt32();
            EdgeId = binaryReader.ReadUInt32();
        }
    }
}
