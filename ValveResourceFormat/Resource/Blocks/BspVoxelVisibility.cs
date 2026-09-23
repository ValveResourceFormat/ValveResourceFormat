using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// A binary tree of split planes where a leaf is a cluster outright, named by a child index past
    /// the end of the nodes.
    /// </summary>
    public sealed class BspVoxelVisibility : IWorldVisibility
    {
        /// <summary>
        /// Represents one split plane and the two subtrees either side of it.
        /// </summary>
        /// <param name="normal">Plane normal.</param>
        /// <param name="distance">Plane distance along the normal.</param>
        /// <param name="front">Child on the positive side.</param>
        /// <param name="back">Child on the negative side.</param>
        public readonly struct Node(Vector3 normal, float distance, int front, int back)
        {
            /// <summary>Gets the plane normal.</summary>
            public Vector3 Normal => normal;

            /// <summary>Gets the plane distance along the normal.</summary>
            public float Distance => distance;

            /// <summary>Gets the child on the positive side of the plane.</summary>
            public int Front => front;

            /// <summary>Gets the child on the negative side of the plane.</summary>
            public int Back => back;

            /// <summary>Gets how far a point sits in front of the plane.</summary>
            /// <param name="point">Point to measure.</param>
            public float DistanceTo(Vector3 point) => Vector3.Dot(normal, point) - distance;
        }

        /// <summary>
        /// Represents one cluster.
        /// </summary>
        /// <param name="bounds">The box this cluster covers, empty when the map left it unset.</param>
        /// <param name="rowOffset">Byte offset of this cluster's visibility row.</param>
        /// <param name="nearVisibleDistance">Distance this cluster starts being drawn at.</param>
        /// <param name="farVisibleDistance">Distance this cluster stops being drawn at.</param>
        public readonly struct Cluster(AABB bounds, int rowOffset, float nearVisibleDistance, float farVisibleDistance)
        {
            /// <summary>Gets the box this cluster covers.</summary>
            public AABB Bounds => bounds;

            /// <summary>Gets the byte offset of this cluster's visibility row.</summary>
            public int RowOffset => rowOffset;

            /// <summary>Gets the distance this cluster starts being drawn at.</summary>
            public float NearVisibleDistance => nearVisibleDistance;

            /// <summary>
            /// Gets the distance this cluster stops being drawn at. Later formats dropped this.
            /// </summary>
            public float FarVisibleDistance => farVisibleDistance;

            /// <summary>Gets whether this cluster was given a real box. Most are not.</summary>
            public bool HasBounds => bounds.Size != Vector3.Zero;
        }

        /// <summary>Gets the split plane tree. The root is index 0.</summary>
        public Node[] Nodes { get; private set; } = [];

        /// <summary>Gets the clusters, which are this tree's leaves.</summary>
        public Cluster[] Clusters { get; private set; } = [];

        /// <summary>Gets the visibility rows, addressed by <see cref="Cluster.RowOffset"/>.</summary>
        public byte[] VisData { get; private set; } = [];

        /// <inheritdoc/>
        public Vector3 MinBounds { get; private set; }

        /// <inheritdoc/>
        public Vector3 MaxBounds { get; private set; }

        /// <inheritdoc/>
        public int ClusterCount => Clusters.Length;

        /// <summary>Gets the number of bytes in one cluster's visibility row.</summary>
        public int BytesPerCluster { get; private set; }

        /// <inheritdoc/>
        public int ClusterBitfieldWordCount => MathUtils.DivideRoundUp(Math.Max(ClusterCount, 1), MathUtils.BitsPerWord);

        /// <inheritdoc/>
        public bool HasVisibilityData => Nodes.Length > 0 && ClusterCount > 0 && BytesPerCluster > 0 && VisData.Length > 0;

        /// <inheritdoc/>
        /// <remarks>This format has no sun row.</remarks>
        public ReadOnlyMemory<byte> SunVisibility => default;

        /// <summary>
        /// Reads the layout out of the block's key values.
        /// </summary>
        /// <param name="data">The block's KV3 data.</param>
        public void Read(KVObject data)
        {
            ArgumentNullException.ThrowIfNull(data);

            var clusters = data.GetArray("m_clusters");
            var nodes = data.GetArray("m_nodes");

            Clusters = new Cluster[clusters.Count];
            BytesPerCluster = (clusters.Count + 7) / 8;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            for (var i = 0; i < clusters.Count; i++)
            {
                var cluster = clusters[i];
                var bounds = new AABB(
                    cluster.GetSubCollection("m_vMins").ToVector3(),
                    cluster.GetSubCollection("m_vMaxs").ToVector3());

                Clusters[i] = new Cluster(
                    bounds,
                    cluster.GetInt32Property("m_nData"),
                    cluster.GetFloatProperty("m_flNearVisibleDistance"),
                    cluster.GetFloatProperty("m_flFarVisibleDistance"));

                if (Clusters[i].HasBounds)
                {
                    min = Vector3.Min(min, bounds.Min);
                    max = Vector3.Max(max, bounds.Max);
                }
            }

            // Most clusters ship without a box, so the volume is whatever the ones that have them cover
            if (min.X <= max.X)
            {
                MinBounds = min;
                MaxBounds = max;
            }

            Nodes = new Node[nodes.Count];

            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var children = node.GetIntegerArray("m_nChildren");

                Nodes[i] = new Node(
                    node.GetSubCollection("m_vSplitNormal").ToVector3(),
                    node.GetFloatProperty("m_flDist"),
                    children.Length > 0 ? (int)children[0] : 0,
                    children.Length > 1 ? (int)children[1] : 0);
            }

            var dwords = data.GetIntegerArray("m_visDataDwords");
            VisData = new byte[dwords.Length * sizeof(uint)];

            for (var i = 0; i < dwords.Length; i++)
            {
                BitConverter.TryWriteBytes(VisData.AsSpan(i * sizeof(uint)), (uint)dwords[i]);
            }
        }

        /// <summary>
        /// Resolves a child reference, which names a cluster once it runs past the end of the nodes.
        /// </summary>
        /// <param name="child">Child reference out of a node.</param>
        /// <param name="cluster">The cluster it names, when it names one.</param>
        /// <returns>Whether the reference is a cluster rather than another node.</returns>
        private bool IsCluster(int child, out int cluster)
        {
            cluster = child - Nodes.Length;
            return cluster >= 0;
        }

        /// <inheritdoc/>
        public int GetClusterForPosition(Vector3 position)
        {
            if (Nodes.Length == 0)
            {
                return -1;
            }

            var index = 0;

            while (true)
            {
                var node = Nodes[index];
                var child = node.DistanceTo(position) >= 0f ? node.Front : node.Back;

                if (IsCluster(child, out var cluster))
                {
                    return cluster < ClusterCount ? cluster : -1;
                }

                if ((uint)child >= (uint)Nodes.Length)
                {
                    return -1;
                }

                index = child;
            }
        }

        /// <inheritdoc/>
        public ReadOnlyMemory<byte> GetVisibilityRowForPoint(Vector3 point)
        {
            var cluster = GetClusterForPosition(point);

            return cluster < 0 ? default : GetVisibilityRow(cluster);
        }

        /// <summary>
        /// Gets the visibility row for a cluster, or empty when it has none.
        /// </summary>
        /// <param name="clusterId">Cluster to look up.</param>
        public ReadOnlyMemory<byte> GetVisibilityRow(int clusterId)
        {
            if (clusterId < 0 || clusterId >= ClusterCount)
            {
                return default;
            }

            var offset = Clusters[clusterId].RowOffset;

            return offset < 0 || offset + BytesPerCluster > VisData.Length
                ? default
                : VisData.AsMemory(offset, BytesPerCluster);
        }

        /// <inheritdoc/>
        public void GetVisClustersForBox(Vector3 min, Vector3 max, Span<uint> clusterBits)
        {
            clusterBits.Clear();

            if (Nodes.Length > 0)
            {
                Descend(0, new AABB(min, max), clusterBits);
            }
        }

        private void Descend(int index, AABB box, Span<uint> clusterBits)
        {
            if ((uint)index >= (uint)Nodes.Length)
            {
                return;
            }

            var node = Nodes[index];

            // The corner furthest along the normal is the last part of the box to leave the front side,
            // and its opposite the last to leave the back. Written out rather than derived from a centre
            // and extent, because these planes sit flush on box faces and rounding decides the result
            var front = new Vector3(
                node.Normal.X < 0f ? box.Min.X : box.Max.X,
                node.Normal.Y < 0f ? box.Min.Y : box.Max.Y,
                node.Normal.Z < 0f ? box.Min.Z : box.Max.Z);

            if (node.DistanceTo(front) >= 0f)
            {
                Visit(node.Front, box, clusterBits);
            }

            if (node.DistanceTo(box.Min + box.Max - front) <= 0f)
            {
                Visit(node.Back, box, clusterBits);
            }
        }

        private void Visit(int child, AABB box, Span<uint> clusterBits)
        {
            if (IsCluster(child, out var cluster))
            {
                SetCluster(cluster, clusterBits);
                return;
            }

            Descend(child, box, clusterBits);
        }

        private void SetCluster(int cluster, Span<uint> clusterBits)
        {
            if (cluster >= 0 && cluster < ClusterCount && cluster < clusterBits.Length * MathUtils.BitsPerWord)
            {
                MathUtils.SetBit(clusterBits, cluster);
            }
        }

        /// <inheritdoc/>
        public Dictionary<ushort, List<(Vector3 Min, Vector3 Max)>> BuildClusterChildBounds()
        {
            var clusterChildren = new Dictionary<ushort, List<(Vector3 Min, Vector3 Max)>>();

            // Only the clusters the map gave a box to can be drawn; most have none
            for (var i = 0; i < Clusters.Length; i++)
            {
                if (Clusters[i].HasBounds)
                {
                    clusterChildren[(ushort)i] = [(Clusters[i].Bounds.Min, Clusters[i].Bounds.Max)];
                }
            }

            return clusterChildren;
        }
    }
}
