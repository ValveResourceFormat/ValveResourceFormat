namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// View frustum for culling objects outside the camera's visible area.
    /// </summary>
    public class Frustum
    {
        private Vector4[] Planes = new Vector4[6];

        /// <summary>
        /// Creates an empty frustum with no planes.
        /// </summary>
        /// <returns>A new empty frustum.</returns>
        public static Frustum CreateEmpty()
        {
            var rv = new Frustum
            {
                Planes = [],
            };
            return rv;
        }

        /// <summary>
        /// Updates the frustum planes from a view-projection matrix.
        /// </summary>
        /// <param name="viewProjectionMatrix">Combined view and projection matrix.</param>
        public void Update(in Matrix4x4 viewProjectionMatrix)
        {
            Planes[0] = Vector4.Normalize(new Vector4(
                viewProjectionMatrix.M14 + viewProjectionMatrix.M11,
                viewProjectionMatrix.M24 + viewProjectionMatrix.M21,
                viewProjectionMatrix.M34 + viewProjectionMatrix.M31,
                viewProjectionMatrix.M44 + viewProjectionMatrix.M41));
            Planes[1] = Vector4.Normalize(new Vector4(
                viewProjectionMatrix.M14 - viewProjectionMatrix.M11,
                viewProjectionMatrix.M24 - viewProjectionMatrix.M21,
                viewProjectionMatrix.M34 - viewProjectionMatrix.M31,
                viewProjectionMatrix.M44 - viewProjectionMatrix.M41));
            Planes[2] = Vector4.Normalize(new Vector4(
                viewProjectionMatrix.M14 - viewProjectionMatrix.M12,
                viewProjectionMatrix.M24 - viewProjectionMatrix.M22,
                viewProjectionMatrix.M34 - viewProjectionMatrix.M32,
                viewProjectionMatrix.M44 - viewProjectionMatrix.M42));
            Planes[3] = Vector4.Normalize(new Vector4(
                viewProjectionMatrix.M14 + viewProjectionMatrix.M12,
                viewProjectionMatrix.M24 + viewProjectionMatrix.M22,
                viewProjectionMatrix.M34 + viewProjectionMatrix.M32,
                viewProjectionMatrix.M44 + viewProjectionMatrix.M42));
            Planes[4] = Vector4.Normalize(new Vector4(
                viewProjectionMatrix.M13,
                viewProjectionMatrix.M23,
                viewProjectionMatrix.M33,
                viewProjectionMatrix.M43));
            Planes[5] = Vector4.Normalize(new Vector4(
                viewProjectionMatrix.M14 - viewProjectionMatrix.M13,
                viewProjectionMatrix.M24 - viewProjectionMatrix.M23,
                viewProjectionMatrix.M34 - viewProjectionMatrix.M33,
                viewProjectionMatrix.M44 - viewProjectionMatrix.M43));
        }

        /// <summary>
        /// Creates a deep copy of this frustum.
        /// </summary>
        /// <returns>A new frustum with the same planes.</returns>
        public Frustum Clone()
        {
            var rv = new Frustum();
            Planes.CopyTo(rv.Planes, 0);
            return rv;
        }

        /// <summary>
        /// Tests if an axis-aligned bounding box intersects this frustum.
        /// </summary>
        /// <param name="box">The bounding box to test.</param>
        /// <returns><see langword="true"/> if the box is at least partially inside the frustum.</returns>
        public bool Intersects(in AABB box)
        {
            for (var i = 0; i < Planes.Length; ++i)
            {
                var closest = new Vector3(
                    Planes[i].X < 0 ? box.Min.X : box.Max.X,
                    Planes[i].Y < 0 ? box.Min.Y : box.Max.Y,
                    Planes[i].Z < 0 ? box.Min.Z : box.Max.Z);

                if (Vector3.Dot(new Vector3(Planes[i].X, Planes[i].Y, Planes[i].Z), closest) + Planes[i].W < 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Tests if a point is inside this frustum.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns><see langword="true"/> if the point is inside the frustum.</returns>
        public bool Intersects(in Vector3 point)
        {
            for (var i = 0; i < Planes.Length; ++i)
            {
                if (Vector3.Dot(new Vector3(Planes[i].X, Planes[i].Y, Planes[i].Z), point) + Planes[i].W < 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = 0;
            for (var i = 0; i < Planes.Length; ++i)
            {
                hash ^= Planes[i].GetHashCode();
            }

            return hash;
        }
    }
}
