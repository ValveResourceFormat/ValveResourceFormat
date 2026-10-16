using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.NavMesh
{
    /// <summary>
    /// Represents a navigation mesh file.
    /// </summary>
    public class NavMeshFile
    {
        /// <summary>
        /// Magic number for navigation mesh files.
        /// </summary>
        public const uint MAGIC = 0xFEEDFACE;

        /// <summary>
        /// Gets or sets the file version.
        /// </summary>
        public uint Version { get; set; }

        /// <summary>
        /// Gets or sets the sub-version.
        /// </summary>
        public uint SubVersion { get; set; }

        /// <summary>
        /// Gets the navigation mesh areas indexed by area ID.
        /// </summary>
        public Dictionary<uint, NavMeshArea> Areas { get; private set; } = [];
        private readonly Dictionary<byte, List<NavMeshArea>> HullAreas = [];

        /// <summary>
        /// Gets the ladders in the navigation mesh.
        /// </summary>
        public NavMeshLadder[] Ladders { get; private set; } = [];

        /// <summary>
        /// Gets whether the navigation mesh has been analyzed.
        /// </summary>
        public bool IsAnalyzed { get; private set; }

        /// <summary>
        /// Gets the place names stored in the place directory of this file.
        /// </summary>
        public string[] PlaceNames { get; private set; } = [];

        /// <summary>
        /// Gets whether the navigation mesh contains areas without a place name.
        /// </summary>
        public bool HasUnnamedAreas { get; private set; }

        /// <summary>
        /// The <see cref="NavMeshArea.MovableMeshId"/> value of areas that belong to the static world.
        /// </summary>
        public const uint NoMovableMesh = 0xFFFFFFFF;

        /// <summary>
        /// Gets the ids of the movable nav meshes stored in this file. Only stored in version 35 and newer.
        /// </summary>
        /// <remarks>
        /// Each id is a list of integers separated by colons.
        /// </remarks>
        public string[] MovableMeshIds { get; private set; } = [];

        /// <summary>
        /// Gets the transform of each movable nav mesh, parallel to <see cref="MovableMeshIds"/>.
        /// </summary>
        public Matrix4x4[] MovableMeshTransforms { get; private set; } = [];

        /// <summary>
        /// Gets whether gravity follows the rotation of each movable nav mesh, parallel to <see cref="MovableMeshIds"/>.
        /// Read from <see cref="MovableMeshSettings"/>, so only stored in version 36 and newer.
        /// </summary>
        public bool[] MovableMeshGravityFollowsRotation { get; private set; } = [];

        /// <summary>
        /// Gets the transformed bounding boxes stored in this file.
        /// </summary>
        public NavMeshTransformedBounds[] TransformedBounds { get; private set; } = [];

        /// <summary>
        /// Gets the generation parameters.
        /// </summary>
        public NavMeshGenerationParams? GenerationParams { get; private set; }

        /// <summary>
        /// Gets or sets custom data associated with the navigation mesh, only stored when <see cref="SubVersion"/> is not zero.
        /// </summary>
        /// <remarks>
        /// Counter-Strike 2 stores its bot analysis here, as an <c>arealist</c> array parallel to the areas in file order.
        /// Its <c>hidingspotdata</c> is also read into <see cref="NavMeshArea.HidingSpots"/>. The area and spot ids in
        /// <c>spotencounterdata</c> and <c>order</c> are written from runtime pointers and are not valid ids.
        /// </remarks>
        public KVDocument? CustomData { get; set; }

        /// <summary>
        /// Gets or sets the movable mesh settings, such as <see cref="MovableMeshGravityFollowsRotation"/>.
        /// Only stored in version 36 and newer.
        /// </summary>
        public KVDocument? MovableMeshSettings { get; set; }

        /// <summary>
        /// Gets or sets game specific data. Only stored in version 36 and newer.
        /// </summary>
        /// <remarks>
        /// No known game uses this, files contain an empty table.
        /// </remarks>
        public KVDocument? GameData { get; set; }

        /// <summary>
        /// Gets or sets extra generation parameters stored as <c>NavGenParams.HullParams</c>, which are also read into
        /// <see cref="NavMeshGenerationParams.HullParams"/>. Only stored in version 36 and newer.
        /// </summary>
        public KVDocument? ExtraGenerationParams { get; set; }

        /// <summary>
        /// Reads the navigation mesh from a file.
        /// </summary>
        public void Read(string filename)
        {
            using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Read(fs);
        }

        /// <summary>
        /// Reads the navigation mesh from a stream.
        /// </summary>
        public void Read(Stream stream)
        {
            using var binaryReader = new BinaryReader(stream, Encoding.UTF8, true);
            Read(binaryReader);
        }

        /// <summary>
        /// Reads the navigation mesh from a binary reader.
        /// </summary>
        public void Read(BinaryReader binaryReader)
        {
            var magic = binaryReader.ReadUInt32();
            if (magic != MAGIC)
            {
                throw new UnexpectedMagicException($"Unexpected magic, expected {MAGIC:X}", magic, nameof(magic));
            }

            var version = binaryReader.ReadUInt32();
            if (version < 30 || version > 36)
            {
                throw new UnexpectedMagicException("Unsupported nav version", version, nameof(version));
            }

            Areas = [];
            HullAreas.Clear();

            Version = version;
            SubVersion = binaryReader.ReadUInt32();

            IsAnalyzed = binaryReader.ReadByte() != 0;

            ReadPlaces(binaryReader);

            if (Version >= 36)
            {
                MovableMeshSettings = ReadKV3(binaryReader);
                MovableMeshGravityFollowsRotation = [.. MovableMeshSettings.Root.GetIntegerArray("movable_mesh_gravity_follows_rotation").Select(value => value == 1)];
            }

            NavMeshPolygon[]? polygons = null;
            if (Version >= 31)
            {
                polygons = ReadPolygons(binaryReader);
            }

            if (Version >= 35)
            {
                ReadMovableMeshes(binaryReader);
            }

            if (Version >= 36)
            {
                GameData = ReadKV3(binaryReader);
            }

            var areasInFileOrder = ReadAreas(binaryReader, polygons);

            ReadLadders(binaryReader);

            ReadTransformedBounds(binaryReader);

            GenerationParams = new NavMeshGenerationParams();
            GenerationParams.Read(binaryReader, this);

            if (Version >= 36)
            {
                ExtraGenerationParams = ReadKV3(binaryReader);
                GenerationParams.ReadExtraHullParams(ExtraGenerationParams);
            }

            ReadCustomData(binaryReader, areasInFileOrder);

            Debug.Assert(binaryReader.BaseStream.Position == binaryReader.BaseStream.Length);
        }

        private static KVDocument ReadKV3(BinaryReader binaryReader)
        {
            // Align to 8-byte boundary
            binaryReader.BaseStream.Position = (binaryReader.BaseStream.Position + 7) & ~7L;

            var kv3 = new BinaryKV3
            {
                Offset = (uint)binaryReader.BaseStream.Position,
                Resource = null!
            };
            kv3.Read(binaryReader);
            return kv3.Data;
        }

        private void ReadCustomData(BinaryReader binaryReader, NavMeshArea[] areas)
        {
            if (SubVersion <= 0)
            {
                return;
            }

            CustomData = ReadKV3(binaryReader);

            var areaList = CustomData.Root.GetArray("arealist");
            if (areaList == null || areaList.Count != areas.Length)
            {
                return;
            }

            for (var i = 0; i < areas.Length; i++)
            {
                if (areaList[i].ValueType != KVValueType.Collection)
                {
                    continue;
                }

                var hidingSpots = areaList[i].GetArray("hidingspotdata");
                if (hidingSpots == null || hidingSpots.Count == 0)
                {
                    continue;
                }

                var spots = new NavMeshHidingSpot[hidingSpots.Count];
                for (var j = 0; j < spots.Length; j++)
                {
                    spots[j] = NavMeshHidingSpot.FromKV(hidingSpots[j]);
                }

                areas[i].HidingSpots = spots;
            }
        }

        private void ReadPlaces(BinaryReader binaryReader)
        {
            var placeCount = binaryReader.ReadUInt16();
            PlaceNames = new string[placeCount];

            for (var i = 0; i < placeCount; i++)
            {
                var length = binaryReader.ReadUInt16();
                var name = binaryReader.ReadBytes(length);
                PlaceNames[i] = Encoding.UTF8.GetString(name).TrimEnd('\0');
            }

            HasUnnamedAreas = binaryReader.ReadByte() != 0;
        }

        private void ReadMovableMeshes(BinaryReader binaryReader)
        {
            var movableMeshCount = binaryReader.ReadUInt32();
            MovableMeshIds = new string[movableMeshCount];
            MovableMeshTransforms = new Matrix4x4[movableMeshCount];

            for (var i = 0; i < movableMeshCount; i++)
            {
                MovableMeshIds[i] = binaryReader.ReadNullTermString(Encoding.ASCII);
                MovableMeshTransforms[i] = binaryReader.ReadMatrix3x4();
            }
        }

        private void ReadTransformedBounds(BinaryReader binaryReader)
        {
            var boundsCount = binaryReader.ReadInt32();
            TransformedBounds = new NavMeshTransformedBounds[boundsCount];

            for (var i = 0; i < boundsCount; i++)
            {
                TransformedBounds[i] = new NavMeshTransformedBounds(binaryReader);
            }
        }

        private void ReadLadders(BinaryReader binaryReader)
        {
            var ladderCount = binaryReader.ReadUInt32();
            Ladders = new NavMeshLadder[ladderCount];
            for (var i = 0; i < ladderCount; i++)
            {
                var ladder = new NavMeshLadder();
                ladder.Read(binaryReader, this);
                Ladders[i] = ladder;
            }
        }

        private NavMeshArea[] ReadAreas(BinaryReader binaryReader, NavMeshPolygon[]? polygons)
        {
            var areaCount = binaryReader.ReadUInt32();
            var areas = new NavMeshArea[areaCount];
            for (var i = 0; i < areaCount; i++)
            {
                var area = new NavMeshArea();
                area.Read(binaryReader, this, polygons);
                AddArea(area);
                areas[i] = area;
            }

            return areas;
        }

        private NavMeshPolygon[] ReadPolygons(BinaryReader binaryReader)
        {
            var cornerCount = binaryReader.ReadUInt32();
            var corners = new Vector3[cornerCount];
            for (var i = 0; i < cornerCount; i++)
            {
                corners[i] = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
            }

            var polygonCount = binaryReader.ReadUInt32();
            var polygons = new NavMeshPolygon[polygonCount];
            var cornerIndices = new List<uint>();
            for (uint i = 0; i < polygonCount; i++)
            {
                var polygonCornerCount = binaryReader.ReadByte();
                var polygonCorners = new Vector3[polygonCornerCount];
                for (var j = 0; j < polygonCornerCount; j++)
                {
                    var cornerIndex = binaryReader.ReadUInt32();
                    cornerIndices.Add(cornerIndex);
                    polygonCorners[j] = corners[cornerIndex];
                }

                polygons[i] = new NavMeshPolygon
                {
                    Corners = polygonCorners,
                    MovableMeshId = Version >= 35 ? binaryReader.ReadUInt32() : NoMovableMesh,
                };
            }

            var cornerGravity = Version >= 32 ? ReadCornerGravity(binaryReader) : null;
            if (cornerGravity != null)
            {
                var cornerIndex = 0;
                for (var i = 0; i < polygons.Length; i++)
                {
                    var polygonGravity = new Vector3[polygons[i].Corners.Length];
                    for (var j = 0; j < polygonGravity.Length; j++)
                    {
                        polygonGravity[j] = cornerGravity[cornerIndices[cornerIndex++]];
                    }

                    polygons[i] = polygons[i] with { CornerGravity = polygonGravity };
                }
            }

            return polygons;
        }

        // Gravity directions are indexed by corner, same as the shared corner array
        private static Vector3[]? ReadCornerGravity(BinaryReader binaryReader)
        {
            // Bit 0 is the only flag, it is set when any corner has a gravity direction other than the default
            var flags = binaryReader.ReadUInt32();
            if ((flags & 1) == 0)
            {
                return null;
            }

            var gravityCount = binaryReader.ReadUInt32();
            var gravity = new Vector3[gravityCount];
            for (var i = 0; i < gravityCount; i++)
            {
                gravity[i] = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
            }

            return gravity;
        }

        private void AddArea(NavMeshArea area)
        {
            Areas[area.AreaId] = area;

            if (HullAreas.TryGetValue(area.HullIndex, out var areas))
            {
                areas.Add(area);
            }
            else
            {
                HullAreas[area.HullIndex] = [area];
            }
        }

        /// <summary>
        /// Gets all navigation mesh areas for the specified hull index.
        /// </summary>
        public List<NavMeshArea>? GetHullAreas(byte hullIndex)
        {
            return HullAreas.GetValueOrDefault(hullIndex);
        }

        /// <summary>
        /// Gets a navigation mesh area by its identifier.
        /// </summary>
        public NavMeshArea? GetArea(uint areaId)
        {
            return Areas.GetValueOrDefault(areaId);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Returns a formatted summary of the navigation mesh including version, area count, and generation parameters.
        /// </remarks>
        public override string ToString()
        {
            var stringBuilder = new StringBuilder();

            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Version: {Version}");
            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Sub version: {SubVersion}");
            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Analyzed: {IsAnalyzed}");

            if (PlaceNames.Length > 0)
            {
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Places: {string.Join(", ", PlaceNames)}");
            }

            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Number of areas: {Areas?.Count ?? 0}");
            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Number of ladders: {Ladders?.Length ?? 0}");

            if (GenerationParams != null)
            {
                stringBuilder.AppendLine();
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Nav Gen Version: {GenerationParams.NavGenVersion}");
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Use Project Defaults: {GenerationParams.UseProjectDefaults}");
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Tile Size: {GenerationParams.TileSize}");
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Cell Size: {GenerationParams.CellSize}");
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Cell Height: {GenerationParams.CellHeight}");
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Min Region Size: {GenerationParams.MinRegionSize}");
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Merged Region Size: {GenerationParams.MergedRegionSize}");
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Mesh Sample Distance: {GenerationParams.MeshSampleDistance}");
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Max Sample Error: {GenerationParams.MaxSampleError}");
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Max Edge Length: {GenerationParams.MaxEdgeLength}");
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Max Edge Error: {GenerationParams.MaxEdgeError}");
                stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Verts Per Poly: {GenerationParams.VertsPerPoly}");

                if (GenerationParams.NavGenVersion >= 7)
                {
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Small Area On Edge Removal: {GenerationParams.SmallAreaOnEdgeRemoval}");
                }

                if (GenerationParams.NavGenVersion >= 12)
                {
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull Preset Name: {GenerationParams.HullPresetName}");
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull Definitions File: {GenerationParams.HullDefinitionsFile}");
                }

                if (GenerationParams.NavGenVersion >= 10)
                {
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Gravity Follows Rotation: {GenerationParams.GravityFollowsRotation}");
                }

                for (var i = 0; i < GenerationParams.HullParams.Length; i++)
                {
                    var hull = GenerationParams.HullParams[i];
                    stringBuilder.AppendLine();
                    if (GenerationParams.NavGenVersion >= 9)
                    {
                        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Enabled: {hull.Enabled}");
                    }
                    if (GenerationParams.NavGenVersion >= 8)
                    {
                        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Short Height Enabled: {hull.ShortHeightEnabled}");
                        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Short Height: {hull.ShortHeight}");
                    }
                    if (GenerationParams.NavGenVersion >= 11)
                    {
                        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Border Erosion: {hull.BorderErosion}");
                    }
                    if (GenerationParams.NavGenVersion >= 13)
                    {
                        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Agent Crawl Enabled: {hull.AgentCrawlEnabled}");
                        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Agent Crawl Height: {hull.AgentCrawlHeight}");
                    }

                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Radius: {hull.Radius}");
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Height: {hull.Height}");
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Max Climb: {hull.MaxClimb}");
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Max Slope: {hull.MaxSlope}");
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Max Jump Down Dist: {hull.MaxJumpDownDist}");
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Max Jump Horiz Dist Base: {hull.MaxJumpHorizDistBase}");
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Max Jump Up Dist: {hull.MaxJumpUpDist}");

                    if (hull.Name != null)
                    {
                        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Name: {hull.Name}");
                        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Flow Map Enabled: {hull.FlowMapEnabled}");
                        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Flow Map Node Max Radius: {hull.FlowMapNodeMaxRadius}");
                    }
                }
            }

            return stringBuilder.ToString();
        }
    }
}
