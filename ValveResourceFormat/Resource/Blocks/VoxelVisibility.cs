using System.Diagnostics;
using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "VXVS" block.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/worldrenderer/CVoxelVisibility">CVoxelVisibility</seealso>
    public class VoxelVisibility : Block
    {
        /// <inheritdoc/>
        public override BlockType Type => BlockType.VXVS;

        /// <summary>
        /// Gets the number of visibility clusters.
        /// </summary>
        public uint BaseClusterCount { get; private set; }

        /// <summary>
        /// Gets the number of PVS bytes per cluster.
        /// </summary>
        public uint PVSBytesPerCluster { get; private set; }

        /// <summary>
        /// Gets the minimum bounds of the octree.
        /// </summary>
        public Vector3 MinBounds { get; private set; }

        /// <summary>
        /// Gets the maximum bounds of the octree.
        /// </summary>
        public Vector3 MaxBounds { get; private set; }

        /// <summary>
        /// Gets the grid cell size.
        /// </summary>
        public float GridSize { get; private set; }

        /// <summary>
        /// Gets the cluster index used for sky visibility.
        /// </summary>
        public uint SkyVisibilityCluster { get; private set; }

        /// <summary>
        /// Gets the cluster index used for sun visibility.
        /// </summary>
        public uint SunVisibilityCluster { get; private set; }

        /// <summary>
        /// Represents an octree node.
        /// </summary>
        /// <param name="first">First packed word.</param>
        /// <param name="second">Second packed word.</param>
        public readonly struct Node(uint first, uint second)
        {
            /// <summary>
            /// Gets whether this node is a leaf.
            /// </summary>
            public bool IsLeaf => (first & 1) != 0;

            /// <summary>
            /// Gets the child offset for internal nodes, or the region offset for leaf nodes.
            /// </summary>
            public uint Offset => first >> 1;

            /// <summary>
            /// Gets the number of regions in this leaf.
            /// </summary>
            public byte RegionCount => (byte)second;

            /// <summary>
            /// Gets the enclosed cluster list index.
            /// </summary>
            public uint EnclosedListIndex => second >> 8;

            /// <summary>
            /// Gets whether this node has an enclosed cluster list.
            /// </summary>
            public bool IsEnclosedCluster => EnclosedListIndex != 0xFFFFFF;
        }

        /// <summary>
        /// Represents a region entry in a leaf node.
        /// </summary>
        /// <param name="value">Packed 64-bit region value.</param>
        public readonly struct Region(ulong value)
        {
            /// <summary>
            /// Gets the cluster id.
            /// </summary>
            public ushort ClusterId => (ushort)(value & 0x7FFF);

            /// <summary>
            /// Gets whether this region intersects geometry.
            /// </summary>
            public bool IsIntersectingGeo => ((value >> 15) & 0x1) != 0;

            /// <summary>
            /// Gets the leaf index.
            /// </summary>
            // Game doesn't use LeafIndex even though visbuilder populates it
            public uint LeafIndex => (uint)((value >> 16) & 0xFFFFFF);

            /// <summary>
            /// Gets the spatial mask index.
            /// </summary>
            public uint MaskIndex => (uint)(value >> 40);
        }

        /// <summary>
        /// Gets the octree nodes.
        /// </summary>
        public Node[] Nodes { get; private set; } = [];

        /// <summary>
        /// Gets the region entries.
        /// </summary>
        public Region[] Regions { get; private set; } = [];

        /// <summary>
        /// Gets the enclosed cluster list entries.
        /// </summary>
        public (int Offset, int Count)[] EnclosedClusterList { get; private set; } = [];

        /// <summary>
        /// Gets the enclosed cluster ids.
        /// </summary>
        public ushort[] EnclosedClusters { get; private set; } = [];

        /// <summary>
        /// Gets the spatial occupancy masks.
        /// </summary>
        public ulong[] Masks { get; private set; } = [];

        /// <summary>
        /// Gets the raw PVS bit table.
        /// </summary>
        public byte[] VisBlocks { get; private set; } = [];

        /// <summary>
        /// Gets whether this block holds visibility that can be queried. Files from before the octree
        /// format, and maps compiled without visibility, have none.
        /// </summary>
        public bool HasVisibilityData => BaseClusterCount > 0 && PVSBytesPerCluster > 0 && Nodes.Length > 0;

        /// <summary>
        /// Gets the visibility row listing the clusters sunlight reaches, empty when there is no sun row.
        /// Nothing outside it can be lit by the sun, so nothing outside it casts a sun shadow.
        /// </summary>
        public ReadOnlyMemory<byte> SunVisibility => GetVisibilityRow(SunVisibilityCluster);

        /// <summary>
        /// Gets the visibility row listing the clusters the sky reaches, empty when there is no sky row.
        /// </summary>
        public ReadOnlyMemory<byte> SkyVisibility => GetVisibilityRow(SkyVisibilityCluster);

        /// <summary>
        /// The maximum number of clusters a visibility query can report.
        /// </summary>
        public const int MaxClusters = 4096;

        /// <summary>
        /// The number of 32 bit words in a cluster bitfield.
        /// </summary>
        public const int ClusterBitfieldWords = MaxClusters / 32;

        // Queries grow by a fraction of a unit so a box flush against a cell boundary still reaches the far side
        private const float QueryEpsilon = 1f / 32f;

        // Above this extent in more than one axis a box walks too many leaves to be worth an exact answer
        private const float LargeBoxExtent = 1024f;

        private byte[]? pvsBuffer;

        private static readonly ulong[] SpatialMaskX = CreateAxisMasks(1);
        private static readonly ulong[] SpatialMaskY = CreateAxisMasks(4);
        private static readonly ulong[] SpatialMaskZ = CreateAxisMasks(16);

        private static ReadOnlySpan<byte> SubGridLevel1 => [0, 2, 8, 10, 32, 34, 40, 42];
        private static ReadOnlySpan<byte> SubGridLevel2 => [0, 1, 4, 5, 16, 17, 20, 21];

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            ArgumentNullException.ThrowIfNull(Resource);

            var dataBlock = Resource.DataBlock;
            if (dataBlock is not BinaryKV3 dataKv3)
            {
                throw new InvalidDataException("Tried to parse VXVS block, but DATA block is not KV3.");
            }

            var data = dataKv3.Data.Root;

            if (data.ContainsKey("m_clusters"))
            {
                return; // Older type of file
            }

            BaseClusterCount = data.GetUInt32Property("m_nBaseClusterCount");
            PVSBytesPerCluster = data.GetUInt32Property("m_nPVSBytesPerCluster");
            MinBounds = data.GetSubCollection("m_vMinBounds").ToVector3();
            MaxBounds = data.GetSubCollection("m_vMaxBounds").ToVector3();
            GridSize = data.GetFloatProperty("m_flGridSize");
            SkyVisibilityCluster = data.GetUInt32Property("m_nSkyVisibilityCluster");
            SunVisibilityCluster = data.GetUInt32Property("m_nSunVisibilityCluster");

            reader.BaseStream.Position = Offset;

            var count = ReadVisBlock(data, "m_NodeBlock", reader);
            Nodes = new Node[count];
            for (var i = 0; i < count; i++)
            {
                Nodes[i] = new Node(reader.ReadUInt32(), reader.ReadUInt32());
            }

            count = ReadVisBlock(data, "m_RegionBlock", reader);
            Regions = new Region[count];
            for (var i = 0; i < count; i++)
            {
                Regions[i] = new Region(reader.ReadUInt64());
            }

            count = ReadVisBlock(data, "m_EnclosedClusterListBlock", reader);
            EnclosedClusterList = new (int, int)[count];
            for (var i = 0; i < count; i++)
            {
                EnclosedClusterList[i] = (reader.ReadInt32(), reader.ReadInt32());
            }

            count = ReadVisBlock(data, "m_EnclosedClustersBlock", reader);
            EnclosedClusters = new ushort[count];
            for (var i = 0; i < count; i++)
            {
                EnclosedClusters[i] = reader.ReadUInt16();
            }

            count = ReadVisBlock(data, "m_MasksBlock", reader);
            Masks = new ulong[count];
            for (var i = 0; i < count; i++)
            {
                Masks[i] = reader.ReadUInt64();
            }

            count = ReadVisBlock(data, "m_nVisBlocks", reader);
            VisBlocks = reader.ReadBytes(count);
        }

        private int ReadVisBlock(KVObject data, string name, BinaryReader reader)
        {
            var block = data.GetSubCollection(name);
            var offset = block.GetIntegerProperty("m_nOffset");

            Debug.Assert(reader.BaseStream.Position == Offset + offset);
            reader.BaseStream.Position = Offset + offset;

            return block.GetInt32Property("m_nElementCount");
        }

        /// <summary>
        /// Gets the cluster id for a given world-space position.
        /// </summary>
        public int GetClusterForPosition(Vector3 position)
        {
            if (Nodes.Length == 0)
            {
                return 0;
            }

            var min = MinBounds;
            var max = MaxBounds;
            var leafIndex = FindLeafNode(position, ref min, ref max);

            if (leafIndex >= 0)
            {
                var node = Nodes[leafIndex];
                if (node.RegionCount > 0)
                {
                    var spatialMask = ComputeSpatialMask(position, min, max);
                    var regionStart = node.Offset;

                    for (uint r = 0; r < node.RegionCount; r++)
                    {
                        var regionIndex = regionStart + r;
                        if (regionIndex >= Regions.Length)
                        {
                            break;
                        }

                        var region = Regions[regionIndex];
                        if ((spatialMask & Masks[region.MaskIndex]) != 0)
                        {
                            return region.ClusterId;
                        }
                    }
                }
            }

            var halfGrid = new Vector3(GridSize * 0.5f);

            Span<uint> clusterBits = stackalloc uint[ClusterBitfieldWords];
            GetVisClustersForBox(position - halfGrid, position + halfGrid, clusterBits);

            for (var i = 0; i < ClusterBitfieldWords; i++)
            {
                var word = clusterBits[i];
                if (word != 0)
                {
                    return BitOperations.TrailingZeroCount(word) + 32 * i;
                }
            }

            return 0;
        }

        // The sky and sun rows are stored past the addressable clusters, so they are only reachable this way
        private ReadOnlyMemory<byte> GetVisibilityRow(uint cluster)
        {
            var offset = (long)cluster * PVSBytesPerCluster;

            if (PVSBytesPerCluster == 0 || offset + PVSBytesPerCluster > VisBlocks.Length)
            {
                return default;
            }

            return VisBlocks.AsMemory((int)offset, (int)PVSBytesPerCluster);
        }

        /// <summary>
        /// Fills a bitfield with every cluster the given box overlaps, one bit per cluster id.
        /// </summary>
        /// <param name="min">Minimum corner of the box.</param>
        /// <param name="max">Maximum corner of the box.</param>
        /// <param name="clusterBits">Destination bitfield, at least <see cref="ClusterBitfieldWords"/> words long.</param>
        /// <param name="exact">Visit every leaf rather than taking the precomputed cluster list of a fully enclosed octree node.</param>
        public void GetVisClustersForBox(Vector3 min, Vector3 max, Span<uint> clusterBits, bool exact = false)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(clusterBits.Length, ClusterBitfieldWords);

            clusterBits = clusterBits[..ClusterBitfieldWords];
            clusterBits.Clear();

            // Cluster 0 stands in wherever the octree cannot answer, and is a member of every pvs row
            if (Nodes.Length == 0)
            {
                clusterBits[0] = 1;
                return;
            }

            var center = (min + max) * 0.5f;
            var halfSize = Vector3.Max(max - center, Vector3.Zero) + new Vector3(QueryEpsilon);
            var queryMin = center - halfSize;
            var queryMax = center + halfSize;

            if (queryMin.X <= MinBounds.X && queryMin.Y <= MinBounds.Y && queryMin.Z <= MinBounds.Z
                && queryMax.X >= MaxBounds.X && queryMax.Y >= MaxBounds.Y && queryMax.Z >= MaxBounds.Z)
            {
                clusterBits[0] = 1;
                return;
            }

            var size = halfSize * 2f;
            var largeAxes = (size.X >= LargeBoxExtent ? 1 : 0)
                + (size.Y >= LargeBoxExtent ? 1 : 0)
                + (size.Z >= LargeBoxExtent ? 1 : 0);

            QueryOctreeBox(0, MinBounds, MaxBounds, queryMin, queryMax, clusterBits, !exact && largeAxes > 1);
        }

        /// <summary>
        /// Writes the id of every cluster the given box overlaps.
        /// </summary>
        /// <param name="min">Minimum corner of the box.</param>
        /// <param name="max">Maximum corner of the box.</param>
        /// <param name="clusters">
        /// Destination for the cluster ids. When more clusters overlap than fit, the result collapses to the
        /// catch-all cluster 0 instead of an arbitrary subset.
        /// </param>
        /// <param name="exact">Visit every leaf rather than taking the precomputed cluster list of a fully enclosed octree node.</param>
        /// <returns>The number of cluster ids written.</returns>
        public int GetVisClusterList(Vector3 min, Vector3 max, Span<ushort> clusters, bool exact = false)
        {
            Span<uint> clusterBits = stackalloc uint[ClusterBitfieldWords];
            GetVisClustersForBox(min, max, clusterBits, exact);

            var count = 0;

            for (var i = 0; i < ClusterBitfieldWords; i++)
            {
                var word = clusterBits[i];

                while (word != 0)
                {
                    if (count < clusters.Length)
                    {
                        clusters[count] = (ushort)(i * 32 + BitOperations.TrailingZeroCount(word));
                    }

                    count++;
                    word &= word - 1;
                }
            }

            if (count > clusters.Length)
            {
                clusters[0] = 0;
                return 1;
            }

            return count;
        }

        /// <summary>
        /// Gets the PVS bitfield for the cluster(s) at the given point, or <see langword="null"/> if there is none.
        /// </summary>
        public byte[]? GetPVSForPoint(Vector3 point)
        {
            if (Nodes.Length == 0 || PVSBytesPerCluster == 0)
            {
                // Native function fills the entire buffer with 0xFF instead of returning null
                return null;
            }

            var min = MinBounds;
            var max = MaxBounds;
            var leafIndex = FindLeafNode(point, ref min, ref max);
            if (leafIndex < 0 || Nodes[leafIndex].RegionCount == 0)
            {
                return null;
            }

            pvsBuffer ??= new byte[PVSBytesPerCluster];

            var node = Nodes[leafIndex];
            var spatialMask = ComputeSpatialMask(point, min, max);
            var regionCount = Math.Min(node.RegionCount, (uint)Regions.Length - node.Offset);

            var found = false;
            foreach (var region in Regions.AsSpan((int)node.Offset, (int)regionCount))
            {
                if ((spatialMask & Masks[region.MaskIndex]) == 0 || region.ClusterId >= BaseClusterCount)
                {
                    continue;
                }

                var row = VisBlocks.AsSpan((int)(region.ClusterId * PVSBytesPerCluster), (int)PVSBytesPerCluster);

                if (!found)
                {
                    row.CopyTo(pvsBuffer);
                    found = true;
                }
                else
                {
                    for (var i = 0; i < row.Length; i++)
                    {
                        pvsBuffer[i] |= row[i];
                    }
                }
            }

            return found ? pvsBuffer : null;
        }

        private void QueryOctreeBox(uint nodeIndex, Vector3 nodeMin, Vector3 nodeMax,
            Vector3 boxMin, Vector3 boxMax, Span<uint> bitfield, bool useEnclosedShortcut)
        {
            if (nodeIndex >= Nodes.Length)
            {
                return;
            }

            var node = Nodes[nodeIndex];

            if (useEnclosedShortcut && node.IsEnclosedCluster)
            {
                var (offset, count) = EnclosedClusterList[node.EnclosedListIndex];
                for (var i = 0; i < count; i++)
                {
                    var clusterId = (uint)EnclosedClusters[offset + i];
                    MathUtils.SetBit(bitfield, (int)clusterId);
                }

                return;
            }

            if (node.IsLeaf)
            {
                var boxSpatialMask = ComputeBoxSpatialMask(boxMin, boxMax, nodeMin, nodeMax);
                if (boxSpatialMask == 0)
                {
                    return;
                }

                var regionStart = node.Offset;
                for (uint r = 0; r < node.RegionCount; r++)
                {
                    var regionIndex = regionStart + r;
                    if (regionIndex >= Regions.Length)
                    {
                        break;
                    }

                    var region = Regions[regionIndex];
                    if ((boxSpatialMask & Masks[region.MaskIndex]) == 0)
                    {
                        continue;
                    }

                    var clusterId = (uint)region.ClusterId;
                    MathUtils.SetBit(bitfield, (int)clusterId);
                }

                return;
            }

            var childBase = node.Offset;
            var midX = (nodeMin.X + nodeMax.X) * 0.5f;
            var midY = (nodeMin.Y + nodeMax.Y) * 0.5f;
            var midZ = (nodeMin.Z + nodeMax.Z) * 0.5f;

            var xLow = nodeMin.X <= boxMax.X && boxMin.X <= midX;
            var xHigh = boxMin.X <= nodeMax.X && midX <= boxMax.X;
            var yLow = nodeMin.Y <= boxMax.Y && boxMin.Y <= midY;
            var yHigh = boxMin.Y <= nodeMax.Y && midY <= boxMax.Y;
            var zLow = nodeMin.Z <= boxMax.Z && boxMin.Z <= midZ;
            var zHigh = boxMin.Z <= nodeMax.Z && midZ <= boxMax.Z;

            for (uint octant = 0; octant < 8; octant++)
            {
                if (!((octant & 1) != 0 ? xHigh : xLow))
                {
                    continue;
                }

                if (!((octant & 2) != 0 ? yHigh : yLow))
                {
                    continue;
                }

                if (!((octant & 4) != 0 ? zHigh : zLow))
                {
                    continue;
                }

                var childMin = nodeMin;
                var childMax = nodeMax;
                if ((octant & 1) != 0) { childMin.X = midX; } else { childMax.X = midX; }
                if ((octant & 2) != 0) { childMin.Y = midY; } else { childMax.Y = midY; }
                if ((octant & 4) != 0) { childMin.Z = midZ; } else { childMax.Z = midZ; }

                QueryOctreeBox(childBase + octant, childMin, childMax, boxMin, boxMax, bitfield, useEnclosedShortcut);
            }
        }

        private int FindLeafNode(Vector3 point, ref Vector3 min, ref Vector3 max)
        {
            if (point.X < min.X || point.Y < min.Y || point.Z < min.Z || point.X > max.X || point.Y > max.Y || point.Z > max.Z)
            {
                return -1;
            }

            var nodeIndex = 0;

            if (Nodes[0].IsLeaf)
            {
                return 0;
            }

            while (true)
            {
                var node = Nodes[nodeIndex];
                var childBase = node.IsLeaf ? -1 : (int)node.Offset;

                var midX = (min.X + max.X) * 0.5f;
                var midY = (min.Y + max.Y) * 0.5f;
                var midZ = (min.Z + max.Z) * 0.5f;

                var octant = 0;
                if (midX < point.X)
                {
                    octant |= 1;
                }

                if (midY < point.Y)
                {
                    octant |= 2;
                }

                if (midZ < point.Z)
                {
                    octant |= 4;
                }

                nodeIndex = octant + childBase;

                if ((octant & 1) != 0) { min.X = midX; } else { max.X = midX; }
                if ((octant & 2) != 0) { min.Y = midY; } else { max.Y = midY; }
                if ((octant & 4) != 0) { min.Z = midZ; } else { max.Z = midZ; }

                if ((uint)nodeIndex >= Nodes.Length)
                {
                    return -1;
                }

                if (Nodes[nodeIndex].IsLeaf)
                {
                    return nodeIndex;
                }
            }
        }

        private static ulong ComputeSpatialMask(Vector3 point, Vector3 min, Vector3 max)
        {
            var midX = (min.X + max.X) * 0.5f;
            var midY = (min.Y + max.Y) * 0.5f;
            var midZ = (min.Z + max.Z) * 0.5f;

            var octant1 = 0;
            if (midX < point.X)
            {
                octant1 |= 1;
            }

            if (midY < point.Y)
            {
                octant1 |= 2;
            }

            if (midZ < point.Z)
            {
                octant1 |= 4;
            }

            if ((octant1 & 1) != 0) { min.X = midX; } else { max.X = midX; }
            if ((octant1 & 2) != 0) { min.Y = midY; } else { max.Y = midY; }
            if ((octant1 & 4) != 0) { min.Z = midZ; } else { max.Z = midZ; }

            var mid2X = (min.X + max.X) * 0.5f;
            var mid2Y = (min.Y + max.Y) * 0.5f;
            var mid2Z = (min.Z + max.Z) * 0.5f;

            var octant2 = 0;
            if (mid2X < point.X)
            {
                octant2 |= 1;
            }

            if (mid2Y < point.Y)
            {
                octant2 |= 2;
            }

            if (mid2Z < point.Z)
            {
                octant2 |= 4;
            }

            return 1UL << (SubGridLevel1[octant1] + SubGridLevel2[octant2]);
        }

        private static ulong ComputeBoxSpatialMask(Vector3 boxMin, Vector3 boxMax, Vector3 leafMin, Vector3 leafMax)
        {
            var cellSize = (leafMax.X - leafMin.X) * 0.25f;
            if (cellSize <= 0)
            {
                return 0;
            }

            var xMask = SpatialMaskX[OverlappedCells(boxMin.X, boxMax.X, leafMin.X, cellSize)];
            var yMask = SpatialMaskY[OverlappedCells(boxMin.Y, boxMax.Y, leafMin.Y, cellSize)];
            var zMask = SpatialMaskZ[OverlappedCells(boxMin.Z, boxMax.Z, leafMin.Z, cellSize)];

            return xMask & yMask & zMask;
        }

        /// <summary>
        /// Returns which of the four cells along one axis the box reaches into, as a four bit set.
        /// Cells are half open, so a box that only touches a cell edge does not enter it, and a box
        /// that misses the leaf entirely reaches nothing.
        /// </summary>
        private static int OverlappedCells(float boxMin, float boxMax, float leafMin, float cellSize)
        {
            var cells = 0;

            for (var cell = 0; cell < 4; cell++)
            {
                var low = leafMin + cellSize * cell;

                if (low < boxMax && boxMin < low + cellSize)
                {
                    cells |= 1 << cell;
                }
            }

            return cells;
        }

        /// <summary>
        /// Builds the 16 masks that turn a four bit set of cells on one axis into occupancy grid bits.
        /// Cell (x, y, z) sits at bit x + 4y + 16z, so a set on one axis repeats with that axis' stride.
        /// </summary>
        private static ulong[] CreateAxisMasks(int stride)
        {
            var masks = new ulong[16];
            var unit = (1UL << stride) - 1;

            for (var cells = 0; cells < masks.Length; cells++)
            {
                var mask = 0UL;

                for (var cell = 0; cell < 4; cell++)
                {
                    if ((cells & (1 << cell)) != 0)
                    {
                        mask |= unit << (cell * stride);
                    }
                }

                for (var shift = stride * 4; shift < 64; shift *= 2)
                {
                    mask |= mask << shift;
                }

                masks[cells] = mask;
            }

            return masks;
        }

        /// <summary>
        /// Builds a list of bounding boxes for each cluster.
        /// </summary>
        public Dictionary<ushort, List<(Vector3 Min, Vector3 Max)>> BuildClusterChildBounds()
        {
            var clusterChildren = new Dictionary<ushort, List<(Vector3 Min, Vector3 Max)>>();

            if (Nodes.Length == 0)
            {
                return clusterChildren;
            }

            var nodeBounds = BuildNodeBounds();

            for (var i = 0; i < Nodes.Length; i++)
            {
                var node = Nodes[i];
                if (!node.IsLeaf || node.RegionCount == 0)
                {
                    continue;
                }

                var regionStart = node.Offset;

                for (uint r = 0; r < node.RegionCount; r++)
                {
                    var regionIndex = regionStart + r;
                    if (regionIndex >= Regions.Length)
                    {
                        break;
                    }

                    var region = Regions[regionIndex];
                    if (region.ClusterId >= BaseClusterCount || region.MaskIndex >= Masks.Length)
                    {
                        continue;
                    }

                    var mask = Masks[region.MaskIndex];
                    if (mask == 0)
                    {
                        continue;
                    }

                    if (!clusterChildren.TryGetValue(region.ClusterId, out var list))
                    {
                        list = [];
                        clusterChildren[region.ClusterId] = list;
                    }

                    var (min, max) = nodeBounds[i];
                    var cellSize = (max.X - min.X) * 0.25f;
                    UnpackMask(mask, min, cellSize, list);
                }
            }

            return clusterChildren;
        }

        private (Vector3 Min, Vector3 Max)[] BuildNodeBounds()
        {
            var bounds = new (Vector3 Min, Vector3 Max)[Nodes.Length];
            ComputeNodeBounds(0, MinBounds, MaxBounds, bounds);
            return bounds;
        }

        private void ComputeNodeBounds(uint nodeIndex, Vector3 min, Vector3 max, (Vector3 Min, Vector3 Max)[] bounds)
        {
            if (nodeIndex >= Nodes.Length)
            {
                return;
            }

            bounds[nodeIndex] = (min, max);

            var node = Nodes[nodeIndex];
            if (node.IsLeaf)
            {
                return;
            }

            var childBase = node.Offset;
            var midX = (min.X + max.X) * 0.5f;
            var midY = (min.Y + max.Y) * 0.5f;
            var midZ = (min.Z + max.Z) * 0.5f;

            for (uint octant = 0; octant < 8; octant++)
            {
                var childMin = min;
                var childMax = max;
                if ((octant & 1) != 0) { childMin.X = midX; } else { childMax.X = midX; }
                if ((octant & 2) != 0) { childMin.Y = midY; } else { childMax.Y = midY; }
                if ((octant & 4) != 0) { childMin.Z = midZ; } else { childMax.Z = midZ; }

                ComputeNodeBounds(childBase + octant, childMin, childMax, bounds);
            }
        }

        private static void UnpackMask(ulong mask, Vector3 leafMin, float cellSize, List<(Vector3 Min, Vector3 Max)> output)
        {
            var remaining = mask;

            while (remaining != 0)
            {
                var startBit = BitOperations.TrailingZeroCount(remaining);
                var block = 1UL << startBit;

                for (var x = startBit & 3; x < 3; x++)
                {
                    var expanded = block | (block << 1);
                    if ((expanded & ~mask) != 0)
                    {
                        break;
                    }

                    block = expanded;
                }

                for (var y = (startBit >> 2) & 3; y < 3; y++)
                {
                    var expanded = block | (block << 4);
                    if ((expanded & ~mask) != 0)
                    {
                        break;
                    }

                    block = expanded;
                }

                for (var z = (startBit >> 4) & 3; z < 3; z++)
                {
                    var expanded = block | (block << 16);
                    if ((expanded & ~mask) != 0)
                    {
                        break;
                    }

                    block = expanded;
                }

                remaining &= ~block;

                var highBit = 63 - BitOperations.LeadingZeroCount(block);
                var minCorner = new Vector3(startBit & 3, (startBit >> 2) & 3, (startBit >> 4) & 3);
                var maxCorner = new Vector3((highBit & 3) + 1, ((highBit >> 2) & 3) + 1, ((highBit >> 4) & 3) + 1);

                output.Add((leafMin + minCorner * cellSize, leafMin + maxCorner * cellSize));
            }
        }

        /// <inheritdoc/>
        public override void Serialize(Stream stream)
        {
            throw new NotImplementedException("Serializing this block is not yet supported. If you need this, send us a pull request!");
        }

        /// <inheritdoc/>
        public override void WriteText(IndentedTextWriter writer)
        {
            if (Nodes.Length == 0)
            {
                if (BaseClusterCount == 0)
                {
                    writer.WriteLine("No voxel visibility data (older format or empty)");
                }
                return;
            }

            var enclosedNodeCount = Nodes.Count(n => n.IsEnclosedCluster);

            writer.WriteLine($"Nodes: {Nodes.Length} ({enclosedNodeCount} enclosed cluster nodes)");
            writer.WriteLine($"Regions: {Regions.Length}");
            writer.WriteLine($"Enclosed Cluster List Entries: {EnclosedClusterList.Length}");
            writer.WriteLine($"Enclosed Clusters: {EnclosedClusters.Length}");
            writer.WriteLine($"Masks: {Masks.Length}");
            writer.WriteLine($"Vis Block Bytes: {VisBlocks.Length}");
        }
    }
}
