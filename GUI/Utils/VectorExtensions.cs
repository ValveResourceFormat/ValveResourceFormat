namespace GUI.Utils
{
    static class VectorExtensions
    {
        public static OpenTK.Mathematics.Matrix4 ToOpenTK(this Matrix4x4 m)
        {
            return new OpenTK.Mathematics.Matrix4(m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);
        }

        public static OpenTK.Mathematics.Matrix3x4 To3x4(this Matrix4x4 m)
        {
            return new OpenTK.Mathematics.Matrix3x4(
                m.M11, m.M21, m.M31, m.M41,
                m.M12, m.M22, m.M32, m.M42,
                m.M13, m.M23, m.M33, m.M43
            );
        }
    }
}
