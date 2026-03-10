using System.IO;

namespace ValveResourceFormat.NavMesh
{
    /// <summary>
    /// Per-hull navigation agent generation parameters.
    /// Corresponds to <c>CNavHullVData</c>.
    /// </summary>
    public class NavMeshGenerationHullParams
    {
        /// <summary>
        /// Gets or sets whether this agent is enabled for generation.
        /// When disabled, zero nav areas will be produced for this agent.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the radius of the navigating agent capsule.
        /// </summary>
        public float Radius { get; set; }

        /// <summary>
        /// Gets or sets the height of the navigating agent capsule.
        /// </summary>
        public float Height { get; set; }

        /// <summary>
        /// Gets or sets whether shorter (crouch) navigating agent capsules are enabled in addition to regular height capsules.
        /// </summary>
        public bool ShortHeightEnabled { get; set; }

        /// <summary>
        /// Gets or sets the crouch height of the navigating agent capsule when <see cref="ShortHeightEnabled"/> is <see langword="true"/>.
        /// </summary>
        public float ShortHeight { get; set; }

        /// <summary>
        /// Gets or sets the maximum vertical offset that the agent simply ignores and walks over.
        /// </summary>
        public float MaxClimb { get; set; }

        /// <summary>
        /// Gets or sets the maximum ground slope (in degrees) that is considered walkable.
        /// </summary>
        public int MaxSlope { get; set; }

        /// <summary>
        /// Gets or sets the maximum vertical offset at which to create a jump connection (possibly one-way).
        /// </summary>
        public float MaxJumpDownDist { get; set; }

        /// <summary>
        /// Gets or sets the maximum horizontal offset over which to create a jump connection.
        /// This is a parameter into the true threshold function rather than a direct distance.
        /// </summary>
        public float MaxJumpHorizDistBase { get; set; }

        /// <summary>
        /// Gets or sets the maximum vertical offset at which to make a jump connection two-way.
        /// </summary>
        public float MaxJumpUpDist { get; set; }

        /// <summary>
        /// Gets or sets the border erosion in voxel units.
        /// A value of -1 uses the default value based on the agent radius.
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
