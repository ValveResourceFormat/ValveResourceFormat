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
        public uint[] Ladders { get; set; }

        private static NavMeshConnection[] ReadConnections(BinaryReader binaryReader)
        {
            var connectionCount = binaryReader.ReadUInt32();
            var connections = new NavMeshConnection[connectionCount];

            for (var i = 0; i < connectionCount; i++)
            {
                connections[i] = new NavMeshConnection(binaryReader);
            }

            return connections;
        }

        public static NavMeshArea Read(BinaryReader binaryReader, uint navFileVersion)
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
            var cornerCount = binaryReader.ReadUInt32(); //TODO (maybe corner count?)

            area.Corners = new Vector3[cornerCount];
            for (var i = 0; i < cornerCount; i++)
            {
                area.Corners[i] = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
            }

            var unk1 = binaryReader.ReadUInt32();

            area.Connections = new NavMeshConnection[cornerCount][];
            for (var i = 0; i < cornerCount; i++)
            {
                area.Connections[i] = ReadConnections(binaryReader);
            }

            var unk2 = binaryReader.ReadByte(); //TODO - probably hiding spot count?
            var unk3 = binaryReader.ReadUInt32(); //TODO - probably encounter path count?

            var ladderCount = binaryReader.ReadUInt32();
            area.Ladders = new uint[ladderCount];
            for (var i = 0; i < ladderCount; i++)
            {
                var ladderId = binaryReader.ReadUInt32();
                area.Ladders[i] = ladderId;
            }

            var unkBytes2 = binaryReader.ReadBytes(4); //TODO - probably hiding spots, approach areas, encounter paths, place data, ladder data, etc.

            for (var i = 0; i < unkBytes2.Length; i++)
            {
                Debug.Assert(unkBytes2[i] == 0);
            }

            return area;
        }
    }
}
