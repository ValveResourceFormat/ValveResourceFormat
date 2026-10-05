using System.IO;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

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
        public string? HullPresetName { get; set; }

        /// <summary>
        /// Gets or sets the hull definitions file path.
        /// </summary>
        public string? HullDefinitionsFile { get; set; }

        /// <summary>
        /// Gets or sets the hull count.
        /// </summary>
        public int HullCount { get; set; }

        /// <summary>
        /// Gets or sets the hull parameters.
        /// </summary>
        public NavMeshGenerationHullParams[] HullParams { get; set; } = [];

        /// <summary>
        /// Gets or sets whether gravity follows the rotation of movable nav meshes. Meaning unconfirmed.
        /// </summary>
        /// <remarks>
        /// The per movable mesh setting is <see cref="NavMeshFile.MovableMeshGravityFollowsRotation"/>.
        /// </remarks>
        public bool GravityFollowsRotation { get; set; }

        /// <summary>
        /// Reads generation parameters from a binary reader.
        /// </summary>
        public void Read(BinaryReader binaryReader, NavMeshFile navMeshFile)
        {
            NavGenVersion = binaryReader.ReadInt32();

            // Older versions store hulls in a different layout
            if (NavGenVersion < 6)
            {
                throw new UnexpectedMagicException("Unsupported nav generation version", NavGenVersion, nameof(NavGenVersion));
            }

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

            // Count is ignored before v12, three hulls are always stored
            if (NavGenVersion < 12)
            {
                HullCount = 3;
            }

            HullParams = new NavMeshGenerationHullParams[HullCount];

            for (var i = 0; i < HullCount; i++)
            {
                var hullParamsEntry = new NavMeshGenerationHullParams();
                hullParamsEntry.Read(binaryReader, this);
                HullParams[i] = hullParamsEntry;
            }

            if (NavGenVersion >= 10)
            {
                GravityFollowsRotation = binaryReader.ReadByte() != 0;
            }
        }

        internal void ReadExtraHullParams(KVDocument data)
        {
            if (!data.Root.TryGetValue("NavGenParams", out var navGenParams) || navGenParams.ValueType != KVValueType.Collection)
            {
                return;
            }

            var hullParams = navGenParams.GetArray("HullParams");

            if (hullParams == null || hullParams.Count != HullParams.Length)
            {
                return;
            }

            for (var i = 0; i < hullParams.Count; i++)
            {
                var hull = hullParams[i];

                if (hull.ValueType != KVValueType.Collection)
                {
                    continue;
                }

                HullParams[i].Name = hull.GetStringProperty("Name");
                HullParams[i].FlowMapEnabled = hull.GetBooleanProperty("FlowMap_Enabled");
                HullParams[i].FlowMapNodeMaxRadius = hull.GetFloatProperty("FlowMap_NodeMaxRadius");
            }
        }
    }
}
