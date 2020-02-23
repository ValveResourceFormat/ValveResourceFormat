using OpenTK;

namespace GUI.Types.Renderer
{
    internal class Frustum
    {
        private readonly Vector4[] Planes = new Vector4[6];

        public Frustum(Matrix4 viewProjectionMatrix)
        {
            Planes[0] = new Vector4(
                viewProjectionMatrix.M14 + viewProjectionMatrix.M11,
                viewProjectionMatrix.M24 + viewProjectionMatrix.M21,
                viewProjectionMatrix.M34 + viewProjectionMatrix.M31,
                viewProjectionMatrix.M44 + viewProjectionMatrix.M41);
            Planes[1] = new Vector4(
                viewProjectionMatrix.M14 - viewProjectionMatrix.M11,
                viewProjectionMatrix.M24 - viewProjectionMatrix.M21,
                viewProjectionMatrix.M34 - viewProjectionMatrix.M31,
                viewProjectionMatrix.M44 - viewProjectionMatrix.M41);
            Planes[2] = new Vector4(
                viewProjectionMatrix.M14 - viewProjectionMatrix.M12,
                viewProjectionMatrix.M24 - viewProjectionMatrix.M22,
                viewProjectionMatrix.M34 - viewProjectionMatrix.M32,
                viewProjectionMatrix.M44 - viewProjectionMatrix.M42);
            Planes[3] = new Vector4(
                viewProjectionMatrix.M14 + viewProjectionMatrix.M12,
                viewProjectionMatrix.M24 + viewProjectionMatrix.M22,
                viewProjectionMatrix.M34 + viewProjectionMatrix.M32,
                viewProjectionMatrix.M44 + viewProjectionMatrix.M42);
            Planes[4] = new Vector4(
                viewProjectionMatrix.M13,
                viewProjectionMatrix.M23,
                viewProjectionMatrix.M33,
                viewProjectionMatrix.M43);
            Planes[5] = new Vector4(
                viewProjectionMatrix.M14 - viewProjectionMatrix.M13,
                viewProjectionMatrix.M24 - viewProjectionMatrix.M23,
                viewProjectionMatrix.M34 - viewProjectionMatrix.M33,
                viewProjectionMatrix.M44 - viewProjectionMatrix.M43);

            for (var i = 0; i < Planes.Length; ++i)
            {
                Planes[i].Normalize();
            }
        }

        public bool Intersects(AABB box)
        {
            for (var i = 0; i < Planes.Length; ++i)
            {
                var closest = new Vector3(
                    Planes[i].X < 0 ? box.Min.X : box.Max.X,
                    Planes[i].Y < 0 ? box.Min.Y : box.Max.Y,
                    Planes[i].Z < 0 ? box.Min.Z : box.Max.Z);

                if (Vector3.Dot(Planes[i].Xyz, closest) + Planes[i].W < 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
