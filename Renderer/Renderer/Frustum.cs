
namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// View frustum for culling objects outside the camera's visible area.
    /// </summary>
    public readonly struct Frustum
    {
        /// <summary>Gets the six clipping planes that define this frustum.</summary>
        public readonly Plane[] Planes { get; }

        /// <summary>Initializes a new <see cref="Frustum"/> with six zeroed planes.</summary>
        public Frustum()
        {
            Planes = new Plane[6];
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
            var m = viewProjectionMatrix;
            var c1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
            var c2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
            var c3 = new Vector4(m.M13, m.M23, m.M33, m.M43);
            var c4 = new Vector4(m.M14, m.M24, m.M34, m.M44);

            Planes[0] = Plane.Normalize(new Plane(c4 + c1)); // Left
            Planes[1] = Plane.Normalize(new Plane(c4 - c1)); // Right
            Planes[2] = Plane.Normalize(new Plane(c4 - c2)); // Top
            Planes[3] = Plane.Normalize(new Plane(c4 + c2)); // Bottom
            Planes[4] = Plane.Normalize(new Plane(c3));       // Near
            Planes[5] = Plane.Normalize(new Plane(c4 - c3)); // Far
        }

        /// <summary>
        /// Creates a deep copy of this frustum.
        /// </summary>
        /// <returns>A new frustum with the same planes.</returns>
        public Frustum Clone()
        {
            var rv = new Frustum();
            Planes.CopyTo(rv.Planes);
            return rv;
        }

        /// <summary>
        /// Tests if an axis-aligned bounding box intersects this frustum.
        /// </summary>
        /// <param name="box">The bounding box to test.</param>
        /// <returns><see langword="true"/> if the box is at least partially inside the frustum.</returns>
        public bool Intersects(in AABB box)
        {
            var center = new Vector4((box.Max + box.Min) * 0.5f, 1f);
            var extent = (box.Max - box.Min) * 0.5f;

            foreach (ref readonly var plane in Planes.AsSpan())
            {
                var dist = Plane.Dot(plane, center);
                var radius = Vector3.Dot(extent, Vector3.Abs(plane.Normal));

                if (dist + radius < 0)
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
            foreach (ref readonly var plane in Planes.AsSpan())
            {
                if (Plane.DotCoordinate(plane, point) < 0)
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
