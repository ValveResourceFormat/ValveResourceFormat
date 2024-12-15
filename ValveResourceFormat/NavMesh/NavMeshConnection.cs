using System.IO;

namespace ValveResourceFormat.NavMesh
{
    public class NavMeshConnection
    {
        public uint AreaId { get; set; }
        public uint EdgeId { get; set; }

        public void Read(BinaryReader binaryReader)
        {
            AreaId = binaryReader.ReadUInt32();
            EdgeId = binaryReader.ReadUInt32();
        }
    }
}
