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
            var c1 = Vector3.Transform(new Vector3(Min.X, Min.Y, Min.Z), transform);
            var c2 = Vector3.Transform(new Vector3(Max.X, Min.Y, Min.Z), transform);
            var c3 = Vector3.Transform(new Vector3(Max.X, Max.Y, Min.Z), transform);
            var c4 = Vector3.Transform(new Vector3(Min.X, Max.Y, Min.Z), transform);
            var c5 = Vector3.Transform(new Vector3(Min.X, Max.Y, Max.Z), transform);
            var c6 = Vector3.Transform(new Vector3(Min.X, Min.Y, Max.Z), transform);
            var c7 = Vector3.Transform(new Vector3(Max.X, Min.Y, Max.Z), transform);
            var c8 = Vector3.Transform(new Vector3(Max.X, Max.Y, Max.Z), transform);

            var min = c1;
            var max = c1;

            min = Vector3.Min(min, c2);
            max = Vector3.Max(max, c2);

            min = Vector3.Min(min, c3);
            max = Vector3.Max(max, c3);

            min = Vector3.Min(min, c4);
            max = Vector3.Max(max, c4);

            min = Vector3.Min(min, c5);
            max = Vector3.Max(max, c5);

            min = Vector3.Min(min, c6);
            max = Vector3.Max(max, c6);

            min = Vector3.Min(min, c7);
            max = Vector3.Max(max, c7);

            min = Vector3.Min(min, c8);
            max = Vector3.Max(max, c8);

            return new AABB(min, max);
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
