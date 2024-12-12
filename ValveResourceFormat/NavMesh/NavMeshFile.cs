using System.Diagnostics;
using System.IO;
using System.Text;
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
        private readonly Dictionary<byte, List<NavMeshArea>> HullAreas = [];
        public NavMeshLadder[] Ladders { get; private set; }
        public bool IsAnalyzed { get; private set; }
        public NavMeshMetadata Metadata { get; private set; }

        public void Read(string filename)
        {
            var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Read(fs);
        }

        public void Read(Stream stream)
        {
            using var binaryReader = new BinaryReader(stream, Encoding.UTF8, true);
            Read(binaryReader);
        }

        public void Read(BinaryReader binaryReader)
        {
            Areas = [];
            HullAreas.Clear();

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
                Debug.Assert(unk1 == 0);
                var unk2 = binaryReader.ReadUInt32();
                Debug.Assert(unk2 == 0);
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

            Metadata = new NavMeshMetadata();
            Metadata.Read(binaryReader, this);

            if (SubVersion > 0)
            {
                while (binaryReader.ReadByte() == 0)
                {
                    //Reading past 0x00 padding
                }

                var kv3 = new BinaryKV3();
                kv3.Offset = (uint)(binaryReader.BaseStream.Position - 1);
                kv3.Size = (uint)(binaryReader.BaseStream.Length - kv3.Offset);
                kv3.Read(binaryReader, null);
            }
            Debug.Assert(binaryReader.BaseStream.Position == binaryReader.BaseStream.Length);
        }

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
    }
}
