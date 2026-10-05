using System.IO;
using System.Linq;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.NavMesh
{
    /// <summary>
    /// Represents a navigation flow map file (<c>.navflowmap</c>), a binary KV3 file that groups the areas of the
    /// navigation mesh into connected clusters per hull, with precomputed routes between them.
    /// </summary>
    public class NavFlowMapFile
    {
        /// <summary>
        /// Gets the file version.
        /// </summary>
        public int Version { get; private set; }

        /// <summary>
        /// Gets the cluster graph of each navigation mesh hull.
        /// </summary>
        public NavFlowMapHull[] Hulls { get; private set; } = [];

        /// <summary>
        /// Reads the flow map from a file.
        /// </summary>
        public void Read(string filename)
        {
            using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Read(fs);
        }

        /// <summary>
        /// Reads the flow map from a stream holding binary KV3.
        /// </summary>
        public void Read(Stream stream)
        {
            using var binaryReader = new BinaryReader(stream, Encoding.UTF8, true);

            var kv3 = new BinaryKV3(BlockType.Undefined)
            {
                Resource = null!,
                Offset = (uint)stream.Position,
                Size = (uint)(stream.Length - stream.Position),
            };
            kv3.Read(binaryReader);

            Read(kv3.Data);
        }

        /// <summary>
        /// Reads the flow map from its parsed KV3 root.
        /// </summary>
        public void Read(KVObject root)
        {
            Version = root.GetInt32Property("version");
            Hulls = [.. (root.GetArray("hulls") ?? []).Select(ReadHull)];
        }

        private static NavFlowMapHull ReadHull(KVObject hull) => new()
        {
            HullIndex = hull.GetInt32Property("hull_index"),
            Nodes = [.. (hull.GetArray("nodes") ?? []).Select(ReadNode)],
        };

        private static NavFlowMapNode ReadNode(KVObject node) => new()
        {
            Index = node.GetInt32Property("i"),
            Center = node.GetSubCollection("center").ToVector3(),
            AreaIds = [.. node.GetIntegerArray("nav_ids").Select(static id => (uint)id)],
            Connections = [.. (node.GetArray("connections") ?? []).Select(static connection => new NavFlowMapConnection
            {
                Cost = connection.GetFloatProperty("cost"),
                NodeIndex = connection.GetInt32Property("node_index"),
                AreaId = connection.GetUInt32Property("nav_id"),
            })],
            FlowMap = [.. node.GetIntegerArray("flow_map").Select(static entry => unchecked((ushort)entry))],
        };
    }

    /// <summary>
    /// The cluster graph of one navigation mesh hull in a <see cref="NavFlowMapFile"/>.
    /// </summary>
    public class NavFlowMapHull
    {
        /// <summary>
        /// Gets the navigation mesh hull index, matching <see cref="NavMeshArea.HullIndex"/>.
        /// </summary>
        public int HullIndex { get; init; }

        /// <summary>
        /// Gets the clusters of this hull, indexed by <see cref="NavFlowMapNode.Index"/>.
        /// </summary>
        public NavFlowMapNode[] Nodes { get; init; } = [];
    }

    /// <summary>
    /// A cluster of connected navigation mesh areas in a <see cref="NavFlowMapFile"/>.
    /// </summary>
    public class NavFlowMapNode
    {
        /// <summary>
        /// Gets the index of this cluster within its hull.
        /// </summary>
        public int Index { get; init; }

        /// <summary>
        /// Gets the centre of the cluster.
        /// </summary>
        public Vector3 Center { get; init; }

        /// <summary>
        /// Gets the <see cref="NavMeshArea.AreaId"/> of every navigation mesh area in this cluster.
        /// </summary>
        public uint[] AreaIds { get; init; } = [];

        /// <summary>
        /// Gets the connections to neighbouring clusters.
        /// </summary>
        public NavFlowMapConnection[] Connections { get; init; } = [];

        /// <summary>
        /// Gets the precomputed route entries. Each entry packs a cluster index in its upper 12 bits and a 4-bit
        /// value in its lower bits, where 15 means none.
        /// </summary>
        public ushort[] FlowMap { get; init; } = [];
    }

    /// <summary>
    /// A connection from a <see cref="NavFlowMapNode"/> to a neighbouring cluster.
    /// </summary>
    public class NavFlowMapConnection
    {
        /// <summary>
        /// Gets the travel cost to the neighbouring cluster.
        /// </summary>
        public float Cost { get; init; }

        /// <summary>
        /// Gets the <see cref="NavFlowMapNode.Index"/> of the neighbouring cluster.
        /// </summary>
        public int NodeIndex { get; init; }

        /// <summary>
        /// Gets the <see cref="NavMeshArea.AreaId"/> stored with this connection, usually an area of the neighbouring
        /// cluster.
        /// </summary>
        public uint AreaId { get; init; }
    }
}
