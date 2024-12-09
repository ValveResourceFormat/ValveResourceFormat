using System.Diagnostics;
using System.IO;
using System.Text;

namespace ValveResourceFormat.NavMesh
{
    public class NavMeshMetadata
    {
        public int UnkInt1 { get; set; }
        public int UnkInt2 { get; set; }
        public int UnkInt3 { get; set; }

        public float TileSize { get; set; }

        public float CellSize { get; set; }
        public float CellHeight { get; set; }

        public int MinRegionSize { get; set; }
        public int MergedRegionSize { get; set; }

        public float MeshSampleDistance { get; set; }
        public float MaxSampleError { get; set; }

        public int MaxEdgeLength { get; set; }
        public float MaxEdgeError { get; set; }
        public int VertsPerPoly { get; set; }

        public float SmallAreaOnEdgeRemoval { get; set; }

        public string UnkString1 { get; set; }
        public string UnkString2 { get; set; }
        public int UnkInt4 { get; set; }
        public byte UnkByte1 { get; set; }
        public float UnkFloat1 { get; set; }

        public float HumanHeight { get; set; }

        public byte UnkByte2 { get; set; }

        public float HalfHumanHeight { get; set; }
        public float HalfHumanWidth { get; set; }

        public int UnkInt5 { get; set; }
        public float UnkFloat2 { get; set; }
        public float UnkFloat3 { get; set; }
        public float UnkFloat4 { get; set; }
        public int UnkInt6 { get; set; }
        public byte UnkByte3 { get; set; }

        public void Read(BinaryReader binaryReader)
        {
            UnkInt1 = binaryReader.ReadInt32();
            UnkInt2 = binaryReader.ReadInt32();
            UnkInt3 = binaryReader.ReadInt32(); //count?

            //Tiles
            TileSize = binaryReader.ReadSingle();

            //Rasterization
            CellSize = binaryReader.ReadSingle();
            CellHeight = binaryReader.ReadSingle();

            //Region
            MinRegionSize = binaryReader.ReadInt32();
            MergedRegionSize = binaryReader.ReadInt32();

            //Detail Mesh
            MeshSampleDistance = binaryReader.ReadSingle();
            MaxSampleError = binaryReader.ReadSingle();

            //Polygonization
            MaxEdgeLength = binaryReader.ReadInt32();
            MaxEdgeError = binaryReader.ReadSingle();
            VertsPerPoly = binaryReader.ReadInt32();

            //Processing params
            SmallAreaOnEdgeRemoval = binaryReader.ReadSingle();

            UnkString1 = binaryReader.ReadNullTermString(Encoding.UTF8);
            UnkString2 = binaryReader.ReadNullTermString(Encoding.UTF8); //this might be a byte instead - only seen as a 0x00 byte

            UnkInt4 = binaryReader.ReadInt32();
            Debug.Assert(UnkInt4 == 1);
            UnkByte1 = binaryReader.ReadByte();
            Debug.Assert(UnkByte1 == 1);

            UnkFloat1 = binaryReader.ReadSingle(); //related to agent/human size - values seen so far are 15 and 16

            HumanHeight = binaryReader.ReadSingle(); //=71

            UnkByte2 = binaryReader.ReadByte();
            Debug.Assert(UnkByte2 == 0 || UnkByte2 == 1);

            HalfHumanHeight = binaryReader.ReadSingle(); //=35.5

            HalfHumanWidth = binaryReader.ReadSingle(); //=16-17.5

            UnkInt5 = binaryReader.ReadInt32(); //=50
            Debug.Assert(UnkInt5 == 50);
            UnkFloat2 = binaryReader.ReadSingle(); //=157
            //Debug.Assert(UnkFloat2 == 157f);
            UnkFloat3 = binaryReader.ReadSingle(); //=64
            //Debug.Assert(UnkFloat3 == 64f);
            UnkFloat4 = binaryReader.ReadSingle(); //=68
            //Debug.Assert(UnkFloat4 == 68f);

            UnkInt6 = binaryReader.ReadInt32(); //=0
            Debug.Assert(UnkInt6 == 0 || UnkInt6 == -1);
            UnkByte3 = binaryReader.ReadByte(); //=1
            Debug.Assert(UnkByte3 == 0 || UnkByte3 == 1);
        }
    }
}
