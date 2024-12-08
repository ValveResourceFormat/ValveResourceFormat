using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using SkiaSharp;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.NavMesh
{
    public class NavMeshFile
    {
        public const uint MAGIC = 0xFEEDFACE;

        public uint Version { get; set; }
        public uint SubVersion { get; set; }

        public Dictionary<uint, NavMeshArea> Areas { get; private set; }
        private readonly Dictionary<byte, List<NavMeshArea>> LayerAreas = [];
        public NavMeshLadder[] Ladders { get; private set; }
        public int LayerCount { get; private set; }
        public bool IsAnalyzed { get; private set; }

        private static Vector3[][] ReadPolygons(BinaryReader binaryReader)
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

        private static void ReadPostMetadata(BinaryReader binaryReader)
        {
            var unkBytes = binaryReader.ReadBytes(8);
            var unk1 = binaryReader.ReadUInt32(); //count?

            //Tiles
            var tileSize = binaryReader.ReadSingle();

            //Rasterization
            var cellSize = binaryReader.ReadSingle();
            var cellHeight = binaryReader.ReadSingle();

            //Region
            var minRegionSize = binaryReader.ReadUInt32();
            var mergedRegionSize = binaryReader.ReadUInt32();

            //Detail Mesh
            var meshSampleDistance = binaryReader.ReadSingle();
            var maxSampleError = binaryReader.ReadSingle();

            //Polygonization
            var maxEdgeLength = binaryReader.ReadUInt32();
            var maxEdgeError = binaryReader.ReadSingle();
            var vertsPerPoly = binaryReader.ReadUInt32();

            //Processing params
            var smallAreaOnEdgeRemoval = binaryReader.ReadSingle();

            var unkString = binaryReader.ReadNullTermString(Encoding.UTF8);
            var unkString2 = binaryReader.ReadNullTermString(Encoding.UTF8); //this might be a byte instead - only seen as a 0x00 byte

            var unk2 = binaryReader.ReadUInt32();
            var unk3 = binaryReader.ReadByte();

            var unk4 = binaryReader.ReadSingle(); //related to agent/human size - values seen so far are 15 and 16
            Debug.Assert(Math.Abs(unk4 - 15.5) < 0.51);

            var agentHeight = binaryReader.ReadSingle(); //=71
            Debug.Assert(Math.Abs(agentHeight - 71) < 0.01);

            var unk5 = binaryReader.ReadByte();

            var halfHumanHeight = binaryReader.ReadSingle(); //=35.5
            Debug.Assert(Math.Abs(halfHumanHeight - 35.5) < 0.01);

            var halfHumanWidth = binaryReader.ReadSingle(); //=16
            Debug.Assert(Math.Abs(halfHumanWidth - 16) < 0.01);

            var unk6 = binaryReader.ReadUInt32(); //=50
            var unk7 = binaryReader.ReadSingle(); //=157
            var unk8 = binaryReader.ReadSingle(); //=64
            var unk9 = binaryReader.ReadSingle(); //=68

            var unk10 = binaryReader.ReadUInt32(); //=0
            var unk11 = binaryReader.ReadUInt32(); //=1
            Debug.Assert(unk10 == 0);
            Debug.Assert(unk11 == 0 || unk11 == 1);
        }

        private static Vector3[] ReadPolygon(BinaryReader binaryReader, Vector3[] corners)
        {
            var cornerCount = binaryReader.ReadByte();

            var polygon = new Vector3[cornerCount];
            for (var i = 0; i < cornerCount; i++)
            {
                var cornerIndex = binaryReader.ReadUInt32();
                polygon[i] = corners[cornerIndex];
            }
            var unk = binaryReader.ReadInt32();
            Debug.Assert(unk == -1);
            return polygon;
        }

        public void Read(Stream stream)
        {
            using var binaryReader = new BinaryReader(stream, Encoding.UTF8, true);
            Read(binaryReader);
        }

        public void Read(BinaryReader binaryReader)
        {
            Areas = [];
            LayerAreas.Clear();

            var magic = binaryReader.ReadUInt32();
            if (magic != MAGIC)
            {
                throw new UnexpectedMagicException($"Unexpected magic, expected {MAGIC:X}", magic, nameof(magic));
            }

            Version = binaryReader.ReadUInt32();
            SubVersion = binaryReader.ReadUInt32();
            IsAnalyzed = binaryReader.ReadUInt32() != 0;

            Vector3[][] polygons = null;
            if (Version >= 35)
            {
                polygons = ReadPolygons(binaryReader);
                var unk1 = binaryReader.ReadUInt32();
                var unk2 = binaryReader.ReadUInt32();
            }

            var areaCount = binaryReader.ReadUInt32();
            for (var i = 0; i < areaCount; i++)
            {
                var area = new NavMeshArea();
                area.Read(binaryReader, this, polygons);
                AddArea(area);
            }

            var ladderCount = binaryReader.ReadUInt32();
            Ladders = new NavMeshLadder[ladderCount];
            for (var i = 0; i < ladderCount; i++)
            {
                var ladder = new NavMeshLadder();
                ladder.Read(binaryReader, this);
                Ladders[i] = ladder;
            }

            if (Version >= 35)
            {
                ReadPostMetadata(binaryReader);

                while (binaryReader.ReadByte() == 0)
                {
                    //Reading past 0x00 padding
                }

                var kv3 = new BinaryKV3();
                kv3.Offset = (uint)(binaryReader.BaseStream.Position - 1);
                kv3.Size = (uint)(binaryReader.BaseStream.Length - kv3.Offset);
                kv3.Read(binaryReader, null);
            }
            else
            {
                var unkBytes = binaryReader.ReadBytes(144); //TODO - similar to newer postmetadata
                Debug.Assert(binaryReader.BaseStream.Position == binaryReader.BaseStream.Length);
            }
        }

        private void AddArea(NavMeshArea area)
        {
            Areas[area.AreaId] = area;
            LayerCount = Math.Max(LayerCount, area.AgentLayer + 1);

            if (LayerAreas.TryGetValue(area.AgentLayer, out var areas))
            {
                areas.Add(area);
            }
            else
            {
                LayerAreas[area.AgentLayer] = [area];
            }
        }

        public List<NavMeshArea> GetLayerAreas(byte layer)
        {
            return LayerAreas.GetValueOrDefault(layer);
        }

        public NavMeshArea GetArea(uint areaId)
        {
            return Areas.GetValueOrDefault(areaId);
        }
    }
}
