namespace GUI.Types.Renderer
{
    internal readonly struct AABB : IEquatable<AABB>
    {
        public readonly Vector3 Min;
        public readonly Vector3 Max;

        public Vector3 Size => Max - Min;
        public Vector3 Center => (Min + Max) * 0.5f;

        public AABB(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        public AABB(Vector3 point, float uniformSize)
        {
            var size = new Vector3(uniformSize);
            Min = point - size;
            Max = point + size;
        }

        public AABB(float min_x, float min_y, float min_z, float max_x, float max_y, float max_z)
        {
            Min = new Vector3(min_x, min_y, min_z);
            Max = new Vector3(max_x, max_y, max_z);
        }

        public bool Contains(Vector3 point)
        {
            return
                Min.X <= point.X && point.X <= Max.X &&
                Min.Y <= point.Y && point.Y <= Max.Y &&
                Min.Z <= point.Z && point.Z <= Max.Z;
        }

        public bool Intersects(in AABB other)
        {
            return
                Min.X <= other.Max.X && other.Min.X <= Max.X &&
                Min.Y <= other.Max.Y && other.Min.Y <= Max.Y &&
                Min.Z <= other.Max.Z && other.Min.Z <= Max.Z;
        }

        public bool Contains(in AABB other)
        {
            return
                Min.X <= other.Min.X && other.Max.X <= Max.X &&
                Min.Y <= other.Min.Y && other.Max.Y <= Max.Y &&
                Min.Z <= other.Min.Z && other.Max.Z <= Max.Z;
        }

        public AABB Union(in AABB other)
        {
            return new AABB(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));
        }

        public AABB Translate(in Vector3 offset)
        {
            return new AABB(Min + offset, Max + offset);
        }

        // Note: Since we're dealing with AABBs here, the resulting AABB is likely to be bigger than the original if rotation
        // and whatnot is involved. This problem compounds with multiple transformations. Therefore, endeavour to premultiply matrices
        // and only use this at the last step.
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

        public override readonly string ToString()
        {
            return $"AABB [({Min.X},{Min.Y},{Min.Z}) -> ({Max.X},{Max.Y},{Max.Z}))";
        }

        public readonly bool Equals(AABB other) => Min.Equals(other.Min) && Max.Equals(other.Max);
        public override readonly bool Equals(object? obj) => obj is AABB other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(Min.X, Min.Y, Min.Z, Max.X, Max.Y, Max.Z);
    }
}
