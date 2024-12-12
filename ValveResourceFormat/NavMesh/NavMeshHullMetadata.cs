using System.IO;

namespace ValveResourceFormat.NavMesh
{
    public class NavMeshHullMetadata
    {
        public bool AgentEnabled { get; set; } = true;
        public float AgentRadius { get; set; }

        public float AgentHeight { get; set; }

        public bool AgentShortHeightEnabled { get; set; }

        public float AgentShortHeight { get; set; }
        public float AgentMaxClimb { get; set; }

        public int AgentMaxSlope { get; set; }
        public float AgentMaxJumpDownDist { get; set; }
        public float AgentMaxJumpHorizDistBase { get; set; }
        public float AgentMaxJumpUpDist { get; set; }
        public int AgentBorderErosion { get; set; }

        public void Read(BinaryReader binaryReader, NavMeshFile navMeshFile)
        {
            if (navMeshFile.Version >= 35)
            {
                AgentEnabled = binaryReader.ReadByte() > 0;

                AgentRadius = binaryReader.ReadSingle();
                AgentHeight = binaryReader.ReadSingle();

                AgentShortHeightEnabled = binaryReader.ReadByte() > 0;
                AgentShortHeight = binaryReader.ReadSingle();

                AgentMaxClimb = binaryReader.ReadSingle();
                AgentMaxSlope = binaryReader.ReadInt32();

                AgentMaxJumpDownDist = binaryReader.ReadSingle();
                AgentMaxJumpHorizDistBase = binaryReader.ReadSingle();
                AgentMaxJumpUpDist = binaryReader.ReadSingle();

                AgentBorderErosion = binaryReader.ReadInt32();
            }
            else
            {
                AgentRadius = binaryReader.ReadSingle();
                AgentHeight = binaryReader.ReadSingle();

                AgentMaxClimb = binaryReader.ReadSingle();
                AgentMaxSlope = binaryReader.ReadInt32();

                AgentMaxJumpDownDist = binaryReader.ReadSingle(); 
                AgentMaxJumpHorizDistBase = binaryReader.ReadSingle();
                AgentMaxJumpUpDist = binaryReader.ReadSingle();
            }
        }
    }
}
