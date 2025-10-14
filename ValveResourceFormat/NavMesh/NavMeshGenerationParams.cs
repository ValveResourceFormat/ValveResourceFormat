using System.Diagnostics;
using System.IO;
using System.Text;

#nullable disable

namespace ValveResourceFormat.NavMesh
{
    /// <summary>
    /// Navigation mesh generation parameters.
    /// </summary>
    public class NavMeshGenerationParams
    {
        /// <summary>
        /// Gets or sets the navigation generation version.
        /// </summary>
        public int NavGenVersion { get; set; }

        /// <summary>
        /// Gets or sets whether to use project defaults.
        /// </summary>
        public bool UseProjectDefaults { get; set; }

        /// <summary>
        /// Gets or sets the tile size.
        /// </summary>
        public float TileSize { get; set; }

        /// <summary>
        /// Gets or sets the cell size.
        /// </summary>
        public float CellSize { get; set; }

        /// <summary>
        /// Gets or sets the cell height.
        /// </summary>
        public float CellHeight { get; set; }

        /// <summary>
        /// Gets or sets the minimum region size.
        /// </summary>
        public int MinRegionSize { get; set; }

        /// <summary>
        /// Gets or sets the merged region size.
        /// </summary>
        public int MergedRegionSize { get; set; }

        /// <summary>
        /// Gets or sets the mesh sample distance.
        /// </summary>
        public float MeshSampleDistance { get; set; }

        /// <summary>
        /// Gets or sets the maximum sample error.
        /// </summary>
        public float MaxSampleError { get; set; }

        /// <summary>
        /// Gets or sets the maximum edge length.
        /// </summary>
        public int MaxEdgeLength { get; set; }

        /// <summary>
        /// Gets or sets the maximum edge error.
        /// </summary>
        public float MaxEdgeError { get; set; }

        /// <summary>
        /// Gets or sets the vertices per polygon.
        /// </summary>
        public int VertsPerPoly { get; set; }

        /// <summary>
        /// Gets or sets the small area on edge removal threshold.
        /// </summary>
        public float SmallAreaOnEdgeRemoval { get; set; }

        /// <summary>
        /// Gets or sets the hull preset name.
        /// </summary>
        public string HullPresetName { get; set; }

        /// <summary>
        /// Gets or sets the hull definitions file path.
        /// </summary>
        public string HullDefinitionsFile { get; set; }

        /// <summary>
        /// Gets or sets the hull count.
        /// </summary>
        public int HullCount { get; set; }

        /// <summary>
        /// Gets or sets the hull parameters.
        /// </summary>
        public NavMeshGenerationHullParams[] HullParams { get; set; }

        /// <summary>
        /// Reads generation parameters from a binary reader.
        /// </summary>
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
