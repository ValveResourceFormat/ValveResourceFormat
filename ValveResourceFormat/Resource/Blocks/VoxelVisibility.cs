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

            // Bit 15 is always 0 (unused in engine)

            /// <summary>
            /// Gets the node index.
            /// </summary>
            // Game doesn't use NodeIndex even though visbuilder populates it
            public uint NodeIndex => (uint)((value >> 16) & 0xFFFFFF);

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

        private byte[]? pvsBuffer;

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

        //public Dictionary<ushort, List<(Vector3 Min, Vector3 Max)>> BuildClusterChildBounds_ViaRegionNodeIndex()
        //{
        //    var clusterChildren = new Dictionary<ushort, List<(Vector3 Min, Vector3 Max)>>();
        //
        //    if (Nodes.Length == 0)
        //    {
        //        return clusterChildren;
        //    }
        //
        //    var nodeBounds = BuildNodeBounds();
        //
        //    foreach (var region in Regions)
        //    {
        //        if (region.ClusterId >= BaseClusterCount
        //            || region.NodeIndex >= nodeBounds.Length
        //            || region.MaskIndex >= Masks.Length)
        //        {
        //            continue;
        //        }
        //
        //        var mask = Masks[region.MaskIndex];
        //        if (mask == 0)
        //        {
        //            continue;
        //        }
        //
        //        if (!clusterChildren.TryGetValue(region.ClusterId, out var list))
        //        {
        //            list = [];
        //            clusterChildren[region.ClusterId] = list;
        //        }
        //
        //        var (min, max) = nodeBounds[region.NodeIndex];
        //        var cellSize = (max.X - min.X) * 0.25f;
        //        UnpackMask(mask, min, cellSize, list);
        //    }
        //
        //    return clusterChildren;
        //}

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

            var halfGrid = GridSize * 0.5f + 0.03125f;
            var boxMin = position - new Vector3(halfGrid);
            var boxMax = position + new Vector3(halfGrid);

            Span<uint> bitfield = stackalloc uint[128];
            bitfield.Clear();
            QueryOctreeBox(0, MinBounds, MaxBounds, boxMin, boxMax, bitfield, halfGrid * 2f >= 1024f);

            for (var i = 0; i < 128; i++)
            {
                var word = bitfield[i];
                if (word != 0)
                {
                    return BitOperations.TrailingZeroCount(word) + 32 * i;
                }
            }

            return 0;
        }

        /// Native function fills the entire buffer with 0xFF instead of returning null
        public byte[]? GetPVSForPoint(Vector3 point)
        {
            if (Nodes.Length == 0 || PVSBytesPerCluster == 0)
            {
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
                    bitfield[(int)(clusterId >> 5)] |= 1u << (int)(clusterId & 0x1F);
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
                    bitfield[(int)(clusterId >> 5)] |= 1u << (int)(clusterId & 0x1F);
                }

                return;
            }

            var childBase = node.Offset;
            var midX = (nodeMin.X + nodeMax.X) * 0.5f;
            var midY = (nodeMin.Y + nodeMax.Y) * 0.5f;
            var midZ = (nodeMin.Z + nodeMax.Z) * 0.5f;

            var xLow = boxMin.X <= midX;
            var xHigh = boxMax.X >= midX;
            var yLow = boxMin.Y <= midY;
            var yHigh = boxMax.Y >= midY;
            var zLow = boxMin.Z <= midZ;
            var zHigh = boxMax.Z >= midZ;

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

            var xMin = Math.Clamp((int)((boxMin.X - leafMin.X) / cellSize), 0, 3);
            var xMax = Math.Clamp((int)((boxMax.X - leafMin.X) / cellSize), 0, 3);
            var yMin = Math.Clamp((int)((boxMin.Y - leafMin.Y) / cellSize), 0, 3);
            var yMax = Math.Clamp((int)((boxMax.Y - leafMin.Y) / cellSize), 0, 3);
            var zMin = Math.Clamp((int)((boxMin.Z - leafMin.Z) / cellSize), 0, 3);
            var zMax = Math.Clamp((int)((boxMax.Z - leafMin.Z) / cellSize), 0, 3);

            var xMask = (ulong)((1 << (xMax + 1)) - (1 << xMin)) * 0x1111111111111111UL;
            var yMask = ((1UL << (4 * (yMax + 1))) - (1UL << (4 * yMin))) * 0x0001000100010001UL;
            var zMask = (zMax >= 3 ? 0UL : 1UL << (16 * (zMax + 1))) - (1UL << (16 * zMin));

            return xMask & yMask & zMask;
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
