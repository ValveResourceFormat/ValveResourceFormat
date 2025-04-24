using System.Diagnostics;
using System.IO;
using System.Text;

#nullable disable

namespace ValveResourceFormat.NavMesh
{
    public class NavMeshGenerationParams
    {
        public int NavGenVersion { get; set; }
        public bool UseProjectDefaults { get; set; }

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
        public NavMeshGenerationHullParams[] HullParams { get; set; }

        public void Read(BinaryReader binaryReader, NavMeshFile navMeshFile)
        {
            NavGenVersion = binaryReader.ReadInt32();
            UseProjectDefaults = binaryReader.ReadUInt32() != 0;

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

            if (NavGenVersion >= 7)
            {
                //Processing params
                SmallAreaOnEdgeRemoval = binaryReader.ReadSingle();
            }

            if (NavGenVersion >= 12)
            {
                HullPresetName = binaryReader.ReadNullTermString(Encoding.UTF8);
                HullDefinitionsFile = binaryReader.ReadNullTermString(Encoding.UTF8);
            }

            HullCount = binaryReader.ReadInt32();
            HullParams = new NavMeshGenerationHullParams[HullCount];
            for (var i = 0; i < HullCount; i++)
            {
                var hullParamsEntry = new NavMeshGenerationHullParams();
                hullParamsEntry.Read(binaryReader, this);
                HullParams[i] = hullParamsEntry;
            }

            if (NavGenVersion <= 11)
            {
                //Version <=11 stores 3 hulls even if less are used (citadel start.nav)
                var tempHullParams = new NavMeshGenerationHullParams();
                for (var i = HullCount; i < 3; i++)
                {
                    tempHullParams.Read(binaryReader, this);
                }
            }

            if (NavGenVersion >= 12)
            {
                var unkByte = binaryReader.ReadByte();
                Debug.Assert(unkByte == 0 || unkByte == 1);
            }
        }
    }
}
