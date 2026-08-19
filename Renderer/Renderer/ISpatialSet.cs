namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Spatial set of scene nodes, queryable by frustum or box. Implemented hierarchically by
    /// <see cref="Octree"/> and flat by <see cref="SpatialNodeSet"/>.
    /// </summary>
    public interface ISpatialSet
    {
        /// <summary>Gets or sets whether the set needs rebuilding from the scene's node list.</summary>
        bool Dirty { get; set; }

        /// <summary>Removes every node from the set.</summary>
        void Clear();

        /// <summary>Adds a node to the set.</summary>
        void Insert(SceneNode node);

        /// <summary>Refreshes a node's stored bounds.</summary>
        void Update(SceneNode node);

        /// <summary>Appends every node whose bounds intersect the frustum.</summary>
        void Query(Frustum frustum, List<SceneNode> results);

        /// <summary>Appends every node whose bounds intersect the box.</summary>
        void Query(in AABB bounds, List<SceneNode> results);

        /// <summary>Returns the bounds covering every node in the set.</summary>
        AABB GetBounds();
    }
}
