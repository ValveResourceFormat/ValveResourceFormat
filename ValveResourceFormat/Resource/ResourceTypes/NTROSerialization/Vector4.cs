using System;

namespace ValveResourceFormat.ResourceTypes.NTROSerialization
{
    /// <summary>
    /// Represents a 4D vector.
    /// </summary>
    public class Vector4
    {
        /// <summary>
        /// Gets the X component of the Vector4.
        /// </summary>
        public float X { get; }

        /// <summary>
        /// Gets the Y component of the Vector4.
        /// </summary>
        public float Y { get; }

        /// <summary>
        /// Gets the Z component of the Vector4.
        /// </summary>
        public float Z { get; }

        /// <summary>
        /// Gets the W component of the Vector4.
        /// </summary>
        public float W { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Vector4"/> class.
        /// </summary>
        /// <param name="x">The x component of the Vector4.</param>
        /// <param name="y">The y component of the Vector4.</param>
        /// <param name="z">The z component of the Vector4.</param>
        /// <param name="w">The w component of the Vector4.</param>
        public Vector4(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        // Due to DataType needing to be known to do ToString() here, it is done elsewhere
    }
}
