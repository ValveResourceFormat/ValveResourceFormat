using System.Diagnostics;
using System.IO;
using System.Text;

namespace ValveResourceFormat.NavMesh
{
    public class NavMeshMetadata
    {
        public int UnkInt1 { get; set; }
        public int UnkInt2 { get; set; }
        public int UnkInt3 { get; set; }

        public float TileSize { get; set; }

        public float CellSize { get; set; }
        public float CellHeight { get; set; }

        public int MinRegionSize { get; set; }
        public int MergedRegionSize { get; set; }

        public float MeshSampleDistance { get; set; }
        public float MaxSampleError { get; set; }

        public int MaxEdgeLength { get; set; }
        public float MaxEdgeError { get; set; }
        public int VertsPerPoly { get; set; }

        public float SmallAreaOnEdgeRemoval { get; set; }

        public string HullPresetName { get; set; }
        public string HullDefinitionsFile { get; set; }
        public int HullCount { get; set; }
        public NavMeshHullMetadata[] HullData { get; set; }
        public byte UnkByte3 { get; set; }

        public void Read(BinaryReader binaryReader, NavMeshFile navMeshFile)
        {
            UnkInt1 = binaryReader.ReadInt32();
            UnkInt2 = binaryReader.ReadInt32();
            UnkInt3 = binaryReader.ReadInt32();

            //Tiles
            TileSize = binaryReader.ReadSingle();

            //Rasterization
            CellSize = binaryReader.ReadSingle();
            CellHeight = binaryReader.ReadSingle();

            //Region
            MinRegionSize = binaryReader.ReadInt32();
            MergedRegionSize = binaryReader.ReadInt32();

            //Detail Mesh
            MeshSampleDistance = binaryReader.ReadSingle();
            MaxSampleError = binaryReader.ReadSingle();

            //Polygonization
            MaxEdgeLength = binaryReader.ReadInt32();
            MaxEdgeError = binaryReader.ReadSingle();
            VertsPerPoly = binaryReader.ReadInt32();

            //Processing params
            SmallAreaOnEdgeRemoval = binaryReader.ReadSingle();

            if (navMeshFile.Version >= 35)
            {
                HullPresetName = binaryReader.ReadNullTermString(Encoding.UTF8);
                HullDefinitionsFile = binaryReader.ReadNullTermString(Encoding.UTF8);
            }

            HullCount = binaryReader.ReadInt32();
            HullData = new NavMeshHullMetadata[HullCount];
            for (var i = 0; i < HullCount; i++)
            {
                var hullData = new NavMeshHullMetadata();
                hullData.Read(binaryReader, navMeshFile);
                HullData[i] = hullData;
            }

            if (navMeshFile.Version >= 35)
            {
                UnkByte3 = binaryReader.ReadByte();
                Debug.Assert(UnkByte3 == 0 || UnkByte3 == 1);
            }
        }
    }
}
