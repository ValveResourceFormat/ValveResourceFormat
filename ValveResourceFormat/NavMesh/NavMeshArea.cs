using System.Diagnostics;
using System.IO;

namespace ValveResourceFormat.NavMesh
{
    /// <summary>
    /// Represents a navigation mesh area.
    /// </summary>
    public class NavMeshArea
    {
        /// <summary>
        /// Gets or sets the area identifier.
        /// </summary>
        public uint AreaId { get; set; }

        /// <summary>
        /// Gets or sets the hull index.
        /// </summary>
        public byte HullIndex { get; set; }

        /// <summary>
        /// Gets or sets the base attribute flags of this area.
        /// </summary>
        /// <remarks>
        /// Areas also have a dynamic attribute set, that one is a 32-bit runtime only value and is never stored in the file.
        /// The named values of <see cref="NavAttributeFlags"/> only apply to version 35 and newer.
        /// </remarks>
        public NavAttributeFlags AttributeFlags { get; set; }

        /// <summary>
        /// Gets or sets the id of the movable nav mesh this area belongs to,
        /// or <see cref="NavMeshFile.NoMovableMesh"/> when it belongs to the static world.
        /// </summary>
        public uint MovableMeshId { get; set; } = NavMeshFile.NoMovableMesh;

        /// <summary>
        /// Gets or sets the corner vertices.
        /// </summary>
        public Vector3[] Corners { get; set; } = [];

        /// <summary>
        /// Gets or sets the connections to other areas.
        /// </summary>
        public NavMeshConnection[][] Connections { get; set; } = [];

        /// <summary>
        /// Gets or sets the ladders above this area.
        /// </summary>
        public uint[] LaddersAbove { get; set; } = [];

        /// <summary>
        /// Gets or sets the ladders below this area.
        /// </summary>
        public uint[] LaddersBelow { get; set; } = [];

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

        /// <summary>
        /// Reads the navigation mesh area from a binary reader.
        /// </summary>
        public void Read(BinaryReader binaryReader, NavMeshFile navMeshFile, NavMeshPolygon[]? polygons = null)
        {
            AreaId = binaryReader.ReadUInt32();
            AttributeFlags = (NavAttributeFlags)binaryReader.ReadInt64();
            HullIndex = binaryReader.ReadByte();

            if (navMeshFile.Version >= 31)
            {
                if (polygons == null)
                {
                    throw new InvalidOperationException("Polygons array is required for version 31 or higher");
                }

                var polygonIndex = binaryReader.ReadUInt32();
                var polygon = polygons[polygonIndex];
                Corners = polygon.Corners;
                MovableMeshId = polygon.MovableMeshId;
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

            binaryReader.ReadSingle(); //almost always 0

            Connections = new NavMeshConnection[Corners.Length][];
            for (var i = 0; i < Corners.Length; i++)
            {
                Connections[i] = ReadConnections(binaryReader);
            }

            var unk2 = binaryReader.ReadByte(); //probably LegacyHidingSpotData count (always 0)
            Debug.Assert(unk2 == 0);
            var unk3 = binaryReader.ReadUInt32(); //probably LegacySpotEncounterData count (always 0)
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
