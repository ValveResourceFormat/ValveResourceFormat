using System.IO;

namespace ValveResourceFormat.NavMesh
{
    public class NavMeshConnection
    {
        public uint AreaId { get; set; }

        public NavMeshConnection(BinaryReader binaryReader)
        {
            AreaId = binaryReader.ReadUInt32();
            binaryReader.ReadUInt32(); //TODO
        }
    }
}
