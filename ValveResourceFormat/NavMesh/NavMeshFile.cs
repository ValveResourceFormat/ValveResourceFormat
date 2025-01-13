using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.NavMesh
{
    public class NavMeshFile
    {
        public const uint MAGIC = 0xFEEDFACE;

        public uint Version { get; set; }
        public uint SubVersion { get; set; }

        public Dictionary<uint, NavMeshArea> Areas { get; private set; }
        private readonly Dictionary<byte, List<NavMeshArea>> HullAreas = [];
        public NavMeshLadder[] Ladders { get; private set; }
        public bool IsAnalyzed { get; private set; }
        public NavMeshGenerationParams GenerationParams { get; private set; }

        public KVObject CustomData { get; set; }

        public void Read(string filename)
        {
            using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Read(fs);
        }

        public void Read(Stream stream)
        {
            using var binaryReader = new BinaryReader(stream, Encoding.UTF8, true);
            Read(binaryReader);
        }

        public void Read(BinaryReader binaryReader)
        {
            var magic = binaryReader.ReadUInt32();
            if (magic != MAGIC)
            {
                throw new UnexpectedMagicException($"Unexpected magic, expected {MAGIC:X}", magic, nameof(magic));
            }

            var version = binaryReader.ReadUInt32();
            if (version < 30 || version > 35)
            {
                throw new UnexpectedMagicException("Unsupported nav version", version, nameof(version));
            }

            Areas = [];
            HullAreas.Clear();

            Version = version;
            SubVersion = binaryReader.ReadUInt32();

            var unk1 = binaryReader.ReadUInt32();
            IsAnalyzed = (unk1 & 0x00000001) > 0;

            Vector3[][] polygons = null;
            if (Version >= 31)
            {
                polygons = ReadPolygons(binaryReader);
            }

            if (Version >= 32)
            {
                var unk2 = binaryReader.ReadUInt32();
                Debug.Assert(unk2 == 0);
            }

            if (Version >= 35)
            {
                var unk3 = binaryReader.ReadUInt32();
                Debug.Assert(unk3 == 0);
            }

            ReadAreas(binaryReader, polygons);

            ReadLadders(binaryReader);

            var unkCount = binaryReader.ReadInt32();
            binaryReader.BaseStream.Position += sizeof(float) * unkCount * 18; //seem to be vector3s

            GenerationParams = new NavMeshGenerationParams();
            GenerationParams.Read(binaryReader, this);

            ReadCustomData(binaryReader);

            Debug.Assert(binaryReader.BaseStream.Position == binaryReader.BaseStream.Length);
        }

        private void ReadCustomData(BinaryReader binaryReader)
        {
            if (SubVersion > 0)
            {
                while (binaryReader.ReadByte() == 0)
                {
                    //Reading past 0x00 padding
                }

                var kv3 = new BinaryKV3();
                kv3.Offset = (uint)(binaryReader.BaseStream.Position - 1);
                kv3.Size = (uint)(binaryReader.BaseStream.Length - kv3.Offset);
                kv3.Read(binaryReader);

                CustomData = kv3.Data;
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

        private void ReadAreas(BinaryReader binaryReader, Vector3[][] polygons)
        {
            var areaCount = binaryReader.ReadUInt32();
            for (var i = 0; i < areaCount; i++)
            {
                var area = new NavMeshArea();
                area.Read(binaryReader, this, polygons);
                AddArea(area);
            }
        }

        private Vector3[][] ReadPolygons(BinaryReader binaryReader)
        {
            var cornerCount = binaryReader.ReadUInt32();
            var corners = new Vector3[cornerCount];
            for (var i = 0; i < cornerCount; i++)
            {
                corners[i] = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
            }

            var polygonCount = binaryReader.ReadUInt32();
            var polygons = new Vector3[polygonCount][];
            for (uint i = 0; i < polygonCount; i++)
            {
                polygons[i] = ReadPolygon(binaryReader, corners);
            }
            return polygons;
        }

        private Vector3[] ReadPolygon(BinaryReader binaryReader, Vector3[] corners)
        {
            var cornerCount = binaryReader.ReadByte();

            var polygon = new Vector3[cornerCount];
            for (var i = 0; i < cornerCount; i++)
            {
                var cornerIndex = binaryReader.ReadUInt32();
                polygon[i] = corners[cornerIndex];
            }
            if (Version >= 35)
            {
                var unk = binaryReader.ReadInt32();
                Debug.Assert(unk == -1);
            }
            return polygon;
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

        public List<NavMeshArea> GetHullAreas(byte hullIndex)
        {
            return HullAreas.GetValueOrDefault(hullIndex);
        }

        public NavMeshArea GetArea(uint areaId)
        {
            return Areas.GetValueOrDefault(areaId);
        }

        public override string ToString()
        {
            var stringBuilder = new StringBuilder();

            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Version: {Version}");
            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Sub version: {SubVersion}");
            stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Analyzed: {IsAnalyzed}");
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

                for (var i = 0; i < GenerationParams.HullCount; i++)
                {
                    var hull = GenerationParams.HullParams[i];
                    stringBuilder.AppendLine();
                    if (GenerationParams.NavGenVersion >= 9)
                    {
                        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Enabled: {hull.Enabled}");
                        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Short Height Enabled: {hull.ShortHeightEnabled}");
                        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Short Height: {hull.ShortHeight}");
                    }
                    if (GenerationParams.NavGenVersion >= 11)
                    {
                        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Border Erosion: {hull.BorderErosion}");
                    }

                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Radius: {hull.Radius}");
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Height: {hull.Height}");
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Max Climb: {hull.MaxClimb}");
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Max Slope: {hull.MaxSlope}");
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Max Jump Down Dist: {hull.MaxJumpDownDist}");
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Max Jump Horiz Dist Base: {hull.MaxJumpHorizDistBase}");
                    stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Hull {i} Max Jump Up Dist: {hull.MaxJumpUpDist}");
                }
            }

            return stringBuilder.ToString();
        }
    }
}
