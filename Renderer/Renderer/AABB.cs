namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Axis-aligned bounding box for spatial queries and culling.
    /// </summary>
    public readonly struct AABB : IEquatable<AABB>
    {
#pragma warning disable CA1051 // Do not declare visible instance fields
        /// <summary>
        /// Minimum corner of the bounding box.
        /// </summary>
        public readonly Vector3 Min;

        /// <summary>
        /// Maximum corner of the bounding box.
        /// </summary>
        public readonly Vector3 Max;
#pragma warning restore CA1051

        /// <summary>
        /// Gets the size of the bounding box.
        /// </summary>
        public Vector3 Size => Max - Min;

        /// <summary>
        /// Gets the center point of the bounding box.
        /// </summary>
        public Vector3 Center => (Min + Max) * 0.5f;

        /// <summary>
        /// Initializes a new bounding box from minimum and maximum corners.
        /// </summary>
        /// <param name="min">Minimum corner.</param>
        /// <param name="max">Maximum corner.</param>
        public AABB(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>
        /// Initializes a new bounding box centered at a point with uniform extents.
        /// </summary>
        /// <param name="point">Center point.</param>
        /// <param name="halfExtents">Half-size along each axis.</param>
        public AABB(Vector3 point, float halfExtents)
        {
            var size = new Vector3(halfExtents);
            Min = point - size;
            Max = point + size;
        }

        /// <summary>
        /// Initializes a new bounding box from individual coordinate components.
        /// </summary>
        public AABB(float min_x, float min_y, float min_z, float max_x, float max_y, float max_z)
        {
            Min = new Vector3(min_x, min_y, min_z);
            Max = new Vector3(max_x, max_y, max_z);
        }

        /// <summary>
        /// Tests if a point is inside this bounding box.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns><see langword="true"/> if the point is inside the box.</returns>
        public bool Contains(Vector3 point)
        {
            return
                Min.X <= point.X && point.X <= Max.X &&
                Min.Y <= point.Y && point.Y <= Max.Y &&
                Min.Z <= point.Z && point.Z <= Max.Z;
        }

        /// <summary>
        /// Tests if another bounding box intersects this one.
        /// </summary>
        /// <param name="other">The other bounding box.</param>
        /// <returns><see langword="true"/> if the boxes overlap.</returns>
        public bool Intersects(in AABB other)
        {
            return
                Min.X <= other.Max.X && other.Min.X <= Max.X &&
                Min.Y <= other.Max.Y && other.Min.Y <= Max.Y &&
                Min.Z <= other.Max.Z && other.Min.Z <= Max.Z;
        }

        /// <summary>
        /// Tests if another bounding box is completely inside this one.
        /// </summary>
        /// <param name="other">The other bounding box.</param>
        /// <returns><see langword="true"/> if the other box is fully contained.</returns>
        public bool Contains(in AABB other)
        {
            return
                Min.X <= other.Min.X && other.Max.X <= Max.X &&
                Min.Y <= other.Min.Y && other.Max.Y <= Max.Y &&
                Min.Z <= other.Min.Z && other.Max.Z <= Max.Z;
        }

        /// <summary>
        /// Computes the union of this bounding box with another.
        /// </summary>
        /// <param name="other">The other bounding box.</param>
        /// <returns>A new bounding box that contains both boxes.</returns>
        public AABB Union(in AABB other)
        {
            return new AABB(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));
        }

        /// <summary>
        /// Expands this bounding box to include a point.
        /// </summary>
        /// <param name="point">The point to include.</param>
        /// <returns>A new bounding box that contains the original box and the point.</returns>
        public AABB Encapsulate(in Vector3 point)
        {
            return new AABB(Vector3.Min(Min, point), Vector3.Max(Max, point));
        }

        /// <summary>
        /// Translates this bounding box by an offset.
        /// </summary>
        /// <param name="offset">The translation offset.</param>
        /// <returns>A new bounding box at the translated position.</returns>
        public AABB Translate(in Vector3 offset)
        {
            return new AABB(Min + offset, Max + offset);
        }

        /// <summary>
        /// Transforms this bounding box by a matrix and returns an axis-aligned result.
        /// </summary>
        /// <param name="transform">The transformation matrix.</param>
        /// <returns>A new axis-aligned bounding box that contains the transformed box.</returns>
        /// <remarks>
        /// The resulting AABB may be larger than the original if rotation is involved.
        /// To minimize error accumulation, premultiply matrices before transforming.
        /// </remarks>
        public AABB Transform(in Matrix4x4 transform)
        {
            var center = Center;
            var extents = Max - center;
            var newCenter = Vector3.Transform(center, transform);

            var newExtents = new Vector3(
                MathF.Abs(transform.M11) * extents.X + MathF.Abs(transform.M21) * extents.Y + MathF.Abs(transform.M31) * extents.Z,
                MathF.Abs(transform.M12) * extents.X + MathF.Abs(transform.M22) * extents.Y + MathF.Abs(transform.M32) * extents.Z,
                MathF.Abs(transform.M13) * extents.X + MathF.Abs(transform.M23) * extents.Y + MathF.Abs(transform.M33) * extents.Z
            );

            return new AABB(newCenter - newExtents, newCenter + newExtents);
        }

        /// <inheritdoc/>
        public override readonly string ToString()
        {
            return $"AABB [({Min.X},{Min.Y},{Min.Z}) -> ({Max.X},{Max.Y},{Max.Z}))";
        }

        /// <inheritdoc/>
        public readonly bool Equals(AABB other) => Min.Equals(other.Min) && Max.Equals(other.Max);

        /// <inheritdoc/>
        public override readonly bool Equals(object? obj) => obj is AABB other && Equals(other);

        /// <inheritdoc/>
        public override readonly int GetHashCode() => HashCode.Combine(Min.X, Min.Y, Min.Z, Max.X, Max.Y, Max.Z);

        /// <summary>
        /// Checks if two bounding boxes are equal.
        /// </summary>
        public static bool operator ==(AABB left, AABB right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Checks if two bounding boxes are not equal.
        /// </summary>
        public static bool operator !=(AABB left, AABB right)
        {
            return !(left == right);
        }
    }
}
