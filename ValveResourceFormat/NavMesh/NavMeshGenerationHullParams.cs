using System.IO;

namespace ValveResourceFormat.NavMesh
{
    /// <summary>
    /// Hull generation parameters for navigation meshes.
    /// </summary>
    public class NavMeshGenerationHullParams
    {
        /// <summary>
        /// Gets or sets whether the hull is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the hull radius.
        /// </summary>
        public float Radius { get; set; }

        /// <summary>
        /// Gets or sets the hull height.
        /// </summary>
        public float Height { get; set; }

        /// <summary>
        /// Gets or sets whether short height is enabled.
        /// </summary>
        public bool ShortHeightEnabled { get; set; }

        /// <summary>
        /// Gets or sets the short height value.
        /// </summary>
        public float ShortHeight { get; set; }

        /// <summary>
        /// Gets or sets the maximum climb height.
        /// </summary>
        public float MaxClimb { get; set; }

        /// <summary>
        /// Gets or sets the maximum slope.
        /// </summary>
        public int MaxSlope { get; set; }

        /// <summary>
        /// Gets or sets the maximum jump down distance.
        /// </summary>
        public float MaxJumpDownDist { get; set; }

        /// <summary>
        /// Gets or sets the maximum horizontal jump distance base.
        /// </summary>
        public float MaxJumpHorizDistBase { get; set; }

        /// <summary>
        /// Gets or sets the maximum jump up distance.
        /// </summary>
        public float MaxJumpUpDist { get; set; }

        /// <summary>
        /// Gets or sets the border erosion value.
        /// </summary>
        public int BorderErosion { get; set; }

        /// <summary>
        /// Reads hull parameters from a binary reader.
        /// </summary>
        public void Read(BinaryReader binaryReader, NavMeshGenerationParams generationParams)
        {
            if (generationParams.NavGenVersion >= 9)
            {
                Enabled = binaryReader.ReadByte() > 0;
            }

            Radius = binaryReader.ReadSingle();
            Height = binaryReader.ReadSingle();
            if (generationParams.NavGenVersion >= 9)
            {
                ShortHeightEnabled = binaryReader.ReadByte() > 0;
                ShortHeight = binaryReader.ReadSingle();
            }

            if (generationParams.NavGenVersion >= 13)
            {
                //TODO: Figure out the two values below
                binaryReader.ReadByte();
                binaryReader.ReadSingle();
            }

            MaxClimb = binaryReader.ReadSingle();
            MaxSlope = binaryReader.ReadInt32();

            MaxJumpDownDist = binaryReader.ReadSingle();
            MaxJumpHorizDistBase = binaryReader.ReadSingle();
            MaxJumpUpDist = binaryReader.ReadSingle();

            if (generationParams.NavGenVersion >= 11)
            {
                BorderErosion = binaryReader.ReadInt32();
            }
        }
    }
}
