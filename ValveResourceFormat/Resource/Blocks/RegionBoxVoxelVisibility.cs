using System.IO;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// An octree whose leaves list boxes, each tagged with the cluster that owns it, so resolving a
    /// query is box overlap. The tree and the visibility rows are packed one after the other in the
    /// block payload.
    /// </summary>
    public sealed class RegionBoxVoxelVisibility : IWorldVisibility
    {
        /// <summary>Bytes the tree spends on each node, on top of the 8 byte node itself.</summary>
        private const int NodeExtraBytes = 12;

        private const int NodeBytes = 8;
        private const int RegionBytes = 28;

        /// <summary>
        /// The cluster id of a region no cluster claims. A third of the regions in some maps carry it.
        /// </summary>
        public const ushort NoCluster = 0xFFFF;

        /// <summary>
        /// Represents an octree node.
        /// </summary>
        /// <param name="packed">Packed first word: leaf flag and offset.</param>
        /// <param name="regionCount">Number of regions in this leaf.</param>
        public readonly struct Node(uint packed, ushort regionCount)
        {
            /// <summary>Gets whether this node is a leaf.</summary>
            public bool IsLeaf => (packed & 1) != 0;

            /// <summary>Gets the first child for internal nodes, or the first region for leaf nodes.</summary>
            public uint Offset => packed >> 1;

            /// <summary>Gets the number of regions in this leaf.</summary>
            public ushort RegionCount => regionCount;
        }

        /// <summary>
        /// Represents one box of space belonging to a cluster.
        /// </summary>
        /// <param name="bounds">The box this region covers.</param>
        /// <param name="clusterId">The cluster this box belongs to.</param>
        public readonly struct Region(AABB bounds, ushort clusterId)
        {
            /// <summary>Gets the box this region covers.</summary>
            public AABB Bounds => bounds;

            /// <summary>Gets the cluster this box belongs to, or <see cref="NoCluster"/> when none does.</summary>
            public ushort ClusterId => clusterId;

            /// <summary>Gets whether a cluster claims this box at all.</summary>
            public bool HasCluster => clusterId != NoCluster;
        }

        /// <summary>Gets the minimum bounds of the octree.</summary>
        public Vector3 MinBounds { get; private set; }

        /// <summary>Gets the maximum bounds of the octree.</summary>
        public Vector3 MaxBounds { get; private set; }

        /// <summary>Gets the grid cell size.</summary>
        public float GridSize { get; private set; }

        /// <summary>Gets the octree nodes.</summary>
        public Node[] Nodes { get; private set; } = [];

        /// <summary>Gets the region entries.</summary>
        public Region[] Regions { get; private set; } = [];

        /// <summary>Gets the number of clusters, excluding the sky row.</summary>
        public int ClusterCount { get; private set; }

        /// <summary>Gets the number of bytes in one cluster's visibility row.</summary>
        public int BytesPerCluster { get; private set; }

        /// <summary>Gets the raw visibility rows, one per cluster plus the sky row.</summary>
        public byte[] VisBlocks { get; private set; } = [];

        /// <summary>Gets the byte offset of each cluster's row within <see cref="VisBlocks"/>.</summary>
        public int[] ClusterRowOffsets { get; private set; } = [];

        /// <summary>Gets the byte offset of the sky row within <see cref="VisBlocks"/>, or -1 when absent.</summary>
        public int SkyRowOffset { get; private set; } = -1;

        /// <summary>
        /// Gets the byte offset of the sun row within <see cref="VisBlocks"/>, or -1 when the file
        /// carries no sun row.
        /// </summary>
        public int SunRowOffset { get; private set; } = -1;

        /// <summary>
        /// Gets whether the rows are stored uncompressed. Nothing can be read out of them when they are not.
        /// </summary>
        public bool IsRawPvs { get; private set; }

        /// <summary>
        /// Gets whether this holds visibility that can be queried.
        /// </summary>
        public bool HasVisibilityData => IsRawPvs && Nodes.Length > 0 && ClusterCount > 0 && BytesPerCluster > 0;

        /// <summary>
        /// Reads the tree and the visibility rows out of the block.
        /// </summary>
        /// <param name="data">The block's KV3 data.</param>
        /// <param name="reader">Reader positioned anywhere in the resource.</param>
        /// <param name="blockOffset">Byte offset of the block's payload in the resource.</param>
        public void Read(KVObject data, BinaryReader reader, long blockOffset)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(reader);

            MinBounds = data.GetSubCollection("m_vMinBounds").ToVector3();
            MaxBounds = data.GetSubCollection("m_vMaxBounds").ToVector3();
            GridSize = data.GetFloatProperty("m_flGridSize");
            IsRawPvs = data.GetStringProperty("m_nPVSCompression") is "VOXVIS_COMPRESS_RAW";

            var nodeCount = data.GetInt32Property("m_nNodeCount");
            var regionCount = data.GetInt32Property("m_nRegionCount");
            var treeSize = data.GetInt32Property("m_nTreeSize");
            var pvsSize = data.GetInt32Property("m_nPVSSizeCompressed");

            var clusters = data.GetArray("m_clusters");
            ClusterCount = clusters.Count;
            BytesPerCluster = (ClusterCount + 7) / 8;

            var blockOffsets = data.GetIntegerArray("m_blockOffset");

            ClusterRowOffsets = new int[ClusterCount];
            for (var i = 0; i < ClusterCount; i++)
            {
                ClusterRowOffsets[i] = RowOffset(clusters[i], blockOffsets);
            }

            if (data.ContainsKey("m_skyVisibilityCluster"))
            {
                SkyRowOffset = RowOffset(data.GetSubCollection("m_skyVisibilityCluster"), blockOffsets);
            }

            if (data.ContainsKey("m_sunVisibilityCluster"))
            {
                SunRowOffset = RowOffset(data.GetSubCollection("m_sunVisibilityCluster"), blockOffsets);
            }

            // The payload is the tree followed by the visibility rows
            if (nodeCount * (NodeBytes + NodeExtraBytes) + (long)regionCount * RegionBytes != treeSize)
            {
                throw new InvalidDataException($"Unexpected visibility tree size {treeSize} for {nodeCount} nodes and {regionCount} regions.");
            }

            reader.BaseStream.Position = blockOffset;

            Nodes = new Node[nodeCount];
            for (var i = 0; i < nodeCount; i++)
            {
                Nodes[i] = new Node(reader.ReadUInt32(), reader.ReadUInt16());
                reader.BaseStream.Position += 2;
            }

            // One vector per node whose meaning is not known; skipped rather than guessed at
            reader.BaseStream.Position += (long)nodeCount * NodeExtraBytes;

            Regions = new Region[regionCount];
            for (var i = 0; i < regionCount; i++)
            {
                var min = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                var max = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                reader.BaseStream.Position += 2;
                Regions[i] = new Region(new AABB(min, max), reader.ReadUInt16());
            }

            reader.BaseStream.Position = blockOffset + treeSize;
            VisBlocks = reader.ReadBytes(pvsSize);
        }

        private static int RowOffset(KVObject cluster, long[] blockOffsets)
        {
            var blockIndex = cluster.GetInt32Property("m_nBlockIndex");
            var offset = cluster.GetInt32Property("m_nOffsetIntoBlock");

            return (int)(blockIndex < blockOffsets.Length ? blockOffsets[blockIndex] : 0) + offset;
        }

        /// <summary>
        /// Gets the cluster containing the given point, or -1 when it is outside every region.
        /// </summary>
        /// <param name="position">World-space point.</param>
        public int GetClusterForPosition(Vector3 position)
        {
            var leaf = FindLeafNode(position);

            if (leaf < 0)
            {
                return -1;
            }

            var node = Nodes[leaf];

            foreach (var region in LeafRegions(node))
            {
                // Regions can name a row past the clusters, such as the sky one, which is not a cluster
                // anything can be looked up by, and a third of them may claim no cluster at all
                if (region.HasCluster && region.ClusterId < ClusterCount && region.Bounds.Contains(position))
                {
                    return region.ClusterId;
                }
            }

            return -1;
        }

        /// <summary>
        /// Gets the visibility row for a cluster, or an empty row when it has none.
        /// </summary>
        /// <param name="clusterId">Cluster to look up.</param>
        public ReadOnlyMemory<byte> GetVisibilityRow(int clusterId)
        {
            if (!HasVisibilityData || clusterId < 0 || clusterId >= ClusterCount)
            {
                return default;
            }

            return RowMemory(ClusterRowOffsets[clusterId]);
        }

        /// <summary>
        /// Gets the visibility row listing the clusters the sky reaches, empty when there is no sky row.
        /// </summary>
        public ReadOnlyMemory<byte> SkyVisibility => SkyRowOffset < 0 ? default : RowMemory(SkyRowOffset);

        /// <inheritdoc/>
        /// <remarks>Empty when the file carries no sun row, which leaves shadow casters uncullable.</remarks>
        public ReadOnlyMemory<byte> SunVisibility => SunRowOffset < 0 ? default : RowMemory(SunRowOffset);

        /// <inheritdoc/>
        public int ClusterBitfieldWordCount => MathUtils.DivideRoundUp(Math.Max(ClusterCount, 1), 32);

        /// <inheritdoc/>
        public ReadOnlyMemory<byte> GetVisibilityRowForPoint(Vector3 point)
        {
            var cluster = GetClusterForPosition(point);

            return cluster < 0 ? default : GetVisibilityRow(cluster);
        }

        /// <inheritdoc/>
        public Dictionary<ushort, List<(Vector3 Min, Vector3 Max)>> BuildClusterChildBounds()
        {
            var clusterChildren = new Dictionary<ushort, List<(Vector3 Min, Vector3 Max)>>();

            // Regions are already boxes here, so a cluster is just the ones that name it
            foreach (var region in Regions)
            {
                if (!region.HasCluster || region.ClusterId >= ClusterCount)
                {
                    continue;
                }

                if (!clusterChildren.TryGetValue(region.ClusterId, out var list))
                {
                    list = [];
                    clusterChildren[region.ClusterId] = list;
                }

                list.Add((region.Bounds.Min, region.Bounds.Max));
            }

            return clusterChildren;
        }

        private ReadOnlyMemory<byte> RowMemory(int offset)
            => offset < 0 || offset + BytesPerCluster > VisBlocks.Length
                ? default
                : VisBlocks.AsMemory(offset, BytesPerCluster);

        /// <summary>
        /// Fills a bitfield with every cluster the given box overlaps, one bit per cluster id.
        /// </summary>
        /// <param name="min">Minimum corner of the box.</param>
        /// <param name="max">Maximum corner of the box.</param>
        /// <param name="clusterBits">Destination bitfield, at least <see cref="ClusterCount"/> bits long.</param>
        public void GetVisClustersForBox(Vector3 min, Vector3 max, Span<uint> clusterBits)
        {
            clusterBits.Clear();

            if (Nodes.Length > 0)
            {
                QueryOctreeBox(0, new AABB(MinBounds, MaxBounds), new AABB(min, max), clusterBits);
            }
        }

        private void QueryOctreeBox(uint nodeIndex, AABB nodeBounds, AABB box, Span<uint> clusterBits)
        {
            if (nodeIndex >= Nodes.Length)
            {
                return;
            }

            var node = Nodes[nodeIndex];

            if (node.IsLeaf)
            {
                var capacity = clusterBits.Length * MathUtils.BitsPerWord;

                foreach (var region in LeafRegions(node))
                {
                    if (region.HasCluster && region.ClusterId < capacity && region.Bounds.Intersects(box))
                    {
                        MathUtils.SetBit(clusterBits, region.ClusterId);
                    }
                }

                return;
            }

            var mid = nodeBounds.Center;

            for (uint octant = 0; octant < 8; octant++)
            {
                var childMin = nodeBounds.Min;
                var childMax = nodeBounds.Max;

                if ((octant & 1) != 0) { childMin.X = mid.X; } else { childMax.X = mid.X; }
                if ((octant & 2) != 0) { childMin.Y = mid.Y; } else { childMax.Y = mid.Y; }
                if ((octant & 4) != 0) { childMin.Z = mid.Z; } else { childMax.Z = mid.Z; }

                var childBounds = new AABB(childMin, childMax);

                if (childBounds.Intersects(box))
                {
                    QueryOctreeBox(node.Offset + octant, childBounds, box, clusterBits);
                }
            }
        }

        private ReadOnlySpan<Region> LeafRegions(Node node)
        {
            var start = (int)node.Offset;

            if (start >= Regions.Length)
            {
                return [];
            }

            return Regions.AsSpan(start, Math.Min(node.RegionCount, Regions.Length - start));
        }

        private int FindLeafNode(Vector3 point)
        {
            if (Nodes.Length == 0 || !new AABB(MinBounds, MaxBounds).Contains(point))
            {
                return -1;
            }

            var nodeIndex = 0;
            var min = MinBounds;
            var max = MaxBounds;

            while (!Nodes[nodeIndex].IsLeaf)
            {
                var mid = (min + max) * 0.5f;
                var octant = 0;

                if (mid.X < point.X) { octant |= 1; min.X = mid.X; } else { max.X = mid.X; }
                if (mid.Y < point.Y) { octant |= 2; min.Y = mid.Y; } else { max.Y = mid.Y; }
                if (mid.Z < point.Z) { octant |= 4; min.Z = mid.Z; } else { max.Z = mid.Z; }

                nodeIndex = (int)Nodes[nodeIndex].Offset + octant;

                if ((uint)nodeIndex >= Nodes.Length)
                {
                    return -1;
                }
            }

            return nodeIndex;
        }
    }
}
