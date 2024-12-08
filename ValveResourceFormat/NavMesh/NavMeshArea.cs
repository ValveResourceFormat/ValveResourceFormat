using System.Diagnostics;
using System.IO;

namespace ValveResourceFormat.NavMesh
{
    public class NavMeshArea
    {
        public uint AreaId { get; set; }
        public byte AgentLayer { get; set; }
        public DynamicAttributeFlags DynamicAttributeFlags { get; set; }
        public Vector3[] Corners { get; set; }
        public NavMeshConnection[][] Connections { get; set; }
        public uint[] LaddersAbove { get; set; }
        public uint[] LaddersBelow { get; set; }

        public static NavMeshConnection[] ReadConnections(BinaryReader binaryReader)
        {
            var connectionCount = binaryReader.ReadUInt32();
            var connections = new NavMeshConnection[connectionCount];

            for (var i = 0; i < connectionCount; i++)
            {
                connections[i] = new NavMeshConnection(binaryReader);
            }

            return connections;
        }

        public static NavMeshArea Read(BinaryReader binaryReader, NavMeshFile navMeshFile, Vector3[][] polygons)
        {
            var area = new NavMeshArea();

            area.AreaId = binaryReader.ReadUInt32();
            area.DynamicAttributeFlags = (DynamicAttributeFlags)binaryReader.ReadInt32(); //this might actually be the next 4 bytes

            var unkBytes = binaryReader.ReadBytes(4); //TODO
            Debug.Assert(unkBytes[0] == 0);
            Debug.Assert(unkBytes[1] == 0);
            Debug.Assert(unkBytes[2] == 0);
            Debug.Assert(unkBytes[3] == 0);
            area.AgentLayer = binaryReader.ReadByte();

            if (navMeshFile.Version >= 35)
            {
                var polygonIndex = binaryReader.ReadUInt32();
                area.Corners = polygons[polygonIndex];
            }
            else
            {
                var cornerCount = binaryReader.ReadUInt32();

                area.Corners = new Vector3[cornerCount];
                for (var i = 0; i < cornerCount; i++)
                {
                    area.Corners[i] = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
                }
            }

            var unk1 = binaryReader.ReadUInt32();

            area.Connections = new NavMeshConnection[area.Corners.Length][];
            for (var i = 0; i < area.Corners.Length; i++)
            {
                area.Connections[i] = ReadConnections(binaryReader);
            }

            var unk2 = binaryReader.ReadByte(); //TODO
            Debug.Assert(unk2 == 0);
            var unk3 = binaryReader.ReadUInt32(); //TODO
            Debug.Assert(unk3 == 0);

            var ladderAboveCount = binaryReader.ReadUInt32();
            area.LaddersAbove = new uint[ladderAboveCount];
            for (var i = 0; i < ladderAboveCount; i++)
            {
                var ladderId = binaryReader.ReadUInt32();
                area.LaddersAbove[i] = ladderId;
            }

            var ladderBelowCount = binaryReader.ReadUInt32();
            area.LaddersBelow = new uint[ladderBelowCount];
            for (var i = 0; i < ladderBelowCount; i++)
            {
                var ladderId = binaryReader.ReadUInt32();
                area.LaddersBelow[i] = ladderId;
            }

            return area;
        }
    }
}
