using System.IO;

namespace ValveResourceFormat.NavMesh
{
    public class NavMeshGenerationHullParams
    {
        public bool Enabled { get; set; } = true;
        public float Radius { get; set; }

        public float Height { get; set; }

        public bool ShortHeightEnabled { get; set; }

        public float ShortHeight { get; set; }
        public float MaxClimb { get; set; }

        public int MaxSlope { get; set; }
        public float MaxJumpDownDist { get; set; }
        public float MaxJumpHorizDistBase { get; set; }
        public float MaxJumpUpDist { get; set; }
        public int BorderErosion { get; set; }

        public void Read(BinaryReader binaryReader, NavMeshFile navMeshFile)
        {
            if (navMeshFile.Version >= 31)
            {
                Enabled = binaryReader.ReadByte() > 0;
            }

            Radius = binaryReader.ReadSingle();
            Height = binaryReader.ReadSingle();
            if (navMeshFile.Version >= 31)
            {
                ShortHeightEnabled = binaryReader.ReadByte() > 0;
                ShortHeight = binaryReader.ReadSingle();
            }

            MaxClimb = binaryReader.ReadSingle();
            MaxSlope = binaryReader.ReadInt32();

            MaxJumpDownDist = binaryReader.ReadSingle();
            MaxJumpHorizDistBase = binaryReader.ReadSingle();
            MaxJumpUpDist = binaryReader.ReadSingle();

            if (navMeshFile.Version >= 35)
            {
                BorderErosion = binaryReader.ReadInt32();
            }
        }
    }
}
