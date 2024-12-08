using System.IO;

namespace ValveResourceFormat.NavMesh
{
    public class NavMeshLadder
    {
        public uint Id { get; set; }
        public float Width { get; set; }
        public float Length { get; set; }
        public Vector3 Top { get; set; }
        public Vector3 Bottom { get; set; }
        public NavDirectionType Direction { get; set; }
        public NavMeshArea TopForwardArea { get; set; }
        public NavMeshArea TopLeftArea { get; set; }
        public NavMeshArea TopRightArea { get; set; }
        public NavMeshArea TopBehindArea { get; set; }
        public NavMeshArea BottomArea { get; set; }

        public static NavMeshLadder Load(BinaryReader binaryReader, NavMeshFile navMeshFile)
        {
            return new()
            {
                Id = binaryReader.ReadUInt32(),

                Width = binaryReader.ReadSingle(),

                Top = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle()),
                Bottom = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle()),

                Length = binaryReader.ReadSingle(),

                Direction = (NavDirectionType)binaryReader.ReadUInt32(),

                TopForwardArea = navMeshFile.GetArea(binaryReader.ReadUInt32()),
                TopLeftArea = navMeshFile.GetArea(binaryReader.ReadUInt32()),
                TopRightArea = navMeshFile.GetArea(binaryReader.ReadUInt32()),
                TopBehindArea = navMeshFile.GetArea(binaryReader.ReadUInt32()),
                BottomArea = navMeshFile.GetArea(binaryReader.ReadUInt32())
            };
        }
    }
}
