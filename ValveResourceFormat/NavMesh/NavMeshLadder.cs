using System.IO;

namespace ValveResourceFormat.NavMesh
{
    /// <summary>
    /// Represents a ladder in a navigation mesh.
    /// </summary>
    public class NavMeshLadder
    {
        /// <summary>
        /// Gets or sets the ladder identifier.
        /// </summary>
        public uint Id { get; set; }

        /// <summary>
        /// Gets or sets the ladder width.
        /// </summary>
        public float Width { get; set; }

        /// <summary>
        /// Gets or sets the ladder length.
        /// </summary>
        public float Length { get; set; }

        /// <summary>
        /// Gets or sets the top position.
        /// </summary>
        public Vector3 Top { get; set; }

        /// <summary>
        /// Gets or sets the bottom position.
        /// </summary>
        public Vector3 Bottom { get; set; }

        /// <summary>
        /// Gets or sets the ladder direction.
        /// </summary>
        public NavDirectionType Direction { get; set; }

        /// <summary>
        /// Gets or sets the top forward area.
        /// </summary>
        public NavMeshArea? TopForwardArea { get; set; }

        /// <summary>
        /// Gets or sets the top left area.
        /// </summary>
        public NavMeshArea? TopLeftArea { get; set; }

        /// <summary>
        /// Gets or sets the top right area.
        /// </summary>
        public NavMeshArea? TopRightArea { get; set; }

        /// <summary>
        /// Gets or sets the top behind area.
        /// </summary>
        public NavMeshArea? TopBehindArea { get; set; }

        /// <summary>
        /// Gets or sets the bottom area.
        /// </summary>
        public NavMeshArea? BottomArea { get; set; }

        /// <summary>
        /// Gets or sets the bottom left area.
        /// </summary>
        public NavMeshArea? BottomLeftArea { get; set; }

        /// <summary>
        /// Gets or sets the bottom right area.
        /// </summary>
        public NavMeshArea? BottomRightArea { get; set; }

        /// <summary>
        /// Reads the ladder from a binary reader.
        /// </summary>
        public void Read(BinaryReader binaryReader, NavMeshFile navMeshFile)
        {
            Id = binaryReader.ReadUInt32();

            Width = binaryReader.ReadSingle();

            Top = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
            Bottom = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());

            Length = binaryReader.ReadSingle();

            Direction = (NavDirectionType)binaryReader.ReadUInt32();

            TopForwardArea = navMeshFile.GetArea(binaryReader.ReadUInt32());
            TopLeftArea = navMeshFile.GetArea(binaryReader.ReadUInt32());
            TopRightArea = navMeshFile.GetArea(binaryReader.ReadUInt32());
            TopBehindArea = navMeshFile.GetArea(binaryReader.ReadUInt32());
            BottomArea = navMeshFile.GetArea(binaryReader.ReadUInt32());

            if (navMeshFile.Version >= 35)
            {
                BottomLeftArea = navMeshFile.GetArea(binaryReader.ReadUInt32());
                BottomRightArea = navMeshFile.GetArea(binaryReader.ReadUInt32());
            }
            else
            {
                BottomLeftArea = null;
                BottomRightArea = null;
            }
        }
    }
}
