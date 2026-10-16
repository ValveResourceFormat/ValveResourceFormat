namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// The shape a hitbox is measured as when finding the point of it closest to a particle.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ClosestPointTestType_t">ClosestPointTestType_t</seealso>
    public enum ClosestPointTestType
    {
        /// <summary>The hitbox's box.</summary>
        PARTICLE_CLOSEST_TYPE_BOX = 0,
        /// <summary>A capsule along the box's longest axis, a quarter of the sum of its extents thick.</summary>
        PARTICLE_CLOSEST_TYPE_CAPSULE = 1,
        /// <summary>A blend from the capsule's closest point toward the box's by the hybrid ratio.</summary>
        PARTICLE_CLOSEST_TYPE_HYBRID = 2,
    }
}
