namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Spatial set for the scene nodes that move. Flat rather than hierarchical: relocating a node is
    /// six float stores and culling is a linear pass over bounds laid out for SIMD.
    /// </summary>
    public class SpatialNodeSet : ISpatialSet
    {
        private SceneNode[] nodes = [];
        private float[] centerX = [];
        private float[] centerY = [];
        private float[] centerZ = [];
        private float[] extentX = [];
        private float[] extentY = [];
        private float[] extentZ = [];

        /// <summary>Gets the number of nodes in the set.</summary>
        public int Count { get; private set; }

        /// <summary>Gets or sets whether the set needs rebuilding from the scene's node list.</summary>
        public bool Dirty { get; set; } = true;

        /// <summary>Gets or sets the debug visualization renderer for this set.</summary>
        public SpatialNodeSetDebugRenderer? DebugRenderer { get; set; }

        /// <summary>Removes every node from the set.</summary>
        public void Clear()
        {
            for (var i = 0; i < Count; i++)
            {
                nodes[i].DynamicSetIndex = -1;
                nodes[i] = null!;
            }

            Count = 0;
        }

        /// <summary>Adds a node to the set and records its current bounds.</summary>
        public void Insert(SceneNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            EnsureCapacity(Count + 1);

            nodes[Count] = node;
            node.DynamicSetIndex = Count;
            WriteBounds(Count, node.BoundingBox);

            Count++;
        }

        /// <summary>
        /// Refreshes a node's stored bounds. Nodes that are not in the set, because their layer is off
        /// or the set has been rebuilt since, are ignored.
        /// </summary>
        public void Update(SceneNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            var index = node.DynamicSetIndex;

            if ((uint)index >= (uint)Count || !ReferenceEquals(nodes[index], node))
            {
                return;
            }

            WriteBounds(index, node.BoundingBox);
        }

        private void WriteBounds(int index, in AABB bounds)
        {
            var center = (bounds.Min + bounds.Max) * 0.5f;
            var extent = (bounds.Max - bounds.Min) * 0.5f;

            centerX[index] = center.X;
            centerY[index] = center.Y;
            centerZ[index] = center.Z;
            extentX[index] = extent.X;
            extentY[index] = extent.Y;
            extentZ[index] = extent.Z;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= nodes.Length)
            {
                return;
            }

            // Rounded up to a whole vector so the SIMD pass never reads past the end of a live lane
            var capacity = Math.Max(required, nodes.Length == 0 ? 64 : nodes.Length * 2);
            capacity = (capacity + Vector<float>.Count - 1) / Vector<float>.Count * Vector<float>.Count;

            Array.Resize(ref nodes, capacity);
            Array.Resize(ref centerX, capacity);
            Array.Resize(ref centerY, capacity);
            Array.Resize(ref centerZ, capacity);
            Array.Resize(ref extentX, capacity);
            Array.Resize(ref extentY, capacity);
            Array.Resize(ref extentZ, capacity);
        }

        /// <summary>
        /// Appends every node whose bounds intersect the frustum, testing a vector of nodes at a time.
        /// </summary>
        public void Query(Frustum frustum, List<SceneNode> results)
        {
            ArgumentNullException.ThrowIfNull(results);

            var planes = frustum.Planes;
            var width = Vector<float>.Count;
            var i = 0;

            for (; i <= Count - width; i += width)
            {
                var cx = new Vector<float>(centerX, i);
                var cy = new Vector<float>(centerY, i);
                var cz = new Vector<float>(centerZ, i);
                var ex = new Vector<float>(extentX, i);
                var ey = new Vector<float>(extentY, i);
                var ez = new Vector<float>(extentZ, i);

                // Accumulated as "behind a plane" rather than "in front of every plane". The two are not
                // complements when the distance is NaN, which an infinite extent produces (0 * infinity,
                // for any plane with a zero normal component), and a NaN must leave the node visible to
                // match Frustum.Intersects, which only rejects on a strict less-than.
                var outside = Vector<int>.Zero;

                foreach (ref readonly var plane in planes.AsSpan())
                {
                    var normal = plane.Normal;
                    var absNormal = Vector3.Abs(normal);

                    // Signed distance from the box center to the plane
                    var distance = (new Vector<float>(normal.X) * cx)
                        + (new Vector<float>(normal.Y) * cy)
                        + (new Vector<float>(normal.Z) * cz)
                        + new Vector<float>(plane.D);

                    // How far the box reaches toward the plane from its center
                    var radius = (new Vector<float>(absNormal.X) * ex)
                        + (new Vector<float>(absNormal.Y) * ey)
                        + (new Vector<float>(absNormal.Z) * ez);

                    outside |= Vector.LessThan(distance + radius, Vector<float>.Zero);
                }

                if (Vector.EqualsAll(outside, Vector<int>.AllBitsSet))
                {
                    continue; // whole vector culled
                }

                for (var lane = 0; lane < width; lane++)
                {
                    if (outside[lane] == 0)
                    {
                        results.Add(nodes[i + lane]);
                    }
                }
            }

            for (; i < Count; i++)
            {
                if (frustum.Intersects(GetBounds(i)))
                {
                    results.Add(nodes[i]);
                }
            }
        }

        /// <summary>
        /// Appends every node whose bounds intersect the given box.
        /// </summary>
        public void Query(in AABB bounds, List<SceneNode> results)
        {
            ArgumentNullException.ThrowIfNull(results);

            for (var i = 0; i < Count; i++)
            {
                if (bounds.Intersects(GetBounds(i)))
                {
                    results.Add(nodes[i]);
                }
            }
        }

        /// <summary>Returns the bounds covering every node in the set, or an empty box when it has none.</summary>
        public AABB GetBounds()
        {
            if (Count == 0)
            {
                return new AABB();
            }

            var bounds = GetBounds(0);

            for (var i = 1; i < Count; i++)
            {
                bounds = bounds.Union(GetBounds(i));
            }

            return bounds;
        }

        private AABB GetBounds(int index)
        {
            var center = new Vector3(centerX[index], centerY[index], centerZ[index]);
            var extent = new Vector3(extentX[index], extentY[index], extentZ[index]);

            return new AABB(center - extent, center + extent);
        }

        /// <summary>Returns the nodes currently in the set, for debug visualization.</summary>
        public ReadOnlySpan<SceneNode> GetNodes() => nodes.AsSpan(0, Count);
    }
}
