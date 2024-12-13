using System.Diagnostics;
using System.IO;

namespace ValveResourceFormat.NavMesh
{
    public class NavMeshArea
    {
        public uint AreaId { get; set; }
        public byte HullIndex { get; set; }
        public DynamicAttributeFlags DynamicAttributeFlags { get; set; }
        public Vector3[] Corners { get; set; }
        public NavMeshConnection[][] Connections { get; set; }
        public uint[] LaddersAbove { get; set; }
        public uint[] LaddersBelow { get; set; }

        private static NavMeshConnection[] ReadConnections(BinaryReader binaryReader)
        {
            var connectionCount = binaryReader.ReadUInt32();
            var connections = new NavMeshConnection[connectionCount];

            for (var i = 0; i < connectionCount; i++)
            {
                var connection = new NavMeshConnection();
                connection.Read(binaryReader);
                connections[i] = connection;
            }

            return connections;
        }

        public void Read(BinaryReader binaryReader, NavMeshFile navMeshFile, Vector3[][] polygons = null)
        {
            AreaId = binaryReader.ReadUInt32();
            DynamicAttributeFlags = (DynamicAttributeFlags)binaryReader.ReadInt64();
            HullIndex = binaryReader.ReadByte();

            if (navMeshFile.Version >= 31)
            {
                var polygonIndex = binaryReader.ReadUInt32();
                Corners = polygons[polygonIndex];
            }
            else
            {
                var cornerCount = binaryReader.ReadUInt32();

                Corners = new Vector3[cornerCount];
                for (var i = 0; i < cornerCount; i++)
                {
                    Corners[i] = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
                }
            }

            var unk1 = binaryReader.ReadUInt32();
            Debug.Assert(unk1 == 0);

            Connections = new NavMeshConnection[Corners.Length][];
            for (var i = 0; i < Corners.Length; i++)
            {
                Connections[i] = ReadConnections(binaryReader);
            }

            var unk2 = binaryReader.ReadByte(); //TODO: this is probably count for LegacyHidingSpotData, LegacyApproachAreaData or LegacySpotEncounterData
            Debug.Assert(unk2 == 0);
            var unk3 = binaryReader.ReadUInt32(); //TODO: this is probably count for LegacyHidingSpotData, LegacyApproachAreaData or LegacySpotEncounterData
            Debug.Assert(unk3 == 0);

            var ladderAboveCount = binaryReader.ReadUInt32();
            LaddersAbove = new uint[ladderAboveCount];
            for (var i = 0; i < ladderAboveCount; i++)
            {
                var ladderId = binaryReader.ReadUInt32();
                LaddersAbove[i] = ladderId;
            }

            var ladderBelowCount = binaryReader.ReadUInt32();
            LaddersBelow = new uint[ladderBelowCount];
            for (var i = 0; i < ladderBelowCount; i++)
            {
                var ladderId = binaryReader.ReadUInt32();
                LaddersBelow[i] = ladderId;
            }
        }
    }
}
