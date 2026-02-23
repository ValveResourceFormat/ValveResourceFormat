using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// View frustum for culling objects outside the camera's visible area.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Frustum
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public readonly Plane[] Planes = new Plane[6];

        public Frustum()
        {
        }

        /// <summary>
        /// Gets a value indicating whether this frustum is empty (does not cull anything).
        /// </summary>
        public bool IsEmpty => Planes[0].Normal == Vector3.Zero && Planes[0].D > 0;

        /// <summary>
        /// Sets this frustum to an empty state that does not cull anything.
        /// Planes will be set to (0, 0, 0, 1) which means all points will be in front of the plane.
        /// </summary>
        public void SetEmpty()
        {
            for (var i = 0; i < Planes.Length; i++)
            {
                Planes[i] = new Plane(Vector3.Zero, 1);
            }
        }

        /// <summary>
        /// Creates an empty frustum that does not cull anything.
        /// </summary>
        /// <returns>A new empty frustum.</returns>
        public static Frustum CreateEmpty()
        {
            var frustum = new Frustum();
            frustum.SetEmpty();
            return frustum;
        }

        /// <summary>
        /// Updates the frustum planes from a view-projection matrix.
        /// </summary>
        /// <param name="viewProjectionMatrix">Combined view and projection matrix.</param>
        public void Update(in Matrix4x4 viewProjectionMatrix)
        {
            Planes[0] = Plane.Normalize(new Plane(
                viewProjectionMatrix.M14 + viewProjectionMatrix.M11,
                viewProjectionMatrix.M24 + viewProjectionMatrix.M21,
                viewProjectionMatrix.M34 + viewProjectionMatrix.M31,
                viewProjectionMatrix.M44 + viewProjectionMatrix.M41));
            Planes[1] = Plane.Normalize(new Plane(
                viewProjectionMatrix.M14 - viewProjectionMatrix.M11,
                viewProjectionMatrix.M24 - viewProjectionMatrix.M21,
                viewProjectionMatrix.M34 - viewProjectionMatrix.M31,
                viewProjectionMatrix.M44 - viewProjectionMatrix.M41));
            Planes[2] = Plane.Normalize(new Plane(
                viewProjectionMatrix.M14 - viewProjectionMatrix.M12,
                viewProjectionMatrix.M24 - viewProjectionMatrix.M22,
                viewProjectionMatrix.M34 - viewProjectionMatrix.M32,
                viewProjectionMatrix.M44 - viewProjectionMatrix.M42));
            Planes[3] = Plane.Normalize(new Plane(
                viewProjectionMatrix.M14 + viewProjectionMatrix.M12,
                viewProjectionMatrix.M24 + viewProjectionMatrix.M22,
                viewProjectionMatrix.M34 + viewProjectionMatrix.M32,
                viewProjectionMatrix.M44 + viewProjectionMatrix.M42));
            Planes[4] = Plane.Normalize(new Plane(
                viewProjectionMatrix.M13,
                viewProjectionMatrix.M23,
                viewProjectionMatrix.M33,
                viewProjectionMatrix.M43));
            Planes[5] = Plane.Normalize(new Plane(
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
                    Planes[i].Normal.X < 0 ? box.Min.X : box.Max.X,
                    Planes[i].Normal.Y < 0 ? box.Min.Y : box.Max.Y,
                    Planes[i].Normal.Z < 0 ? box.Min.Z : box.Max.Z);

                if (Vector3.Dot(Planes[i].Normal, closest) + Planes[i].D < 0)
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
                if (Vector3.Dot(Planes[i].Normal, point) + Planes[i].D < 0)
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
