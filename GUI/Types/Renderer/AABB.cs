using OpenTK;

namespace GUI.Types.Renderer
{
    internal struct AABB
    {
        public Vector3 Min;
        public Vector3 Max;

        public Vector3 Size { get => Max - Min; }
        public Vector3 Center { get => (Min + Max) * 0.5f; }
        public bool IsZero { get => Max.X <= Min.X && Max.Y <= Min.Y && Max.Z <= Min.Z; }

        public AABB(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        public AABB(float min_x, float min_y, float min_z, float max_x, float max_y, float max_z)
        {
            Min = new Vector3(min_x, min_y, min_z);
            Max = new Vector3(max_x, max_y, max_z);
        }

        public bool Contains(Vector3 point)
        {
            return
                point.X >= Min.X && point.X < Max.X &&
                point.Y >= Min.Y && point.Y < Max.Y &&
                point.Z >= Min.Z && point.Z < Max.Z;
        }

        public bool Intersects(AABB other)
        {
            return
                other.Max.X >= Min.X && other.Min.X < Max.X &&
                other.Max.Y >= Min.Y && other.Min.Y < Max.Y &&
                other.Max.Z >= Min.Z && other.Min.Z < Max.Z
                ;
        }

        public bool Contains(AABB other)
        {
            return
                other.Min.X >= Min.X && other.Max.X <= Max.X &&
                other.Min.Y >= Min.Y && other.Max.Y <= Max.Y &&
                other.Min.Z >= Min.Z && other.Max.Z <= Max.Z;
        }

        public AABB Union(AABB other)
        {
            return new AABB(
                new Vector3(System.Math.Min(Min.X, other.Min.X), System.Math.Min(Min.Y, other.Min.Y), System.Math.Min(Min.Z, other.Min.Z)),
                new Vector3(System.Math.Max(Max.X, other.Max.X), System.Math.Max(Max.Y, other.Max.Y), System.Math.Max(Max.Z, other.Max.Z)));
        }

        // Note: Since we're dealing with AABBs here, the resulting AABB is likely to be bigger than the original if rotation
        // and whatnot is involved. This problem compounds with multiple transformations. Therefore, endeavour to premultiply matrices
        // and only use this at the last step.
        public AABB Transformed(Matrix4 transform)
        {
            var points = new Vector4[]
            {
                new Vector4(Min.X, Min.Y, Min.Z, 1.0f) * transform,
                new Vector4(Max.X, Min.Y, Min.Z, 1.0f) * transform,
                new Vector4(Max.X, Max.Y, Min.Z, 1.0f) * transform,
                new Vector4(Min.X, Max.Y, Min.Z, 1.0f) * transform,
                new Vector4(Min.X, Max.Y, Max.Z, 1.0f) * transform,
                new Vector4(Min.X, Min.Y, Max.Z, 1.0f) * transform,
                new Vector4(Max.X, Min.Y, Max.Z, 1.0f) * transform,
                new Vector4(Max.X, Max.Y, Max.Z, 1.0f) * transform,
            };

            var min = points[0];
            var max = points[0];
            for (int i = 1; i < points.Length; ++i)
            {
                var tf = points[i];
                min.X = System.Math.Min(min.X, tf.X);
                min.Y = System.Math.Min(min.Y, tf.Y);
                min.Z = System.Math.Min(min.Z, tf.Z);
                max.X = System.Math.Max(max.X, tf.X);
                max.Y = System.Math.Max(max.Y, tf.Y);
                max.Z = System.Math.Max(max.Z, tf.Z);
            }

            return new AABB(min.Xyz, max.Xyz);
        }

        public override string ToString()
        {
            return string.Format("AABB [({0},{1},{2}) -> ({3},{4},{5}))", Min.X, Min.Y, Min.Z, Max.X, Max.Y, Max.Z);
        }
    }
}
