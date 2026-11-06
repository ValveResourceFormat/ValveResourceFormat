namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Tells where the surfaces of the world model are seen from, which decides how its geometry is split into models.
/// </summary>
internal interface IBrushLightingLocator
{
    /// <summary>Gets whether a point at world coordinates is in the area of the 3D sky box.</summary>
    /// <param name="position">The point.</param>
    bool IsInSky(Vector3 position);

    /// <summary>Gets whether any point of a box is in the area of the 3D sky box.</summary>
    /// <param name="min">The minimum corner of the box.</param>
    /// <param name="max">The maximum corner of the box.</param>
    bool OverlapsSky(Vector3 min, Vector3 max);

    /// <summary>Gets whether every point of a box is in the area of the 3D sky box.</summary>
    /// <param name="min">The minimum corner of the box.</param>
    /// <param name="max">The maximum corner of the box.</param>
    bool IsEntirelyInSky(Vector3 min, Vector3 max);
}
