namespace GUI.Utils
{
    static class VectorExtensions
    {
        // Utility functions to convert float arrays into vectors
        public static Vector3 ToVector3(this float[] floatarr)
        {
            if (floatarr.Length < 3)
            {
                throw new ArgumentException("ToVector3 needs float array of at least length 3.", nameof(floatarr));
            }

            return new Vector3(floatarr[0], floatarr[1], floatarr[2]);
        }

        // The above, but for double arrays (usually used in particle arrays)
        public static Vector3 ToVector3(this double[] floatarr)
        {
            if (floatarr.Length < 3)
            {
                throw new ArgumentException("ToVector3 needs float array of at least length 3.", nameof(floatarr));
            }

            return new Vector3((float)floatarr[0], (float)floatarr[1], (float)floatarr[2]);
        }

        public static OpenTK.Vector2 ToOpenTK(this Vector2 vec)
        {
            return new OpenTK.Vector2(vec.X, vec.Y);
        }

        public static OpenTK.Vector3 ToOpenTK(this Vector3 vec)
        {
            return new OpenTK.Vector3(vec.X, vec.Y, vec.Z);
        }

        public static OpenTK.Vector4 ToOpenTK(this Vector4 vec)
        {
            return new OpenTK.Vector4(vec.X, vec.Y, vec.Z, vec.W);
        }

        public static OpenTK.Matrix4 ToOpenTK(this Matrix4x4 m)
        {
            return new OpenTK.Matrix4(m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);
        }
    }
}
