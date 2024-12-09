using System.IO;

namespace ValveResourceFormat.NavMesh
{
    public class NavMeshConnection
    {
        public uint AreaIndex { get; set; }
        public uint EdgeIndex { get; set; }

        public void Read(BinaryReader binaryReader)
        {
            AreaIndex = binaryReader.ReadUInt32();
            EdgeIndex = binaryReader.ReadUInt32();
        }
    }
}
