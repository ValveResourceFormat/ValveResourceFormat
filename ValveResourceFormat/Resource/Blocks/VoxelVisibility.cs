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

        public uint BaseClusterCount { get; private set; }
        public uint PVSBytesPerCluster { get; private set; }
        public Vector3 MinBounds { get; private set; }
        public Vector3 MaxBounds { get; private set; }
        public float GridSize { get; private set; }
        public uint SkyVisibilityCluster { get; private set; }
        public uint SunVisibilityCluster { get; private set; }

        public readonly struct Node(uint first, uint second)
        {
            public bool IsLeaf => (first & 1) != 0;
            public uint Offset => first >> 1;

            public byte RegionCount => (byte)second;
            public uint EnclosedListIndex => second >> 8;
            public bool IsEnclosedCluster => EnclosedListIndex != 0xFFFFFF;
        }

        public readonly struct Region(ulong value)
        {
            public ushort ClusterId => (ushort)(value & 0x7FFF);

            // Bit 15 is always 0 (unused in engine)

            // Game doesn't use NodeIndex even though visbuilder populates it
            public uint NodeIndex => (uint)((value >> 16) & 0xFFFFFF);
            public uint MaskIndex => (uint)(value >> 40);
        }

        public Node[] Nodes { get; private set; } = [];
        public Region[] Regions { get; private set; } = [];
        public (int Offset, int Count)[] EnclosedClusterList { get; private set; } = [];
        public ushort[] EnclosedClusters { get; private set; } = [];
        public ulong[] Masks { get; private set; } = [];
        public byte[] VisBlocks { get; private set; } = [];

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            ArgumentNullException.ThrowIfNull(Resource);

            var dataBlock = Resource.DataBlock;
            if (dataBlock is not BinaryKV3 dataKv3)
            {
                throw new InvalidDataException("Tried to parse VXVS block, but DATA block is not KV3.");
            }

            var data = dataKv3.Data;

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
