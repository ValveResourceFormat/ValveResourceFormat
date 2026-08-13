using System.IO;

namespace ValveResourceFormat.NavMesh
{
    /// <summary>
    /// A local space bounding box together with a world space transform. What these are used for is not known.
    /// </summary>
    public readonly struct NavMeshTransformedBounds
    {
        /// <summary>
        /// Gets the minimum corner of the bounding box in local space.
        /// </summary>
        public Vector3 Mins { get; }

        /// <summary>
        /// Gets the maximum corner of the bounding box in local space.
        /// </summary>
        public Vector3 Maxs { get; }

        /// <summary>
        /// Gets the transform that places the bounding box into the world.
        /// </summary>
        public Matrix4x4 Transform { get; }

        /// <summary>
        /// Reads the transformed bounds from a binary reader.
        /// </summary>
        public NavMeshTransformedBounds(BinaryReader binaryReader)
        {
            Mins = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());
            Maxs = new Vector3(binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle());

            // Stored as a row major 3x4 matrix, three rotation rows each followed by a translation component
            Transform = Matrix4x4.Transpose(new Matrix4x4(
                binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle(),
                binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle(),
                binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle(), binaryReader.ReadSingle(),
                0f, 0f, 0f, 1f
            ));
        }
    }
}
