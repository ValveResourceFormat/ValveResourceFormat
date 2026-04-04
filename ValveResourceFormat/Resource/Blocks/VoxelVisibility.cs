using System.Diagnostics;
using System.IO;
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

            if (data.Root.ContainsKey("m_clusters"))
            {
                return; // Older type of file
            }

            reader.BaseStream.Position = Offset;

            // Maybe its not reading ints, but rather based on these:
            // m_nBaseClusterCount = 2
            // m_nPVSBytesPerCluster = 4

            // NodeBlock
            var block = data["m_NodeBlock"];
            var offset = block.GetIntegerProperty("m_nOffset");
            var count = block.GetUnsignedIntegerProperty("m_nElementCount");
            var nodeBlocks = new (int, int)[count];

            Debug.Assert(offset == 0);

            reader.BaseStream.Position = Offset + offset;

            for (var i = 0u; i < count; i++)
            {
                nodeBlocks[i] = (reader.ReadInt32(), reader.ReadInt32());
            }

            // RegionBlock
            block = data["m_RegionBlock"];
            offset = block.GetIntegerProperty("m_nOffset");
            count = block.GetUnsignedIntegerProperty("m_nElementCount");
            var regionBlocks = new (int, int)[count];

            Debug.Assert(reader.BaseStream.Position == Offset + offset);

            reader.BaseStream.Position = Offset + offset;

            for (var i = 0u; i < count; i++)
            {
                regionBlocks[i] = (reader.ReadInt32(), reader.ReadInt32());
            }

            // EnclosedClusterListBlock
            block = data["m_EnclosedClusterListBlock"];
            offset = block.GetIntegerProperty("m_nOffset");
            count = block.GetUnsignedIntegerProperty("m_nElementCount");
            var enclosedClusterList = new (int, int)[count];

            Debug.Assert(reader.BaseStream.Position == Offset + offset);

            reader.BaseStream.Position = Offset + offset;

            for (var i = 0u; i < count; i++)
            {
                enclosedClusterList[i] = (reader.ReadInt32(), reader.ReadInt32());
            }

            // EnclosedClustersBlock
            block = data["m_EnclosedClustersBlock"];
            offset = block.GetIntegerProperty("m_nOffset");
            count = block.GetUnsignedIntegerProperty("m_nElementCount");
            var clustersData = new short[count];

            Debug.Assert(reader.BaseStream.Position == Offset + offset);

            reader.BaseStream.Position = Offset + offset;

            for (var i = 0u; i < count; i++)
            {
                clustersData[i] = reader.ReadInt16();
            }

            // MasksBlock
            block = data["m_MasksBlock"];
            offset = block.GetIntegerProperty("m_nOffset");
            count = block.GetUnsignedIntegerProperty("m_nElementCount");
            var masksBlocks = new (int, int)[count];

            Debug.Assert(reader.BaseStream.Position == Offset + offset);

            reader.BaseStream.Position = Offset + offset;

            for (var i = 0u; i < count; i++)
            {
                masksBlocks[i] = (reader.ReadInt32(), reader.ReadInt32());
            }

            // VisBlocks
            block = data["m_nVisBlocks"];
            offset = block.GetIntegerProperty("m_nOffset");
            count = block.GetUnsignedIntegerProperty("m_nElementCount");
            var visBlocksData = new byte[count];

            Debug.Assert(reader.BaseStream.Position == Offset + offset);

            reader.BaseStream.Position = Offset + offset;

            for (var i = 0u; i < count; i++)
            {
                visBlocksData[i] = reader.ReadByte();
            }

            Debug.Assert(reader.BaseStream.Position == Offset + Size);
        }

        /// <inheritdoc/>
        public override void Serialize(Stream stream)
        {
            throw new NotImplementedException("Serializing this block is not yet supported. If you need this, send us a pull request!");
        }

        /// <inheritdoc/>
        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("Parsing world visibility is not implemented. If you're up to the task, try to reverse engineer it!");
        }
    }
}
