using System;

namespace ValveResourceFormat.ResourceTypes.NTROSerialization
{
    /// <summary>
    /// Represents a 3D vector.
    /// </summary>
    public class Vector3
    {
        /// <summary>
        /// Gets the X component of the Vector3.
        /// </summary>
        public float X { get; }

        /// <summary>
        /// Gets the Y component of the Vector3.
        /// </summary>
        public float Y { get; }

        /// <summary>
        /// Gets the Z component of the Vector3.
        /// </summary>
        public float Z { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Vector3"/> class.
        /// </summary>
        /// <param name="x">The x component of the Vector3.</param>
        /// <param name="y">The y component of the Vector3.</param>
        /// <param name="z">The z component of the Vector3.</param>
        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString()
        {
            return string.Format("({0:F6}, {1:F6}, {2:F6})", X, Y, Z);
        }
    }
}
